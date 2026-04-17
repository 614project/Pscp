namespace Pscp.Transpiler;

internal sealed partial class CSharpEmitter
{
    private readonly TranspilationOptions _options;
    private readonly SemanticAnalysisResult? _semantic;
    private readonly CodeWriter _writer = new();
    private readonly Stack<TypeDeclaration> _currentTypeStack = new();
    private readonly HashSet<DeclarationStatement> _hoistedGlobalDeclarations = [];
    private readonly HashSet<string> _declaredTypeNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeclaredTypeShape> _declaredTypeShapes = new(StringComparer.Ordinal);
    private int _temporaryId;

    private sealed record ConstructorShape(int ParameterCount);

    private sealed record DeclaredFieldShape(string Name, TypeSyntax Type);

    private sealed record DeclaredTypeShape(
        string Name,
        bool IsValueType,
        IReadOnlyList<DeclaredFieldShape> Fields,
        IReadOnlyList<ConstructorShape> Constructors);

    public CSharpEmitter(TranspilationOptions options, SemanticAnalysisResult? semantic = null)
    {
        _options = options;
        _semantic = semantic;
    }

    public string Emit(PscpProgram program)
    {
        CollectDeclaredTypeNames(program.Types);
        CollectDeclaredTypeShapes(program.Types, null);
        CollectHoistedGlobalDeclarations(program);

        foreach (string usingLine in CollectUsings(program))
        {
            _writer.WriteLine(usingLine);
        }

        _writer.WriteLine();
        _writer.WriteLine("#nullable enable");
        _writer.WriteLine();
        _writer.WriteLine($"namespace {program.NamespaceName ?? _options.Namespace};");
        _writer.WriteLine();

        foreach (TypeDeclaration type in program.Types)
        {
            EmitTypeDeclaration(type);
            _writer.WriteLine();
        }

        _writer.WriteLine($"public static class {_options.ClassName}");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine("private static readonly __PscpStdin stdin = new();");
        _writer.WriteLine("private static readonly __PscpStdout stdout = new();");
        _writer.WriteLine();

        EmitHoistedGlobalFields();

        foreach (FunctionDeclaration function in program.Functions)
        {
            EmitFunction(function, includeAccessibility: true, isStatic: true);
            _writer.WriteLine();
        }

        EmitMain(program.GlobalStatements);
        _writer.Unindent();
        _writer.WriteLine("}");
        _writer.WriteLine();
        EmitRuntimeHelpers();
        return _writer.ToString();
    }

    private static IEnumerable<string> CollectUsings(PscpProgram program)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        string[] defaults =
        [
            "using System;",
            "using System.Collections.Generic;",
            "using System.Globalization;",
            "using System.IO;",
            "using System.Linq;",
            "using System.Text;",
        ];

        foreach (string line in defaults)
        {
            if (seen.Add(line))
            {
                yield return line;
            }
        }

        foreach (UsingDirective directive in program.Usings)
        {
            string text = directive.Text.TrimEnd();
            if (!text.EndsWith(';'))
            {
                text += ";";
            }

            if (seen.Add(text))
            {
                yield return text;
            }
        }
    }

    private void CollectHoistedGlobalDeclarations(PscpProgram program)
    {
        HashSet<string> referencedNames = [];
        foreach (FunctionDeclaration function in program.Functions)
        {
            CollectReferencedNames(function.Body, referencedNames);
        }

        foreach (DeclarationStatement declaration in program.GlobalStatements.OfType<DeclarationStatement>())
        {
            if (CanHoistGlobalDeclaration(declaration) && IsReferencedOutsideMain(declaration, referencedNames))
            {
                _hoistedGlobalDeclarations.Add(declaration);
            }
        }
    }

    private void EmitHoistedGlobalFields()
    {
        foreach (DeclarationStatement declaration in _hoistedGlobalDeclarations)
        {
            if (!TryGetFieldEntries(declaration, out IReadOnlyList<(string Name, TypeSyntax Type, Expression? Initializer)>? entries))
            {
                continue;
            }

            foreach ((string name, TypeSyntax type, _) in entries!)
            {
                _writer.WriteLine($"private static {EmitType(type)} {name} = default!;");
            }
        }

        if (_hoistedGlobalDeclarations.Count > 0)
        {
            _writer.WriteLine();
        }
    }

    private bool CanHoistGlobalDeclaration(DeclarationStatement declaration)
        => declaration.ExplicitType is not null && TryGetFieldEntries(declaration, out _);

    private static bool IsReferencedOutsideMain(DeclarationStatement declaration, HashSet<string> referencedNames)
        => declaration.Targets.Any(target => TargetContainsReferencedName(target, referencedNames));

    private static bool TargetContainsReferencedName(BindingTarget target, HashSet<string> referencedNames)
        => target switch
        {
            NameTarget nameTarget => referencedNames.Contains(nameTarget.Name),
            TupleTarget tupleTarget => tupleTarget.Elements.Any(element => TargetContainsReferencedName(element, referencedNames)),
            _ => false,
        };

    private void EmitHoistedDeclarationInitialization(DeclarationStatement declaration)
    {
        if (declaration.IsInputShorthand)
        {
            EmitHoistedInputDeclaration(declaration);
            return;
        }

        if (declaration.Targets.Count > 1)
        {
            string initializer = declaration.Initializer is null ? "default!" : EmitExpression(declaration.Initializer, declaration.ExplicitType);
            _writer.WriteLine($"({string.Join(", ", declaration.Targets.Select(EmitBindingPattern))}) = {initializer};");
            return;
        }

        if (declaration.Targets[0] is DiscardTarget)
        {
            if (declaration.Initializer is not null)
            {
                _writer.WriteLine($"_ = {EmitExpression(declaration.Initializer, declaration.ExplicitType)};");
            }

            return;
        }

        string name = EmitBindingPattern(declaration.Targets[0]);
        if (declaration.ExplicitType is SizedArrayTypeSyntax sizedType && declaration.Initializer is null)
        {
            _writer.WriteLine($"{name} = {EmitSizedArrayCreationExpression(sizedType)};");
            return;
        }

        if (declaration.Initializer is not null
            && TryEmitLoweredHoistedDeclarationInitialization(declaration, name))
        {
            return;
        }

        string initializerText = declaration.Initializer is null
            ? EmitImplicitInitializer(declaration.ExplicitType)
            : EmitExpression(declaration.Initializer, declaration.ExplicitType);
        _writer.WriteLine($"{name} = {initializerText};");
    }

    private void EmitHoistedInputDeclaration(DeclarationStatement declaration)
    {
        if (declaration.ExplicitType is null)
        {
            return;
        }

        if (declaration.Targets.Count == 1 && declaration.Targets[0] is TupleTarget tupleTarget)
        {
            _writer.WriteLine($"{EmitBindingPattern(tupleTarget)} = {EmitInputRead(declaration.ExplicitType)};");
            return;
        }

        foreach (BindingTarget target in declaration.Targets)
        {
            string readExpression = EmitInputRead(declaration.ExplicitType);
            if (target is DiscardTarget)
            {
                _writer.WriteLine($"_ = {readExpression};");
            }
            else
            {
                _writer.WriteLine($"{EmitBindingPattern(target)} = {readExpression};");
            }
        }
    }

    private static void CollectReferencedNames(BlockStatement block, HashSet<string> names)
    {
        foreach (Statement statement in block.Statements)
        {
            CollectReferencedNames(statement, names);
        }
    }

    private static void CollectReferencedNames(Statement statement, HashSet<string> names)
    {
        switch (statement)
        {
            case BlockStatement block:
                CollectReferencedNames(block, names);
                break;
            case DeclarationStatement declaration when declaration.Initializer is not null:
                CollectReferencedNames(declaration.Initializer, names);
                break;
            case ExpressionStatement expressionStatement:
                CollectReferencedNames(expressionStatement.Expression, names);
                break;
            case AssignmentStatement assignment:
                CollectReferencedNames(assignment.Target, names);
                CollectReferencedNames(assignment.Value, names);
                break;
            case OutputStatement output:
                CollectReferencedNames(output.Expression, names);
                break;
            case IfStatement ifStatement:
                CollectReferencedNames(ifStatement.Condition, names);
                CollectReferencedNames(ifStatement.ThenBranch, names);
                if (ifStatement.ElseBranch is not null) CollectReferencedNames(ifStatement.ElseBranch, names);
                break;
            case WhileStatement whileStatement:
                CollectReferencedNames(whileStatement.Condition, names);
                CollectReferencedNames(whileStatement.Body, names);
                break;
            case ForInStatement forIn:
                CollectReferencedNames(forIn.Source, names);
                CollectReferencedNames(forIn.Body, names);
                break;
            case FastForStatement fastFor:
                CollectReferencedNames(fastFor.Source, names);
                CollectReferencedNames(fastFor.Body, names);
                break;
            case ReturnStatement { Expression: not null } returnStatement:
                CollectReferencedNames(returnStatement.Expression!, names);
                break;
            case LocalFunctionStatement localFunction:
                CollectReferencedNames(localFunction.Function.Body, names);
                break;
        }
    }

    private static void CollectReferencedNames(Expression expression, HashSet<string> names)
    {
        switch (expression)
        {
            case IdentifierExpression identifier:
                names.Add(identifier.Name);
                break;
            case TupleExpression tuple:
                foreach (Expression element in tuple.Elements) CollectReferencedNames(element, names);
                break;
            case BlockExpression block:
                CollectReferencedNames(block.Block, names);
                break;
            case IfExpression ifExpression:
                CollectReferencedNames(ifExpression.Condition, names);
                CollectReferencedNames(ifExpression.ThenExpression, names);
                CollectReferencedNames(ifExpression.ElseExpression, names);
                break;
            case ConditionalExpression conditional:
                CollectReferencedNames(conditional.Condition, names);
                CollectReferencedNames(conditional.WhenTrue, names);
                CollectReferencedNames(conditional.WhenFalse, names);
                break;
            case UnaryExpression unary:
                CollectReferencedNames(unary.Operand, names);
                break;
            case AssignmentExpression assignment:
                CollectReferencedNames(assignment.Target, names);
                CollectReferencedNames(assignment.Value, names);
                break;
            case PrefixExpression prefix:
                CollectReferencedNames(prefix.Operand, names);
                break;
            case PostfixExpression postfix:
                CollectReferencedNames(postfix.Operand, names);
                break;
            case BinaryExpression binary:
                CollectReferencedNames(binary.Left, names);
                CollectReferencedNames(binary.Right, names);
                break;
            case RangeExpression range:
                CollectReferencedNames(range.Start, names);
                if (range.Step is not null) CollectReferencedNames(range.Step, names);
                CollectReferencedNames(range.End, names);
                break;
            case IsPatternExpression isPattern:
                CollectReferencedNames(isPattern.Left, names);
                if (isPattern.Pattern is ConstantPatternSyntax constantPattern) CollectReferencedNames(constantPattern.Expression, names);
                break;
            case CallExpression call:
                CollectReferencedNames(call.Callee, names);
                foreach (ArgumentSyntax argument in call.Arguments)
                {
                    switch (argument)
                    {
                        case ExpressionArgumentSyntax expressionArgument:
                            CollectReferencedNames(expressionArgument.Expression, names);
                            break;
                    }
                }
                break;
            case MemberAccessExpression member:
                CollectReferencedNames(member.Receiver, names);
                break;
            case IndexExpression index:
                CollectReferencedNames(index.Receiver, names);
                foreach (Expression argument in index.Arguments) CollectReferencedNames(argument, names);
                break;
            case WithExpression @with:
                CollectReferencedNames(@with.Receiver, names);
                break;
            case FromEndExpression fromEnd:
                CollectReferencedNames(fromEnd.Operand, names);
                break;
            case SliceExpression slice:
                if (slice.Start is not null) CollectReferencedNames(slice.Start, names);
                if (slice.End is not null) CollectReferencedNames(slice.End, names);
                break;
            case TupleProjectionExpression projection:
                CollectReferencedNames(projection.Receiver, names);
                break;
            case LambdaExpression lambda:
                switch (lambda.Body)
                {
                    case LambdaExpressionBody expressionBody:
                        CollectReferencedNames(expressionBody.Expression, names);
                        break;
                    case LambdaBlockBody blockBody:
                        CollectReferencedNames(blockBody.Block, names);
                        break;
                }
                break;
            case NewExpression creation:
                foreach (ArgumentSyntax argument in creation.Arguments)
                {
                    if (argument is ExpressionArgumentSyntax expressionArgument)
                    {
                        CollectReferencedNames(expressionArgument.Expression, names);
                    }
                }
                break;
            case NewArrayExpression newArray:
                foreach (Expression dimension in newArray.Dimensions) CollectReferencedNames(dimension, names);
                break;
            case TargetTypedNewArrayExpression targetTypedNewArray:
                foreach (Expression dimension in targetTypedNewArray.Dimensions) CollectReferencedNames(dimension, names);
                break;
            case CollectionExpression collection:
                foreach (CollectionElement element in collection.Elements)
                {
                    switch (element)
                    {
                        case ExpressionElement expressionElement:
                            CollectReferencedNames(expressionElement.Expression, names);
                            break;
                        case RangeElement rangeElement:
                            CollectReferencedNames(rangeElement.Range, names);
                            break;
                        case SpreadElement spreadElement:
                            CollectReferencedNames(spreadElement.Expression, names);
                            break;
                        case BuilderElement builderElement:
                            CollectReferencedNames(builderElement.Source, names);
                            switch (builderElement.Body)
                            {
                                case LambdaExpressionBody expressionBody:
                                    CollectReferencedNames(expressionBody.Expression, names);
                                    break;
                                case LambdaBlockBody blockBody:
                                    CollectReferencedNames(blockBody.Block, names);
                                    break;
                            }
                            break;
                    }
                }
                break;
            case AggregationExpression aggregation:
                CollectReferencedNames(aggregation.Source, names);
                if (aggregation.WhereExpression is not null) CollectReferencedNames(aggregation.WhereExpression, names);
                CollectReferencedNames(aggregation.Body, names);
                break;
            case GeneratorExpression generator:
                CollectReferencedNames(generator.Source, names);
                switch (generator.Body)
                {
                    case LambdaExpressionBody expressionBody:
                        CollectReferencedNames(expressionBody.Expression, names);
                        break;
                    case LambdaBlockBody blockBody:
                        CollectReferencedNames(blockBody.Block, names);
                        break;
                }
                break;
        }
    }

    private void EmitTypeDeclaration(TypeDeclaration declaration)
    {
        _currentTypeStack.Push(declaration);
        if (!declaration.HasBody)
        {
            string header = GetEmittedTypeHeader(declaration);
            _writer.WriteLine(header.EndsWith(";", StringComparison.Ordinal)
                ? header
                : header + ";");
            _currentTypeStack.Pop();
            return;
        }

        _writer.WriteLine(GetEmittedTypeHeader(declaration));
        _writer.WriteLine("{");
        _writer.Indent();
        foreach (TypeMember member in declaration.Members)
        {
            EmitTypeMember(member);
        }
        _writer.Unindent();
        _writer.WriteLine("}");
        _currentTypeStack.Pop();
    }

    private void EmitTypeMember(TypeMember member)
    {
        switch (member)
        {
            case NestedTypeMember nested:
                EmitTypeDeclaration(nested.Declaration);
                break;
            case FieldMember field:
                EmitDeclaration(field.Declaration, isField: true, modifiers: field.Modifiers);
                break;
            case MethodMember method:
                EmitMethodMember(method);
                break;
            case PropertyMember property:
                EmitPropertyMember(property);
                break;
            case OperatorMember @operator:
                EmitOperatorMember(@operator);
                break;
            case OrderingShorthandMember ordering:
                EmitOrderingShorthandMember(ordering);
                break;
        }

        _writer.WriteLine();
    }

    private void EmitOrderingShorthandMember(OrderingShorthandMember ordering)
    {
        string currentType = GetCurrentDeclaringTypeName();
        if (ordering.ParameterNames.Count >= 2)
        {
            string leftName = ordering.ParameterNames[0];
            string rightName = ordering.ParameterNames[1];
            EmitMethodLike($"public static int CompareTo({currentType} {leftName}, {currentType} {rightName})", ordering.Body, isVoidLike: false);
            _writer.WriteLine($"public int CompareTo({currentType} other) => CompareTo(this, other);");
        }
        else
        {
            string otherName = ordering.ParameterNames.Count == 0 ? "other" : ordering.ParameterNames[0];
            EmitMethodLike($"public int CompareTo({currentType} {otherName})", ordering.Body, isVoidLike: false);
            _writer.WriteLine($"public static int CompareTo({currentType} left, {currentType} right) => left.CompareTo(right);");
        }

        _writer.WriteLine($"public static bool operator <({currentType} left, {currentType} right) => CompareTo(left, right) < 0;");
        _writer.WriteLine($"public static bool operator >({currentType} left, {currentType} right) => CompareTo(left, right) > 0;");
        _writer.WriteLine($"public static bool operator <=({currentType} left, {currentType} right) => CompareTo(left, right) <= 0;");
        _writer.WriteLine($"public static bool operator >=({currentType} left, {currentType} right) => CompareTo(left, right) >= 0;");
    }

    private void EmitMethodMember(MethodMember method)
    {
        string modifiers = method.Modifiers.Count == 0 ? "public " : string.Join(" ", method.Modifiers) + " ";
        string parameters = string.Join(", ", method.Parameters.Select(EmitParameter));
        string signature = method.IsConstructor
            ? $"{modifiers}{method.Name}({parameters})"
            : $"{modifiers}{EmitType(method.ReturnType!)} {method.Name}({parameters})";
        if (!string.IsNullOrWhiteSpace(method.InitializerText))
        {
            signature += " " + method.InitializerText;
        }

        EmitMethodLike(signature, method.Body, method.IsConstructor || (method.ReturnType is not null && GetIsVoid(method.ReturnType)));
    }

    private void EmitPropertyMember(PropertyMember property)
    {
        string modifiers = property.Modifiers.Count == 0 ? "public " : string.Join(" ", property.Modifiers) + " ";
        string signature = $"{modifiers}{EmitType(property.Type)} {property.Name}";
        switch (property.Body)
        {
            case ExpressionMethodBody expressionBody:
                _writer.WriteLine($"{signature} => {EmitExpression(expressionBody.Expression)};");
                break;
            case BlockMethodBody blockBody:
                _writer.WriteLine(signature);
                _writer.WriteLine("{");
                _writer.Indent();
                _writer.WriteLine("get");
                _writer.WriteLine("{");
                _writer.Indent();
                EmitBlockContents(blockBody.Block, isVoidLike: false);
                _writer.Unindent();
                _writer.WriteLine("}");
                _writer.Unindent();
                _writer.WriteLine("}");
                break;
        }
    }

    private void EmitOperatorMember(OperatorMember @operator)
    {
        List<string> modifiers = @operator.Modifiers.Count == 0 ? ["public"] : [.. @operator.Modifiers];
        if (!modifiers.Contains("static", StringComparer.Ordinal))
        {
            modifiers.Add("static");
        }

        string signature = $"{string.Join(" ", modifiers)} {EmitType(@operator.ReturnType)} operator {@operator.OperatorTokenText}({string.Join(", ", @operator.Parameters.Select(EmitParameter))})";
        EmitMethodLike(signature, @operator.Body, isVoidLike: GetIsVoid(@operator.ReturnType));
    }

    private void EmitMethodLike(string signature, MethodBody body, bool isVoidLike)
    {
        switch (body)
        {
            case ExpressionMethodBody expressionBody:
                _writer.WriteLine($"{signature} => {EmitExpression(expressionBody.Expression)};");
                break;
            case BlockMethodBody blockBody:
                _writer.WriteLine(signature);
                _writer.WriteLine("{");
                _writer.Indent();
                if (isVoidLike)
                {
                    EmitBlockContents(blockBody.Block, isVoidLike: true);
                }
                else
                {
                    EmitValueAwareBlockContents(blockBody.Block);
                }
                _writer.Unindent();
                _writer.WriteLine("}");
                break;
        }
    }

    private void EmitValueAwareBlockContents(BlockStatement block)
    {
        bool emittedTerminalReturn = false;
        for (int i = 0; i < block.Statements.Count; i++)
        {
            bool isTerminal = i == block.Statements.Count - 1;
            if (EmitValueAwareStatement(block.Statements[i], isTerminal))
            {
                emittedTerminalReturn = isTerminal;
                continue;
            }

            EmitStatement(block.Statements[i]);
        }

        if (!emittedTerminalReturn && !ContainsExplicitReturn(block))
        {
            _writer.WriteLine("return default!;");
        }
    }

    private bool EmitValueAwareStatement(Statement statement, bool isTerminal)
    {
        switch (statement)
        {
            case ExpressionStatement { HasSemicolon: false } expressionStatement when isTerminal:
                _writer.WriteLine($"return {EmitExpression(expressionStatement.Expression)};");
                return true;
            case BlockStatement block when isTerminal:
                _writer.WriteLine("{");
                _writer.Indent();
                EmitValueAwareBlockContents(block);
                _writer.Unindent();
                _writer.WriteLine("}");
                return true;
            case IfStatement ifStatement when ifStatement.ElseBranch is null && CanEmitReturningStatement(ifStatement.ThenBranch):
                _writer.WriteLine($"if ({EmitExpression(ifStatement.Condition)})");
                EmitReturningEmbeddedStatement(ifStatement.ThenBranch);
                return true;
            case IfStatement ifStatement when isTerminal && ifStatement.ElseBranch is not null && CanEmitReturningStatement(ifStatement.ThenBranch) && CanEmitReturningStatement(ifStatement.ElseBranch):
                _writer.WriteLine($"if ({EmitExpression(ifStatement.Condition)})");
                EmitReturningEmbeddedStatement(ifStatement.ThenBranch);
                _writer.WriteLine("else");
                EmitReturningEmbeddedStatement(ifStatement.ElseBranch);
                return true;
            default:
                return false;
        }
    }

    private bool CanEmitReturningStatement(Statement statement)
        => statement switch
        {
            ExpressionStatement { HasSemicolon: false } => true,
            BlockStatement block => block.Statements.Count > 0 && CanEmitReturningStatement(block.Statements[^1]),
            IfStatement ifStatement when ifStatement.ElseBranch is not null
                => CanEmitReturningStatement(ifStatement.ThenBranch) && CanEmitReturningStatement(ifStatement.ElseBranch),
            _ => false,
        };

    private void EmitReturningEmbeddedStatement(Statement statement)
    {
        if (statement is ExpressionStatement { HasSemicolon: false } expressionStatement)
        {
            _writer.WriteLine("{");
            _writer.Indent();
            _writer.WriteLine($"return {EmitExpression(expressionStatement.Expression)};");
            _writer.Unindent();
            _writer.WriteLine("}");
            return;
        }

        if (statement is BlockStatement block)
        {
            _writer.WriteLine("{");
            _writer.Indent();
            EmitValueAwareBlockContents(block);
            _writer.Unindent();
            _writer.WriteLine("}");
            return;
        }

        if (statement is IfStatement nestedIf && nestedIf.ElseBranch is not null && CanEmitReturningStatement(nestedIf.ThenBranch) && CanEmitReturningStatement(nestedIf.ElseBranch))
        {
            _writer.WriteLine("{");
            _writer.Indent();
            EmitValueAwareStatement(nestedIf, isTerminal: true);
            _writer.Unindent();
            _writer.WriteLine("}");
            return;
        }

        EmitEmbeddedStatement(statement);
    }

    private void EmitFunction(FunctionDeclaration function, bool includeAccessibility, bool isStatic)
    {
        string parameters = string.Join(", ", function.Parameters.Select(EmitParameter));
        string prefix = includeAccessibility ? "public " : string.Empty;
        if (isStatic)
        {
            prefix += "static ";
        }

        _writer.WriteLine($"{prefix}{EmitType(function.ReturnType)} {function.Name}({parameters})");
        _writer.WriteLine("{");
        _writer.Indent();
        EmitBlockContents(function.Body, GetIsVoid(function.ReturnType));
        _writer.Unindent();
        _writer.WriteLine("}");
    }

    private void EmitMain(IReadOnlyList<Statement> statements)
    {
        _writer.WriteLine("public static void Main()");
        _writer.WriteLine("{");
        _writer.Indent();
        EmitTopLevelBlockContents(new BlockStatement(statements));
        _writer.Unindent();
        _writer.WriteLine("}");
    }

    private void EmitTopLevelBlockContents(BlockStatement block)
    {
        Expression? implicitReturn = GetImplicitReturnExpression(block);
        int regularCount = implicitReturn is null ? block.Statements.Count : block.Statements.Count - 1;

        for (int i = 0; i < regularCount; i++)
        {
            if (block.Statements[i] is DeclarationStatement declaration && _hoistedGlobalDeclarations.Contains(declaration))
            {
                EmitHoistedDeclarationInitialization(declaration);
            }
            else
            {
                EmitStatement(block.Statements[i]);
            }
        }

        if (implicitReturn is not null)
        {
            _writer.WriteLine($"{EmitExpression(implicitReturn)};");
        }
    }

    private void EmitBlockContents(BlockStatement block, bool isVoidLike)
    {
        Expression? implicitReturn = GetImplicitReturnExpression(block);
        int regularCount = implicitReturn is null ? block.Statements.Count : block.Statements.Count - 1;

        for (int i = 0; i < regularCount; i++)
        {
            EmitStatement(block.Statements[i]);
        }

        if (implicitReturn is not null)
        {
            if (isVoidLike)
            {
                _writer.WriteLine($"{EmitExpression(implicitReturn)};");
            }
            else
            {
                _writer.WriteLine($"return {EmitExpression(implicitReturn)};");
            }
        }
        else if (!isVoidLike && !ContainsExplicitReturn(block))
        {
            _writer.WriteLine("return default!;");
        }
    }

    private void EmitStatement(Statement statement)
    {
        switch (statement)
        {
            case BlockStatement block:
                _writer.WriteLine("{");
                _writer.Indent();
                EmitBlockContents(block, isVoidLike: true);
                _writer.Unindent();
                _writer.WriteLine("}");
                break;
            case DeclarationStatement declaration:
                EmitDeclaration(declaration, isField: false, modifiers: null);
                break;
            case ExpressionStatement expressionStatement:
                _writer.WriteLine($"{EmitStatementExpression(expressionStatement.Expression)};");
                break;
            case AssignmentStatement assignment:
                EmitAssignment(assignment);
                break;
            case OutputStatement output:
                if (!TryEmitLoweredOutput(output))
                {
                    _writer.WriteLine($"stdout.{(output.Kind == OutputKind.Write ? "write" : "writeln")}({EmitExpression(output.Expression)});");
                }
                break;
            case IfStatement ifStatement:
                EmitIfStatement(ifStatement);
                break;
            case WhileStatement whileStatement:
                _writer.WriteLine($"while ({EmitExpression(whileStatement.Condition)})");
                EmitEmbeddedStatement(whileStatement.Body);
                break;
            case ForInStatement forIn:
                EmitForInStatement(forIn);
                break;
            case CStyleForStatement cStyleFor:
                _writer.WriteLine($"for ({cStyleFor.HeaderText})");
                EmitEmbeddedStatement(cStyleFor.Body);
                break;
            case FastForStatement fastFor:
                EmitFastForStatement(fastFor);
                break;
            case ReturnStatement returnStatement:
                _writer.WriteLine(returnStatement.Expression is null
                    ? "return;"
                    : $"return {EmitExpression(returnStatement.Expression)};");
                break;
            case BreakStatement:
                _writer.WriteLine("break;");
                break;
            case ContinueStatement:
                _writer.WriteLine("continue;");
                break;
            case LocalFunctionStatement localFunction:
                EmitFunction(localFunction.Function, includeAccessibility: false, isStatic: false);
                break;
        }
    }

    private void EmitEmbeddedStatement(Statement statement)
    {
        if (statement is BlockStatement block)
        {
            EmitStatement(block);
            return;
        }

        _writer.WriteLine("{");
        _writer.Indent();
        EmitStatement(statement);
        _writer.Unindent();
        _writer.WriteLine("}");
    }

    private void EmitDeclaration(DeclarationStatement declaration, bool isField, IReadOnlyList<string>? modifiers)
    {
        string prefix = modifiers is { Count: > 0 }
            ? string.Join(" ", modifiers) + " "
            : isField ? GetImplicitFieldAccessibilityPrefix() : string.Empty;

        if (declaration.IsInputShorthand)
        {
            if (isField)
            {
                _writer.WriteLine($"{prefix}// input shorthand is not supported for fields");
            }
            else
            {
                EmitInputDeclaration(declaration);
            }
            return;
        }

        if (isField)
        {
            EmitFieldDeclaration(prefix, declaration);
            return;
        }

        if (TryEmitAmbiguousCallLikeDeclaration(declaration))
        {
            return;
        }

        if (declaration.Targets.Count == 1 && declaration.Targets[0] is TupleTarget tupleTarget)
        {
            string initializer = declaration.Initializer is null ? "default!" : EmitExpression(declaration.Initializer, declaration.ExplicitType);
            _writer.WriteLine($"var {EmitBindingPattern(tupleTarget)} = {initializer};");
            return;
        }

        if (declaration.Targets.Count > 1)
        {
            EmitMultiDeclaration(declaration);
            return;
        }

        BindingTarget target = declaration.Targets[0];
        if (target is DiscardTarget)
        {
            if (declaration.Initializer is not null)
            {
                _writer.WriteLine($"_ = {EmitExpression(declaration.Initializer, declaration.ExplicitType)};");
            }

            return;
        }

        string name = EmitBindingPattern(target);
        if (declaration.ExplicitType is SizedArrayTypeSyntax sizedType && declaration.Initializer is null)
        {
            EmitSizedArrayDeclaration(name, sizedType);
            return;
        }

        if (declaration.Initializer is not null
            && TryEmitLoweredDeclarationInitializer(declaration, name))
        {
            return;
        }

        string typeText = declaration.ExplicitType is null ? "var" : EmitType(NormalizeSizedType(declaration.ExplicitType));
        string initializerText = declaration.Initializer is null
            ? EmitImplicitInitializer(declaration.ExplicitType)
            : EmitExpression(declaration.Initializer, declaration.ExplicitType);
        _writer.WriteLine($"{typeText} {name} = {initializerText};");
    }

    private string GetImplicitFieldAccessibilityPrefix()
    {
        if (_currentTypeStack.Count == 0)
        {
            return string.Empty;
        }

        return IsValueTypeDeclaration(_currentTypeStack.Peek().HeaderText) ? "public " : string.Empty;
    }

    private void CollectDeclaredTypeNames(IReadOnlyList<TypeDeclaration> types)
    {
        foreach (TypeDeclaration type in types)
        {
            _declaredTypeNames.Add(type.Name);
            CollectDeclaredTypeNames(type.Members.OfType<NestedTypeMember>().Select(member => member.Declaration).ToArray());
        }
    }

    private void CollectDeclaredTypeShapes(IReadOnlyList<TypeDeclaration> types, string? containerName)
    {
        foreach (TypeDeclaration type in types)
        {
            string fullName = string.IsNullOrWhiteSpace(containerName)
                ? type.Name
                : containerName + "." + type.Name;
            DeclaredTypeShape shape = CreateDeclaredTypeShape(type);
            _declaredTypeShapes[fullName] = shape;
            _declaredTypeShapes.TryAdd(type.Name, shape);
            CollectDeclaredTypeShapes(type.Members.OfType<NestedTypeMember>().Select(member => member.Declaration).ToArray(), fullName);
        }
    }

    private DeclaredTypeShape CreateDeclaredTypeShape(TypeDeclaration declaration)
    {
        List<DeclaredFieldShape> fields = [];
        foreach (FieldMember field in declaration.Members.OfType<FieldMember>())
        {
            if (!TryGetFieldEntries(field.Declaration, out IReadOnlyList<(string Name, TypeSyntax Type, Expression? Initializer)>? entries))
            {
                continue;
            }

            foreach ((string name, TypeSyntax type, _) in entries!)
            {
                fields.Add(new DeclaredFieldShape(name, type));
            }
        }

        List<ConstructorShape> constructors = [];
        if (TryCountHeaderParameters(declaration.HeaderText, out int headerParameterCount))
        {
            constructors.Add(new ConstructorShape(headerParameterCount));
        }

        foreach (MethodMember constructor in declaration.Members.OfType<MethodMember>().Where(member => member.IsConstructor))
        {
            constructors.Add(new ConstructorShape(constructor.Parameters.Count));
        }

        return new DeclaredTypeShape(
            declaration.Name,
            IsValueTypeDeclaration(declaration.HeaderText),
            fields,
            constructors);
    }

    private static bool TryCountHeaderParameters(string headerText, out int parameterCount)
    {
        parameterCount = 0;
        int openParen = headerText.IndexOf('(');
        int closeParen = headerText.LastIndexOf(')');
        if (openParen < 0 || closeParen <= openParen)
        {
            return false;
        }

        string parameterText = headerText[(openParen + 1)..closeParen].Trim();
        if (parameterText.Length == 0)
        {
            return true;
        }

        int depth = 0;
        parameterCount = 1;
        foreach (char ch in parameterText)
        {
            switch (ch)
            {
                case '<':
                case '(':
                case '[':
                case '{':
                    depth++;
                    break;
                case '>':
                case ')':
                case ']':
                case '}':
                    if (depth > 0)
                    {
                        depth--;
                    }
                    break;
                case ',' when depth == 0:
                    parameterCount++;
                    break;
            }
        }

        return true;
    }

    private static bool IsValueTypeDeclaration(string headerText)
    {
        string normalized = headerText.TrimStart();
        return normalized.StartsWith("struct ", StringComparison.Ordinal)
            || normalized.StartsWith("readonly struct ", StringComparison.Ordinal)
            || normalized.StartsWith("record struct ", StringComparison.Ordinal)
            || normalized.StartsWith("readonly record struct ", StringComparison.Ordinal);
    }

    private bool TryGetDeclaredTypeShape(TypeSyntax? type, out DeclaredTypeShape? shape)
    {
        shape = null;
        if (UnwrapNullableType(type) is not NamedTypeSyntax named)
        {
            return false;
        }

        if (_declaredTypeShapes.TryGetValue(named.Name, out shape))
        {
            return true;
        }

        if (_declaredTypeShapes.TryGetValue(PscpIntrinsicCatalog.StripGenericSuffix(named.Name), out shape))
        {
            return true;
        }

        return false;
    }

    private string EmitStatementExpression(Expression expression)
        => expression switch
        {
            AssignmentExpression assignmentExpression => EmitStatementAssignmentExpression(assignmentExpression),
            PrefixExpression prefix when !TryEmitKnownDataStructurePop(prefix.Operand, out _) => EmitScalarPrefixStatementExpression(prefix),
            PrefixExpression prefix => EmitPrefixExpression(prefix),
            PostfixExpression postfix => EmitPostfixStatementExpression(postfix),
            _ => EmitExpression(expression),
        };

    private string EmitScalarPrefixStatementExpression(PrefixExpression prefix)
        => prefix.Operator == PostfixOperator.Increment
            ? $"++{EmitExpression(prefix.Operand)}"
            : $"--{EmitExpression(prefix.Operand)}";

    private bool TryEmitAmbiguousCallLikeDeclaration(DeclarationStatement declaration)
    {
        if (declaration.IsInputShorthand
            || declaration.Initializer is not null
            || declaration.Targets.Count != 1
            || declaration.Targets[0] is not NameTarget nameTarget
            || declaration.ExplicitType is not NamedTypeSyntax named
            || named.TypeArguments.Count != 0
            || named.Name.Contains('.', StringComparison.Ordinal)
            || PscpIntrinsicCatalog.BuiltinTypes.Contains(named.Name)
            || _declaredTypeNames.Contains(named.Name))
        {
            return false;
        }

        _writer.WriteLine($"{named.Name}({nameTarget.Name});");
        return true;
    }

    private void EmitFieldDeclaration(string prefix, DeclarationStatement declaration)
    {
        if (TryGetFieldEntries(declaration, out IReadOnlyList<(string Name, TypeSyntax Type, Expression? Initializer)>? entries))
        {
            foreach ((string fieldName, TypeSyntax fieldType, Expression? fieldInitializer) in entries!)
            {
                string emittedTypeText = EmitType(fieldType);
                if (fieldInitializer is null)
                {
                    if (fieldType is NamedTypeSyntax namedFieldType && IsKnownAutoConstructType(namedFieldType))
                    {
                        _writer.WriteLine($"{prefix}{emittedTypeText} {fieldName} = new();");
                    }
                    else
                    {
                        _writer.WriteLine($"{prefix}{emittedTypeText} {fieldName};");
                    }
                }
                else
                {
                    string initializerText = EmitExpression(fieldInitializer, fieldType);
                    _writer.WriteLine($"{prefix}{emittedTypeText} {fieldName} = {initializerText};");
                }
            }

            return;
        }

        if (declaration.Targets.Count != 1 || declaration.Targets[0] is not NameTarget and not DiscardTarget)
        {
            string comment = declaration.Initializer is null
                ? "unsupported field declaration"
                : EmitExpression(declaration.Initializer, declaration.ExplicitType);
            _writer.WriteLine($"{prefix}// {comment}");
            return;
        }

        if (declaration.Targets[0] is DiscardTarget)
        {
            return;
        }

        string name = EmitBindingPattern(declaration.Targets[0]);
        string typeText = declaration.ExplicitType is null ? "object" : EmitType(NormalizeSizedType(declaration.ExplicitType));
        string initializer = declaration.Initializer is null
            ? EmitImplicitInitializer(declaration.ExplicitType)
            : EmitExpression(declaration.Initializer, declaration.ExplicitType);

        if (declaration.ExplicitType is SizedArrayTypeSyntax sizedType && declaration.Initializer is null)
        {
            initializer = EmitSizedArrayCreationExpression(sizedType);
            typeText = EmitType(new ArrayTypeSyntax(sizedType.ElementType, sizedType.Dimensions.Count));
        }

        if (declaration.Initializer is null
            && !(declaration.ExplicitType is NamedTypeSyntax namedExplicitType && IsKnownAutoConstructType(namedExplicitType)))
        {
            _writer.WriteLine($"{prefix}{typeText} {name};");
            return;
        }

        _writer.WriteLine($"{prefix}{typeText} {name} = {initializer};");
    }

    private bool TryGetFieldEntries(DeclarationStatement declaration, out IReadOnlyList<(string Name, TypeSyntax Type, Expression? Initializer)>? entries)
    {
        List<(string Name, TypeSyntax Type, Expression? Initializer)> result = [];
        entries = result;
        if (declaration.ExplicitType is null)
        {
            return false;
        }

        TypeSyntax normalizedType = NormalizeSizedType(declaration.ExplicitType);
        if (declaration.Targets.Count == 1)
        {
            if (declaration.Targets[0] is NameTarget nameTarget)
            {
                result.Add((nameTarget.Name, normalizedType, declaration.Initializer));
                return true;
            }

            if (declaration.Targets[0] is TupleTarget tupleTarget && normalizedType is TupleTypeSyntax tupleType)
            {
                for (int i = 0; i < tupleTarget.Elements.Count && i < tupleType.Elements.Count; i++)
                {
                    if (tupleTarget.Elements[i] is NameTarget elementName)
                    {
                        Expression? elementInitializer = declaration.Initializer is TupleExpression tupleInitializer && i < tupleInitializer.Elements.Count
                            ? tupleInitializer.Elements[i]
                            : null;
                        result.Add((elementName.Name, tupleType.Elements[i], elementInitializer));
                    }
                }

                return result.Count > 0;
            }

            return false;
        }

        if (declaration.Initializer is TupleExpression tupleExpression)
        {
            for (int i = 0; i < declaration.Targets.Count; i++)
            {
                if (declaration.Targets[i] is NameTarget nameTarget)
                {
                    TypeSyntax targetType = normalizedType is TupleTypeSyntax tupleType && i < tupleType.Elements.Count
                        ? tupleType.Elements[i]
                        : normalizedType;
                    Expression? initializer = i < tupleExpression.Elements.Count ? tupleExpression.Elements[i] : null;
                    result.Add((nameTarget.Name, targetType, initializer));
                }
            }

            return result.Count > 0;
        }

        foreach (BindingTarget target in declaration.Targets)
        {
            if (target is NameTarget nameTarget)
            {
                result.Add((nameTarget.Name, normalizedType, declaration.Initializer));
            }
        }

        return result.Count > 0;
    }

    private string EmitImplicitInitializer(TypeSyntax? explicitType)
    {
        if (explicitType is NamedTypeSyntax named && IsKnownAutoConstructType(named))
        {
            return "new()";
        }

        return "default!";
    }

    private bool TryEmitLoweredDeclarationInitializer(DeclarationStatement declaration, string name)
    {
        if (declaration.Initializer is null)
        {
            return false;
        }

        TypeSyntax? declaredType = NormalizeSizedTypeOrNull(declaration.ExplicitType ?? _semantic?.GetExpressionType(declaration.Initializer));
        if (declaredType is null)
        {
            return false;
        }

        string typeText = EmitType(declaredType);
        return declaration.Initializer switch
        {
            TargetTypedNewArrayExpression targetTypedNewArray => TryEmitLoweredTargetTypedNewArrayDeclaration(name, typeText, declaredType, targetTypedNewArray),
            CollectionExpression collection => TryEmitLoweredCollectionDeclaration(name, typeText, declaredType, collection),
            CallExpression call => TryEmitLoweredCallDeclaration(name, typeText, declaredType, call),
            AggregationExpression aggregation => TryEmitLoweredAggregationDeclaration(name, typeText, declaredType, aggregation),
            _ => false,
        };
    }

    private bool TryEmitLoweredHoistedDeclarationInitialization(DeclarationStatement declaration, string name)
    {
        if (declaration.Initializer is not TargetTypedNewArrayExpression targetTypedNewArray)
        {
            return false;
        }

        TypeSyntax? declaredType = NormalizeSizedTypeOrNull(declaration.ExplicitType ?? _semantic?.GetExpressionType(declaration.Initializer));
        if (declaredType is not ArrayTypeSyntax arrayType
            || targetTypedNewArray.Dimensions.Count != 1
            || !targetTypedNewArray.AutoConstructElements
            || arrayType.ElementType is not NamedTypeSyntax namedElementType
            || !IsKnownAutoConstructType(namedElementType))
        {
            return false;
        }

        string lengthName = NextTemporary("length");
        string indexName = NextTemporary("i");
        string elementTypeText = EmitType(arrayType.ElementType);
        _writer.WriteLine($"int {lengthName} = {EmitExpression(targetTypedNewArray.Dimensions[0])};");
        _writer.WriteLine($"{name} = new {elementTypeText}[{lengthName}];");
        _writer.WriteLine($"for (int {indexName} = 0; {indexName} < {lengthName}; {indexName}++)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine($"{name}[{indexName}] = new {elementTypeText}();");
        _writer.Unindent();
        _writer.WriteLine("}");
        return true;
    }

    private bool TryEmitLoweredTargetTypedNewArrayDeclaration(string name, string typeText, TypeSyntax declaredType, TargetTypedNewArrayExpression targetTypedNewArray)
    {
        if (declaredType is not ArrayTypeSyntax arrayType
            || targetTypedNewArray.Dimensions.Count != 1
            || !targetTypedNewArray.AutoConstructElements
            || arrayType.ElementType is not NamedTypeSyntax namedElementType
            || !IsKnownAutoConstructType(namedElementType))
        {
            return false;
        }

        string lengthName = NextTemporary("length");
        string indexName = NextTemporary("i");
        string elementTypeText = EmitType(arrayType.ElementType);
        _writer.WriteLine($"int {lengthName} = {EmitExpression(targetTypedNewArray.Dimensions[0])};");
        _writer.WriteLine($"{typeText} {name} = new {elementTypeText}[{lengthName}];");
        _writer.WriteLine($"for (int {indexName} = 0; {indexName} < {lengthName}; {indexName}++)");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine($"{name}[{indexName}] = new {elementTypeText}();");
        _writer.Unindent();
        _writer.WriteLine("}");
        return true;
    }

    private bool TryEmitLoweredCollectionDeclaration(string name, string typeText, TypeSyntax declaredType, CollectionExpression collection)
    {
        TypeSyntax? elementType = GetCollectionElementType(declaredType);
        if (elementType is null)
        {
            return false;
        }

        if (declaredType is ArrayTypeSyntax
            && collection.Elements.Count == 1)
        {
            switch (collection.Elements[0])
            {
                case RangeElement rangeElement when !IsLongRange(rangeElement.Range, declaredType):
                {
                    string itemName = NextTemporary("value");
                    EmitRangeArrayInitialization(name, typeText, elementType, rangeElement.Range, itemName, null, itemName);
                    return true;
                }
                case BuilderElement builderElement when builderElement.Source is RangeExpression range && !IsLongRange(range, declaredType):
                {
                    string itemName = ChooseBindingName(builderElement.ItemTarget, "item");
                    string? indexName = builderElement.IndexTarget is null ? null : ChooseBindingName(builderElement.IndexTarget, "index");
                    string valueExpression = EmitLambdaBodyExpression(builderElement.Body, builderElement.IndexTarget, indexName, builderElement.ItemTarget, itemName);
                    EmitRangeArrayInitialization(name, typeText, elementType, range, itemName, indexName, valueExpression);
                    return true;
                }
            }
        }

        bool isList = declaredType is NamedTypeSyntax named && IsListType(named);
        bool isLinkedList = declaredType is NamedTypeSyntax linked && IsLinkedListType(linked);
        string tempCollection = isList || isLinkedList ? name : NextTemporary("items");
        string addCall(string value) => isLinkedList ? $"{tempCollection}.AddLast({value});" : $"{tempCollection}.Add({value});";

        _writer.WriteLine(isLinkedList
            ? $"{typeText} {tempCollection} = new();"
            : isList
                ? $"{typeText} {tempCollection} = new();"
                : $"List<{EmitType(elementType)}> {tempCollection} = new();");

        foreach (CollectionElement element in collection.Elements)
        {
            switch (element)
            {
                case ExpressionElement expressionElement:
                    _writer.WriteLine(addCall(EmitExpression(expressionElement.Expression, elementType)));
                    break;
                case SpreadElement spreadElement:
                {
                    string spreadName = NextTemporary("spread");
                    _writer.WriteLine($"foreach (var {spreadName} in {EmitExpression(spreadElement.Expression)})");
                    _writer.WriteLine("{");
                    _writer.Indent();
                    _writer.WriteLine(addCall(spreadName));
                    _writer.Unindent();
                    _writer.WriteLine("}");
                    break;
                }
                case RangeElement rangeElement:
                {
                    string itemName = NextTemporary("value");
                    _writer.WriteLine(EmitLoopOverSource(rangeElement.Range, itemName, addCall(itemName)));
                    break;
                }
                case BuilderElement builderElement:
                {
                    string itemName = ChooseBindingName(builderElement.ItemTarget, "item");
                    string? indexName = builderElement.IndexTarget is null ? null : ChooseBindingName(builderElement.IndexTarget, "index");
                    string valueExpression = EmitLambdaBodyExpression(builderElement.Body, builderElement.IndexTarget, indexName, builderElement.ItemTarget, itemName);
                    _writer.WriteLine(EmitLoopOverSource(builderElement.Source, itemName, addCall(valueExpression), indexName));
                    break;
                }
            }
        }

        if (!isList && !isLinkedList)
        {
            _writer.WriteLine($"{typeText} {name} = {tempCollection}.ToArray();");
        }

        return true;
    }

    private void EmitRangeArrayInitialization(string name, string typeText, TypeSyntax elementType, RangeExpression range, string itemName, string? indexName, string valueExpression)
    {
        string startName = NextTemporary("start");
        string endName = NextTemporary("end");
        string stepName = NextTemporary("step");
        string countName = NextTemporary("count");
        string absStepName = NextTemporary("absStep");
        string slotName = NextTemporary("slot");
        string forwardCount = range.Kind == RangeKind.RightExclusive
            ? $"{startName} < {endName} ? (({endName} - {startName} - 1) / {stepName}) + 1 : 0"
            : $"{startName} <= {endName} ? (({endName} - {startName}) / {stepName}) + 1 : 0";
        string backwardCount = range.Kind == RangeKind.RightExclusive
            ? $"{startName} > {endName} ? (({startName} - {endName} - 1) / {absStepName}) + 1 : 0"
            : $"{startName} >= {endName} ? (({startName} - {endName}) / {absStepName}) + 1 : 0";
        string forwardOp = range.Kind == RangeKind.RightExclusive ? "<" : "<=";
        string backwardOp = range.Kind == RangeKind.RightExclusive ? ">" : ">=";

        _writer.WriteLine($"int {startName} = {EmitExpression(range.Start)};");
        _writer.WriteLine($"int {endName} = {EmitExpression(range.End)};");
        _writer.WriteLine($"int {stepName} = {EmitRangeStepExpression(range, startName, endName, useLong: false)};");
        _writer.WriteLine($"if ({stepName} == 0) throw new InvalidOperationException(\"Range step cannot be zero.\");");
        _writer.WriteLine($"int {absStepName} = {stepName} > 0 ? {stepName} : -{stepName};");
        _writer.WriteLine($"int {countName} = {stepName} > 0 ? {forwardCount} : {backwardCount};");
        _writer.WriteLine($"{typeText} {name} = new {EmitType(elementType)}[{countName}];");
        _writer.WriteLine($"int {slotName} = 0;");
        string loopHeader = indexName is null
            ? $"for (int {itemName} = {startName}; {stepName} > 0 ? {itemName} {forwardOp} {endName} : {itemName} {backwardOp} {endName}; {itemName} += {stepName})"
            : $"for (int {itemName} = {startName}, {indexName} = 0; {stepName} > 0 ? {itemName} {forwardOp} {endName} : {itemName} {backwardOp} {endName}; {itemName} += {stepName}, {indexName}++)";
        _writer.WriteLine(loopHeader);
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine($"{name}[{slotName}++] = {valueExpression};");
        _writer.Unindent();
        _writer.WriteLine("}");
    }

    private bool TryEmitLoweredCallDeclaration(string name, string typeText, TypeSyntax declaredType, CallExpression call)
    {
        string? intrinsicName = call.Callee switch
        {
            IdentifierExpression identifierExpression => identifierExpression.Name,
            MemberAccessExpression memberAccess when PscpIntrinsicCatalog.IntrinsicCallNames.Contains(memberAccess.MemberName) => memberAccess.MemberName,
            _ => null,
        };
        Expression? receiver = call.Callee is MemberAccessExpression member ? member.Receiver : null;
        if (intrinsicName is null)
        {
            return false;
        }

        if (intrinsicName == "sum" && TryGetSourceOnlyCall(receiver, call.Arguments, out Expression? sumSource))
        {
            string itemName = sumSource is GeneratorExpression generator ? ChooseBindingName(generator.ItemTarget, "item") : NextTemporary("item");
            _writer.WriteLine($"{typeText} {name} = default;");
            if (sumSource is GeneratorExpression sumGenerator)
            {
                string? indexName = sumGenerator.IndexTarget is null ? null : ChooseBindingName(sumGenerator.IndexTarget, "index");
                string valueExpression = EmitLambdaBodyExpression(sumGenerator.Body, sumGenerator.IndexTarget, indexName, sumGenerator.ItemTarget, itemName);
                _writer.WriteLine(EmitLoopOverSource(sumGenerator.Source, itemName, $"{name} += {valueExpression};", indexName));
            }
            else
            {
                _writer.WriteLine(EmitLoopOverSource(sumSource!, itemName, $"{name} += {itemName};"));
            }
            return true;
        }

        if ((intrinsicName == "min" || intrinsicName == "max") && TryGetSourceOnlyCall(receiver, call.Arguments, out Expression? minMaxSource))
        {
            string itemName = NextTemporary("item");
            string hasValueName = NextTemporary("hasValue");
            _writer.WriteLine($"bool {hasValueName} = false;");
            _writer.WriteLine($"{typeText} {name} = default!;");
            _writer.WriteLine(EmitLoopOverSource(minMaxSource!, itemName, $"if (!{hasValueName} || {EmitPreferredComparison(itemName, name, declaredType, preferLower: intrinsicName == "min")}) {{ {name} = {itemName}; {hasValueName} = true; }}"));
            return true;
        }

        if (intrinsicName == "sumBy" && TryGetSourceAndUnaryLambdaCall(receiver, call.Arguments, out Expression? sumBySource, out LambdaExpression? sumBySelector))
        {
            string itemName = ChooseBindingName(sumBySelector!.Parameters[0].Target, "item");
            string selectorExpression = EmitLambdaBodyExpression(sumBySelector.Body, null, null, sumBySelector.Parameters[0].Target, itemName);
            _writer.WriteLine($"{typeText} {name} = default;");
            _writer.WriteLine(EmitLoopOverSource(sumBySource!, itemName, $"{name} += {selectorExpression};"));
            return true;
        }

        if ((intrinsicName == "minBy" || intrinsicName == "maxBy") && TryGetSourceAndUnaryLambdaCall(receiver, call.Arguments, out Expression? minBySource, out LambdaExpression? minBySelector))
        {
            TypeSyntax? keyType = minBySelector!.Body switch
            {
                LambdaExpressionBody expressionBody => _semantic?.GetExpressionType(expressionBody.Expression),
                LambdaBlockBody blockBody => blockBody.Block.Statements.LastOrDefault() is ExpressionStatement { HasSemicolon: false } tail
                    ? _semantic?.GetExpressionType(tail.Expression)
                    : null,
                _ => null,
            };

            if (keyType is null)
            {
                return false;
            }

            string itemName = ChooseBindingName(minBySelector.Parameters[0].Target, "item");
            string selectorExpression = EmitLambdaBodyExpression(minBySelector.Body, null, null, minBySelector.Parameters[0].Target, itemName);
            string hasValueName = NextTemporary("hasValue");
            string bestKeyName = NextTemporary("bestKey");
            string keyName = NextTemporary("key");
            _writer.WriteLine($"bool {hasValueName} = false;");
            _writer.WriteLine($"{typeText} {name} = default!;");
            _writer.WriteLine($"{EmitType(keyType)} {bestKeyName} = default!;");
            _writer.WriteLine(EmitLoopOverSource(minBySource!, itemName, $"var {keyName} = {selectorExpression}; if (!{hasValueName} || {EmitPreferredComparison(keyName, bestKeyName, keyType, preferLower: intrinsicName == "minBy")}) {{ {name} = {itemName}; {bestKeyName} = {keyName}; {hasValueName} = true; }}"));
            return true;
        }

        if (TryGetSourceWithOptionalUnaryLambdaCall(receiver, call.Arguments, out Expression? predicateSource, out LambdaExpression? predicate))
        {
            string itemName = predicate is null ? NextTemporary("item") : ChooseBindingName(predicate.Parameters[0].Target, "item");
            string predicateExpression = predicate is null ? "true" : EmitLambdaBodyExpression(predicate.Body, null, null, predicate.Parameters[0].Target, itemName);
            switch (intrinsicName)
            {
                case "count":
                    _writer.WriteLine($"{typeText} {name} = 0;");
                    _writer.WriteLine(EmitLoopOverSource(predicateSource!, itemName, $"if ({predicateExpression}) {{ {name}++; }}"));
                    return true;
                case "any":
                    _writer.WriteLine($"{typeText} {name} = false;");
                    _writer.WriteLine(EmitLoopOverSource(predicateSource!, itemName, $"if ({predicateExpression}) {{ {name} = true; break; }}"));
                    return true;
                case "all":
                    _writer.WriteLine($"{typeText} {name} = true;");
                    _writer.WriteLine(EmitLoopOverSource(predicateSource!, itemName, $"if (!({predicateExpression})) {{ {name} = false; break; }}"));
                    return true;
                case "find":
                    _writer.WriteLine($"{typeText} {name} = default!;");
                    _writer.WriteLine(EmitLoopOverSource(predicateSource!, itemName, $"if ({predicateExpression}) {{ {name} = {itemName}; break; }}"));
                    return true;
                case "findIndex":
                {
                    string indexName = NextTemporary("index");
                    _writer.WriteLine($"{typeText} {name} = -1;");
                    _writer.WriteLine(EmitLoopOverSource(predicateSource!, itemName, $"if ({predicateExpression}) {{ {name} = {indexName}; break; }}", indexName));
                    return true;
                }
                case "findLastIndex":
                {
                    string indexName = NextTemporary("index");
                    _writer.WriteLine($"{typeText} {name} = -1;");
                    _writer.WriteLine(EmitLoopOverSource(predicateSource!, itemName, $"if ({predicateExpression}) {{ {name} = {indexName}; }}", indexName));
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryEmitLoweredAggregationDeclaration(string name, string typeText, TypeSyntax declaredType, AggregationExpression aggregation)
    {
        string itemName = ChooseBindingName(aggregation.ItemTarget, "item");
        string? indexName = aggregation.IndexTarget is null ? null : ChooseBindingName(aggregation.IndexTarget, "index");
        string aliases = EmitBindingAliasStatements(aggregation.IndexTarget, indexName, aggregation.ItemTarget, itemName);
        string prefix = string.IsNullOrWhiteSpace(aliases) ? string.Empty : aliases + " ";
        string wherePrefix = aggregation.WhereExpression is null ? string.Empty : $"if (!({EmitExpression(aggregation.WhereExpression)})) continue; ";

        switch (aggregation.AggregatorName)
        {
            case "sum":
                _writer.WriteLine($"{typeText} {name} = default;");
                _writer.WriteLine(EmitLoopOverSource(aggregation.Source, itemName, $"{prefix}{wherePrefix}{name} += {EmitExpression(aggregation.Body)};", indexName));
                return true;
            case "count":
                _writer.WriteLine($"{typeText} {name} = 0;");
                _writer.WriteLine(EmitLoopOverSource(aggregation.Source, itemName, $"{prefix}{wherePrefix}if ({EmitExpression(aggregation.Body)}) {{ {name}++; }}", indexName));
                return true;
            case "min":
            case "max":
            {
                string hasValueName = NextTemporary("hasValue");
                string valueName = NextTemporary("value");
                _writer.WriteLine($"bool {hasValueName} = false;");
                _writer.WriteLine($"{typeText} {name} = default!;");
                _writer.WriteLine(EmitLoopOverSource(aggregation.Source, itemName, $"{prefix}{wherePrefix}var {valueName} = {EmitExpression(aggregation.Body)}; if (!{hasValueName} || {EmitPreferredComparison(valueName, name, declaredType, preferLower: aggregation.AggregatorName == "min")}) {{ {name} = {valueName}; {hasValueName} = true; }}", indexName));
                return true;
            }
        }

        return false;
    }

    private bool TryEmitLoweredOutput(OutputStatement output)
    {
        TypeSyntax? outputType = _semantic?.GetExpressionType(output.Expression);
        if (outputType is null)
        {
            return false;
        }

        string tempName = NextTemporary("output");
        string typeText = EmitType(NormalizeSizedType(outputType));
        return output.Expression switch
        {
            CallExpression call => EmitLoweredOutputFromCall(output, tempName, typeText, call),
            AggregationExpression aggregation => EmitLoweredOutputFromAggregation(output, tempName, typeText, aggregation),
            _ => false,
        };
    }

    private bool EmitLoweredOutputFromCall(OutputStatement output, string tempName, string typeText, CallExpression call)
    {
        if (!TryEmitLoweredCallDeclaration(tempName, typeText, NormalizeSizedType(_semantic!.GetExpressionType(call)!), call))
        {
            return false;
        }

        _writer.WriteLine($"stdout.{(output.Kind == OutputKind.Write ? "write" : "writeln")}({tempName});");
        return true;
    }

    private bool EmitLoweredOutputFromAggregation(OutputStatement output, string tempName, string typeText, AggregationExpression aggregation)
    {
        TypeSyntax? aggregationType = _semantic?.GetExpressionType(aggregation);
        if (aggregationType is null
            || !TryEmitLoweredAggregationDeclaration(tempName, typeText, NormalizeSizedType(aggregationType), aggregation))
        {
            return false;
        }

        _writer.WriteLine($"stdout.{(output.Kind == OutputKind.Write ? "write" : "writeln")}({tempName});");
        return true;
    }

    private void EmitMultiDeclaration(DeclarationStatement declaration)
    {
        IReadOnlyList<BindingTarget> targets = declaration.Targets;
        string initializer = declaration.Initializer is null ? "default!" : EmitExpression(declaration.Initializer, declaration.ExplicitType);
        if (declaration.ExplicitType is TupleTypeSyntax tupleType && tupleType.Elements.Count == targets.Count)
        {
            string left = $"({string.Join(", ", targets.Select((target, index) => EmitTypedBindingPattern(target, tupleType.Elements[index])) )})";
            _writer.WriteLine($"{left} = {initializer};");
            return;
        }

        string targetText = declaration.ExplicitType is null
            ? $"var ({string.Join(", ", targets.Select(EmitBindingPattern))})"
            : $"({string.Join(", ", targets.Select(target => $"{EmitType(NormalizeSizedType(declaration.ExplicitType!))} {EmitBindingPattern(target)}"))})";
        _writer.WriteLine($"{targetText} = {initializer};");
    }

    private void EmitAssignment(AssignmentStatement assignment)
        => _writer.WriteLine($"{EmitStatementAssignmentExpression(new AssignmentExpression(assignment.Target, assignment.Operator, assignment.Value, false))};");

    private void EmitIfStatement(IfStatement ifStatement)
    {
        _writer.WriteLine($"if ({EmitExpression(ifStatement.Condition)})");
        EmitEmbeddedStatement(ifStatement.ThenBranch);
        if (ifStatement.ElseBranch is not null)
        {
            _writer.WriteLine("else");
            EmitEmbeddedStatement(ifStatement.ElseBranch);
        }
    }

    private void EmitForInStatement(ForInStatement forIn)
    {
        if (forIn.Source is RangeExpression range)
        {
            EmitRangeForStatement(forIn.Iterator, range, forIn.Body);
            return;
        }

        string iterator = EmitBindingPattern(forIn.Iterator);
        _writer.WriteLine($"foreach (var {iterator} in {EmitEnumerable(forIn.Source)})");
        EmitEmbeddedStatement(forIn.Body);
    }

    private void EmitRangeForStatement(BindingTarget iteratorTarget, RangeExpression range, Statement body)
    {
        string iterator = EmitBindingPattern(iteratorTarget);
        string iteratorType = IsLongRange(range, null) ? "long" : "int";
        string comparison = range.Kind == RangeKind.RightExclusive ? "<" : "<=";
        string decrementComparison = range.Kind == RangeKind.RightExclusive ? ">" : ">=";
        string startName = NextTemporary("start");
        string endName = NextTemporary("end");
        string stepName = NextTemporary("step");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine($"{iteratorType} {startName} = {EmitExpression(range.Start)};");
        _writer.WriteLine($"{iteratorType} {endName} = {EmitExpression(range.End)};");
        if (range.Step is null)
        {
            string op = iteratorType == "long" ? "1L" : "1";
            _writer.WriteLine($"{iteratorType} {stepName} = {startName} <= {endName} ? {op} : -{op};");
        }
        else
        {
            _writer.WriteLine($"{iteratorType} {stepName} = {EmitExpression(range.Step)};");
        }
        _writer.WriteLine($"if ({stepName} == 0) throw new InvalidOperationException(\"Range step cannot be zero.\");");
        _writer.WriteLine($"for ({iteratorType} {iterator} = {startName}; {stepName} > 0 ? {iterator} {comparison} {endName} : {iterator} {decrementComparison} {endName}; {iterator} += {stepName})");
        EmitEmbeddedStatement(body);
        _writer.Unindent();
        _writer.WriteLine("}");
    }

    private void EmitFastForStatement(FastForStatement fastFor)
    {
        if (fastFor.Source is RangeExpression range)
        {
            EmitRangeFastForStatement(range, fastFor);
            return;
        }

        string fallbackItemName = EmitBindingPattern(fastFor.ItemTarget);
        if (fastFor.IndexTarget is null)
        {
            _writer.WriteLine($"foreach (var {fallbackItemName} in {EmitEnumerable(fastFor.Source)})");
            EmitEmbeddedStatement(fastFor.Body);
            return;
        }

        string fallbackIndexName = EmitBindingPattern(fastFor.IndexTarget);
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine($"int {fallbackIndexName} = 0;");
        _writer.WriteLine($"foreach (var {fallbackItemName} in {EmitEnumerable(fastFor.Source)})");
        _writer.WriteLine("{");
        _writer.Indent();
        EmitStatement(fastFor.Body);
        _writer.WriteLine($"{fallbackIndexName}++;");
        _writer.Unindent();
        _writer.WriteLine("}");
        _writer.Unindent();
        _writer.WriteLine("}");
    }

    private void EmitRangeFastForStatement(RangeExpression range, FastForStatement fastFor)
    {
        string itemName = ChooseBindingName(fastFor.ItemTarget, "item");
        string? indexName = fastFor.IndexTarget is null ? null : ChooseBindingName(fastFor.IndexTarget, "index");
        string startName = NextTemporary("start");
        string endName = NextTemporary("end");
        string stepName = NextTemporary("step");
        bool useLong = IsLongRange(range, null);
        string iteratorType = useLong ? "long" : "int";
        string comparison = range.Kind == RangeKind.RightExclusive ? "<" : "<=";
        string decrementComparison = range.Kind == RangeKind.RightExclusive ? ">" : ">=";

        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine($"{iteratorType} {startName} = {EmitExpression(range.Start)};");
        _writer.WriteLine($"{iteratorType} {endName} = {EmitExpression(range.End)};");
        _writer.WriteLine($"{iteratorType} {stepName} = {EmitRangeStepExpression(range, startName, endName, useLong)};");
        _writer.WriteLine($"if ({stepName} == 0) throw new InvalidOperationException(\"Range step cannot be zero.\");");
        if (indexName is not null)
        {
            _writer.WriteLine($"int {indexName} = 0;");
        }

        _writer.WriteLine($"for ({iteratorType} {itemName} = {startName}; {stepName} > 0 ? {itemName} {comparison} {endName} : {itemName} {decrementComparison} {endName}; {itemName} += {stepName})");
        _writer.WriteLine("{");
        _writer.Indent();
        string aliases = EmitBindingAliasStatements(fastFor.IndexTarget, indexName, fastFor.ItemTarget, itemName);
        if (!string.IsNullOrWhiteSpace(aliases))
        {
            _writer.WriteLine(aliases);
        }

        EmitStatement(fastFor.Body);
        if (indexName is not null)
        {
            _writer.WriteLine($"{indexName}++;");
        }

        _writer.Unindent();
        _writer.WriteLine("}");
        _writer.Unindent();
        _writer.WriteLine("}");
    }

    private string EmitParameter(ParameterSyntax parameter)
    {
        string modifier = EmitArgumentModifier(parameter.Modifier);
        return string.IsNullOrEmpty(modifier)
            ? $"{EmitType(parameter.Type)} {EmitBindingPattern(parameter.Target)}"
            : $"{modifier} {EmitType(parameter.Type)} {EmitBindingPattern(parameter.Target)}";
    }

    private static string EmitArgumentModifier(ArgumentModifier modifier)
        => modifier switch
        {
            ArgumentModifier.Ref => "ref",
            ArgumentModifier.Out => "out",
            ArgumentModifier.In => "in",
            _ => string.Empty,
        };

    private static bool GetIsVoid(TypeSyntax type)
        => type is NamedTypeSyntax named && named.Name == "void";

    private static TypeSyntax NormalizeSizedType(TypeSyntax type)
        => type is SizedArrayTypeSyntax sized ? new ArrayTypeSyntax(sized.ElementType, sized.Dimensions.Count) : type;

    private static bool IsBareInvocation(Expression expression)
        => expression is CallExpression;

    private static Expression? GetImplicitReturnExpression(BlockStatement block)
    {
        if (block.Statements.Count == 0)
        {
            return null;
        }

        if (block.Statements[^1] is not ExpressionStatement { HasSemicolon: false } expressionStatement)
        {
            return null;
        }

        if (expressionStatement.Expression is AssignmentExpression { IsExplicitValueAssignment: false })
        {
            return null;
        }

        return IsBareInvocation(expressionStatement.Expression) ? null : expressionStatement.Expression;
    }

    private static bool ContainsExplicitReturn(BlockStatement block)
        => block.Statements.Any(statement => statement is ReturnStatement);

    private string NextTemporary(string prefix)
        => $"__{prefix}{_temporaryId++}";

    private static string GetEmittedTypeHeader(TypeDeclaration declaration)
    {
        if (!declaration.Members.OfType<OrderingShorthandMember>().Any()
            || declaration.HeaderText.Contains("IComparable<", StringComparison.Ordinal))
        {
            return declaration.HeaderText;
        }

        return declaration.HeaderText.Contains(':', StringComparison.Ordinal)
            ? declaration.HeaderText + $", System.IComparable<{declaration.Name}>"
            : declaration.HeaderText + $" : System.IComparable<{declaration.Name}>";
    }

    private string GetCurrentDeclaringTypeName()
        => _currentTypeStack.Count == 0 ? "object" : _currentTypeStack.Peek().Name;

    partial void EmitRuntimeHelpers();
}



