using System.Text;

namespace Pscp.Transpiler;

internal sealed partial class CSharpEmitter
{
    private readonly TranspilationOptions _options;
    private readonly SemanticAnalysisResult? _semantic;
    private readonly CodeWriter _writer = new();
    private readonly Stack<TypeDeclaration> _currentTypeStack = new();
    private readonly HashSet<DeclarationStatement> _hoistedGlobalDeclarations = [];
    private readonly HashSet<string> _declaredTypeNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _declaredValueNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Stack<string>> _identifierAliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeclaredTypeShape> _declaredTypeShapes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _stdoutDirectScalarWriteKinds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _stdoutDirectScalarWritelnKinds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _stdoutDirectNullableScalarWriteKinds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _stdoutDirectNullableScalarWritelnKinds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _stdoutDirectArrayWriteKinds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _stdoutDirectArrayWritelnKinds = new(StringComparer.Ordinal);
    private bool _emitStdin;
    private bool _emitStdout;
    private bool _stdoutNeedsBlankLine;
    private bool _stdoutNeedsFallbackHelpers;
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
        (_emitStdin, _emitStdout) = AnalyzeRuntimeUsage(program);
        CollectDeclaredTypeNames(program.Types);
        CollectDeclaredValueNames(program);
        CollectDeclaredTypeShapes(program.Types, null);
        CollectHoistedGlobalDeclarations(program);

        foreach (string usingLine in CollectUsings(program))
        {
            _writer.WriteLine(usingLine);
        }

        _writer.WriteLine();
        _writer.WriteLine("#nullable enable");
        _writer.WriteLine();
        if (_options.Explain && !string.IsNullOrWhiteSpace(_options.ExplainSource))
        {
            EmitExplainSourceHeader(_options.ExplainSource!);
            _writer.WriteLine();
        }

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
        if (_emitStdin)
        {
            _writer.WriteLine("private static readonly __PscpStdin stdin = new();");
        }

        if (_emitStdout)
        {
            _writer.WriteLine("private static readonly __PscpStdout stdout = new();");
        }

        if (_emitStdin || _emitStdout)
        {
            _writer.WriteLine();
        }

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
        return PruneUsingDirectives(_writer.ToString());
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

    private static string PruneUsingDirectives(string code)
    {
        string normalized = code.Replace("\r", string.Empty);
        string[] lines = normalized.Split('\n');
        int bodyStart = 0;
        while (bodyStart < lines.Length && (lines[bodyStart].StartsWith("using ", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(lines[bodyStart])))
        {
            bodyStart++;
        }

        string body = string.Join("\n", lines.Skip(bodyStart));
        bool needsCollectionsGeneric =
            body.Contains("List<", StringComparison.Ordinal)
            || body.Contains("Dictionary<", StringComparison.Ordinal)
            || body.Contains("HashSet<", StringComparison.Ordinal)
            || body.Contains("Queue<", StringComparison.Ordinal)
            || body.Contains("Stack<", StringComparison.Ordinal)
            || body.Contains("LinkedList<", StringComparison.Ordinal)
            || body.Contains("PriorityQueue<", StringComparison.Ordinal)
            || body.Contains("SortedSet<", StringComparison.Ordinal)
            || body.Contains("Comparer<", StringComparison.Ordinal)
            || body.Contains("IEnumerable<", StringComparison.Ordinal)
            || body.Contains("IEnumerator<", StringComparison.Ordinal)
            || body.Contains("IComparer<", StringComparison.Ordinal)
            || body.Contains("Comparison<", StringComparison.Ordinal);
        bool needsGlobalization = body.Contains("CultureInfo", StringComparison.Ordinal);
        bool needsIO =
            body.Contains("StreamReader", StringComparison.Ordinal)
            || body.Contains("StreamWriter", StringComparison.Ordinal)
            || body.Contains("EndOfStreamException", StringComparison.Ordinal);
        bool needsLinq =
            body.Contains("SelectMany", StringComparison.Ordinal)
            || body.Contains(".ToArray()", StringComparison.Ordinal)
            || body.Contains(".ToList()", StringComparison.Ordinal)
            || body.Contains(".Distinct()", StringComparison.Ordinal)
            || body.Contains(".Reverse()", StringComparison.Ordinal);
        bool needsText =
            body.Contains("StringBuilder", StringComparison.Ordinal)
            || body.Contains("Encoding", StringComparison.Ordinal)
            || body.Contains("UTF8Encoding", StringComparison.Ordinal);

        List<string> result = [];
        foreach (string line in lines)
        {
            if (!line.StartsWith("using ", StringComparison.Ordinal))
            {
                result.Add(line);
                continue;
            }

            bool keep = line switch
            {
                "using System;" => true,
                "using System.Collections.Generic;" => needsCollectionsGeneric,
                "using System.Globalization;" => needsGlobalization,
                "using System.IO;" => needsIO,
                "using System.Linq;" => needsLinq,
                "using System.Text;" => needsText,
                _ => true,
            };

            if (keep)
            {
                result.Add(line);
            }
        }

        return string.Join("\n", result);
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
        => TryGetFieldEntries(declaration, out _);

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

        TypeSyntax? declaredType = GetDeclarationEmissionType(declaration);
        string initializerText = declaration.Initializer is null
            ? EmitImplicitInitializer(declaration.ExplicitType)
            : EmitExpression(declaration.Initializer, declaredType);
        _writer.WriteLine($"{name} = {initializerText};");
    }

    private void EmitHoistedInputDeclaration(DeclarationStatement declaration)
    {
        if (declaration.ExplicitType is null)
        {
            return;
        }

        if (TryEmitDirectInputDeclaration(declaration, hoisted: true))
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
            case SwitchExpression @switch:
                CollectReferencedNames(@switch.Receiver, names);
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
        if (GetIsVoid(function.ReturnType))
        {
            EmitBlockContents(function.Body, isVoidLike: true);
        }
        else
        {
            EmitValueAwareBlockContents(function.Body);
        }
        _writer.Unindent();
        _writer.WriteLine("}");
    }

    private void EmitMain(IReadOnlyList<Statement> statements)
    {
        _writer.WriteLine("public static void Main()");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine("Run();");
        if (_emitStdout)
        {
            _writer.WriteLine("stdout.flush();");
        }
        _writer.Unindent();
        _writer.WriteLine("}");
        _writer.WriteLine();
        _writer.WriteLine("private static void Run()");
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
            _writer.WriteLine($"{EmitStatementExpression(implicitReturn)};");
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
                _writer.WriteLine($"{EmitStatementExpression(implicitReturn)};");
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
                    _writer.WriteLine(EmitOutputInvocation(output.Kind, output.Expression));
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

        if (TryEmitConstDeclaration(declaration, name))
        {
            return;
        }

        string typeText = declaration.ExplicitType is null ? "var" : EmitType(NormalizeSizedType(declaration.ExplicitType));
        string initializerText = declaration.Initializer is null
            ? EmitImplicitInitializer(declaration.ExplicitType)
            : EmitExpression(declaration.Initializer, declaration.ExplicitType);
        _writer.WriteLine($"{typeText} {name} = {initializerText};");
    }

    private bool TryEmitConstDeclaration(DeclarationStatement declaration, string name)
    {
        if (declaration.Mutability != MutabilityKind.Immutable
            || declaration.Initializer is null
            || declaration.Targets.Count != 1
            || declaration.Targets[0] is not NameTarget)
        {
            return false;
        }

        if (!TryGetConstInitializer(declaration.Initializer, out string? initializerText, out string? literalType)
            || literalType is null)
        {
            return false;
        }

        TypeSyntax? effectiveType = NormalizeSizedTypeOrNull(declaration.ExplicitType)
            ?? NormalizeSizedTypeOrNull(_semantic?.GetExpressionType(declaration.Initializer));
        string? typeText = effectiveType is null ? literalType : TryGetConstTypeText(effectiveType);
        if (typeText is null || !IsConstInitializerCompatible(typeText, literalType))
        {
            return false;
        }

        _writer.WriteLine($"const {typeText} {name} = {initializerText};");
        return true;
    }

    private static bool TryGetConstInitializer(Expression expression, out string? initializerText, out string? literalType)
    {
        if (expression is LiteralExpression literal)
        {
            return TryGetLiteralConstInitializer(literal, prefix: null, out initializerText, out literalType);
        }

        if (expression is UnaryExpression { Operator: UnaryOperator.Plus or UnaryOperator.Negate, Operand: LiteralExpression numericLiteral }
            && numericLiteral.Kind is LiteralKind.Integer or LiteralKind.Float)
        {
            string prefix = expression is UnaryExpression { Operator: UnaryOperator.Negate } ? "-" : "+";
            return TryGetLiteralConstInitializer(numericLiteral, prefix, out initializerText, out literalType);
        }

        initializerText = null;
        literalType = null;
        return false;
    }

    private static bool TryGetLiteralConstInitializer(
        LiteralExpression literal,
        string? prefix,
        out string? initializerText,
        out string? literalType)
    {
        literalType = literal.Kind switch
        {
            LiteralKind.Integer => literal.RawText.EndsWith("L", StringComparison.OrdinalIgnoreCase) ? "long" : "int",
            LiteralKind.Float => literal.RawText.EndsWith("m", StringComparison.OrdinalIgnoreCase) ? "decimal" : "double",
            LiteralKind.String => "string",
            LiteralKind.Char => "char",
            LiteralKind.True or LiteralKind.False => "bool",
            _ => null,
        };

        if (literalType is null)
        {
            initializerText = null;
            return false;
        }

        initializerText = prefix is null ? literal.RawText : prefix + literal.RawText;
        return true;
    }

    private static string? TryGetConstTypeText(TypeSyntax type)
        => type is NamedTypeSyntax { TypeArguments.Count: 0 } named
            && named.Name is "int" or "long" or "double" or "decimal" or "bool" or "char" or "string"
                ? named.Name
                : null;

    private static bool IsConstInitializerCompatible(string typeText, string literalType)
        => typeText == literalType
            || literalType == "int" && typeText is "long" or "double" or "decimal";

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

    private void CollectDeclaredValueNames(PscpProgram program)
    {
        foreach (FunctionDeclaration function in program.Functions)
        {
            _declaredValueNames.Add(function.Name);
            CollectDeclaredValueNames(function.Parameters);
            CollectDeclaredValueNames(function.Body);
        }

        foreach (Statement statement in program.GlobalStatements)
        {
            CollectDeclaredValueNames(statement);
        }

        foreach (TypeDeclaration type in program.Types)
        {
            CollectDeclaredValueNames(type);
        }
    }

    private void CollectDeclaredValueNames(TypeDeclaration type)
    {
        foreach (TypeMember member in type.Members)
        {
            switch (member)
            {
                case FieldMember field:
                    CollectDeclaredValueNames(field.Declaration.Targets);
                    break;
                case PropertyMember property:
                    _declaredValueNames.Add(property.Name);
                    CollectDeclaredValueNames(property.Body);
                    break;
                case MethodMember method:
                    _declaredValueNames.Add(method.Name);
                    CollectDeclaredValueNames(method.Parameters);
                    CollectDeclaredValueNames(method.Body);
                    break;
                case OperatorMember @operator:
                    CollectDeclaredValueNames(@operator.Parameters);
                    CollectDeclaredValueNames(@operator.Body);
                    break;
                case NestedTypeMember nested:
                    CollectDeclaredValueNames(nested.Declaration);
                    break;
            }
        }
    }

    private void CollectDeclaredValueNames(MethodBody body)
    {
        if (body is BlockMethodBody block)
        {
            CollectDeclaredValueNames(block.Block);
        }
    }

    private void CollectDeclaredValueNames(BlockStatement block)
    {
        foreach (Statement statement in block.Statements)
        {
            CollectDeclaredValueNames(statement);
        }
    }

    private void CollectDeclaredValueNames(Statement statement)
    {
        switch (statement)
        {
            case BlockStatement block:
                CollectDeclaredValueNames(block);
                break;
            case DeclarationStatement declaration:
                CollectDeclaredValueNames(declaration.Targets);
                break;
            case IfStatement ifStatement:
                CollectDeclaredValueNames(ifStatement.ThenBranch);
                if (ifStatement.ElseBranch is not null)
                {
                    CollectDeclaredValueNames(ifStatement.ElseBranch);
                }
                break;
            case WhileStatement whileStatement:
                CollectDeclaredValueNames(whileStatement.Body);
                break;
            case ForInStatement forIn:
                CollectDeclaredValueNames(forIn.Body);
                break;
            case FastForStatement fastFor:
                CollectDeclaredValueNames(fastFor.Body);
                break;
            case CStyleForStatement cStyleFor:
                CollectDeclaredValueNames(cStyleFor.Body);
                break;
            case LocalFunctionStatement localFunction:
                _declaredValueNames.Add(localFunction.Function.Name);
                CollectDeclaredValueNames(localFunction.Function.Parameters);
                CollectDeclaredValueNames(localFunction.Function.Body);
                break;
        }
    }

    private void CollectDeclaredValueNames(IEnumerable<ParameterSyntax> parameters)
    {
        foreach (ParameterSyntax parameter in parameters)
        {
            CollectDeclaredValueNames(parameter.Target);
        }
    }

    private void CollectDeclaredValueNames(IEnumerable<BindingTarget> targets)
    {
        foreach (BindingTarget target in targets)
        {
            CollectDeclaredValueNames(target);
        }
    }

    private void CollectDeclaredValueNames(BindingTarget target)
    {
        switch (target)
        {
            case NameTarget nameTarget:
                _declaredValueNames.Add(nameTarget.Name);
                break;
            case TupleTarget tupleTarget:
                foreach (BindingTarget element in tupleTarget.Elements)
                {
                    CollectDeclaredValueNames(element);
                }
                break;
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
        => TryGetStatementExpression(expression, out string? emitted)
            ? emitted!
            : $"_ = {EmitExpression(expression)}";

    private bool TryGetStatementExpression(Expression expression, out string? emitted)
    {
        switch (expression)
        {
            case CallExpression:
            case NewExpression:
            case NewArrayExpression:
                emitted = EmitExpression(expression);
                return true;
            case AssignmentExpression assignmentExpression:
                emitted = EmitStatementAssignmentExpression(assignmentExpression);
                return true;
            case PrefixExpression prefix when !TryEmitKnownDataStructurePop(prefix.Operand, out _):
                emitted = EmitScalarPrefixStatementExpression(prefix);
                return true;
            case PrefixExpression prefix:
                emitted = EmitPrefixExpression(prefix);
                return true;
            case PostfixExpression postfix:
                emitted = EmitPostfixStatementExpression(postfix);
                return true;
            default:
                emitted = null;
                return false;
        }
    }

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
        TypeSyntax? normalizedType = GetDeclarationEmissionType(declaration);
        if (normalizedType is null)
        {
            return false;
        }
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

    private TypeSyntax? GetDeclarationEmissionType(DeclarationStatement declaration)
        => NormalizeSizedTypeOrNull(
            declaration.ExplicitType
            ?? (declaration.Initializer is null ? null : _semantic?.GetExpressionType(declaration.Initializer)));

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
        string countName = NextTemporary("count");
        string slotName = NextTemporary("slot");
        string comparison = range.Kind == RangeKind.RightExclusive ? "<" : "<=";

        if (range.Step is null)
        {
            if (CanInlineDirectRangeExpression(range.Start) && CanInlineDirectRangeExpression(range.End))
            {
                string directStart = EmitExpression(range.Start);
                string directEnd = EmitExpression(range.End);
                string directCount = EmitDefaultRangeCountExpression(directStart, directEnd, range.Kind);
                _writer.WriteLine($"{typeText} {name} = new {EmitType(elementType)}[{directCount}];");
                string directLoopHeader = indexName is null
                    ? $"for (int {itemName} = {directStart}, {slotName} = 0; {itemName} {comparison} {directEnd}; {itemName}++, {slotName}++)"
                    : $"for (int {itemName} = {directStart}, {indexName} = 0; {itemName} {comparison} {directEnd}; {itemName}++, {indexName}++)";
                string slotExpression = indexName ?? slotName;
                _writer.WriteLine(directLoopHeader);
                _writer.WriteLine("{");
                _writer.Indent();
                _writer.WriteLine($"{name}[{slotExpression}] = {valueExpression};");
                _writer.Unindent();
                _writer.WriteLine("}");
                return;
            }

            _writer.WriteLine($"int {startName} = {EmitExpression(range.Start)};");
            _writer.WriteLine($"int {endName} = {EmitExpression(range.End)};");
            string countExpression = EmitDefaultRangeCountExpression(startName, endName, range.Kind);
            _writer.WriteLine($"int {countName} = {countExpression};");
            _writer.WriteLine($"{typeText} {name} = new {EmitType(elementType)}[{countName}];");
            _writer.WriteLine($"int {slotName} = 0;");
            string defaultLoopHeader = indexName is null
                ? $"for (int {itemName} = {startName}; {itemName} {comparison} {endName}; {itemName}++)"
                : $"for (int {itemName} = {startName}, {indexName} = 0; {itemName} {comparison} {endName}; {itemName}++, {indexName}++)";
            _writer.WriteLine(defaultLoopHeader);
            _writer.WriteLine("{");
            _writer.Indent();
            _writer.WriteLine($"{name}[{slotName}++] = {valueExpression};");
            _writer.Unindent();
            _writer.WriteLine("}");
            return;
        }

        _writer.WriteLine($"int {startName} = {EmitExpression(range.Start)};");
        _writer.WriteLine($"int {endName} = {EmitExpression(range.End)};");
        string stepName = NextTemporary("step");
        string absStepName = NextTemporary("absStep");
        string forwardCount = range.Kind == RangeKind.RightExclusive
            ? $"{startName} < {endName} ? (({endName} - {startName} - 1) / {stepName}) + 1 : 0"
            : $"{startName} <= {endName} ? (({endName} - {startName}) / {stepName}) + 1 : 0";
        string backwardCount = range.Kind == RangeKind.RightExclusive
            ? $"{startName} > {endName} ? (({startName} - {endName} - 1) / {absStepName}) + 1 : 0"
            : $"{startName} >= {endName} ? (({startName} - {endName}) / {absStepName}) + 1 : 0";
        string backwardOp = range.Kind == RangeKind.RightExclusive ? ">" : ">=";
        _writer.WriteLine($"int {stepName} = {EmitExpression(range.Step)};");
        _writer.WriteLine("#if DEBUG");
        _writer.WriteLine($"if ({stepName} == 0) throw new InvalidOperationException(\"Range step cannot be zero.\");");
        _writer.WriteLine("#endif");
        _writer.WriteLine($"int {absStepName} = {stepName} > 0 ? {stepName} : -{stepName};");
        _writer.WriteLine($"int {countName} = {stepName} > 0 ? {forwardCount} : {backwardCount};");
        _writer.WriteLine($"{typeText} {name} = new {EmitType(elementType)}[{countName}];");
        _writer.WriteLine($"int {slotName} = 0;");
        string loopHeader = indexName is null
            ? $"for (int {itemName} = {startName}; {stepName} > 0 ? {itemName} {comparison} {endName} : {itemName} {backwardOp} {endName}; {itemName} += {stepName})"
            : $"for (int {itemName} = {startName}, {indexName} = 0; {stepName} > 0 ? {itemName} {comparison} {endName} : {itemName} {backwardOp} {endName}; {itemName} += {stepName}, {indexName}++)";
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
            MemberAccessExpression memberAccess when PscpIntrinsicCatalog.IntrinsicCallNames.Contains(PscpIntrinsicCatalog.StripGenericSuffix(memberAccess.MemberName)) => PscpIntrinsicCatalog.StripGenericSuffix(memberAccess.MemberName),
            _ => null,
        };
        Expression? receiver = call.Callee is MemberAccessExpression member ? member.Receiver : null;
        if (intrinsicName is null)
        {
            return false;
        }

        if (_semantic is not null && !_semantic.IsIntrinsicCall(call))
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
                string loopBody = TryEmitLambdaBodyValueStatement(
                    sumGenerator.Body,
                    sumGenerator.IndexTarget,
                    indexName,
                    sumGenerator.ItemTarget,
                    itemName,
                    valueExpression => $"{name} += {valueExpression};",
                    out string loweredLoopBody)
                    ? loweredLoopBody
                    : $"{name} += {EmitLambdaBodyExpression(sumGenerator.Body, sumGenerator.IndexTarget, indexName, sumGenerator.ItemTarget, itemName)};";
                _writer.WriteLine(EmitLoopOverSource(sumGenerator.Source, itemName, loopBody, indexName));
            }
            else
            {
                _writer.WriteLine(EmitLoopOverSource(sumSource!, itemName, $"{name} += {itemName};"));
            }
            return true;
        }

        if ((intrinsicName == "min" || intrinsicName == "max") && TryGetSourceOnlyCall(receiver, call.Arguments, out Expression? minMaxSource))
        {
            string itemName = minMaxSource is GeneratorExpression generator ? ChooseBindingName(generator.ItemTarget, "item") : NextTemporary("item");
            string hasValueName = NextTemporary("hasValue");
            string valueName = NextTemporary("value");
            _writer.WriteLine($"bool {hasValueName} = false;");
            _writer.WriteLine($"{typeText} {name} = default!;");
            if (minMaxSource is GeneratorExpression minMaxGenerator)
            {
                string? indexName = minMaxGenerator.IndexTarget is null ? null : ChooseBindingName(minMaxGenerator.IndexTarget, "index");
                string loopBody = TryEmitLambdaBodyValueStatement(
                    minMaxGenerator.Body,
                    minMaxGenerator.IndexTarget,
                    indexName,
                    minMaxGenerator.ItemTarget,
                    itemName,
                    valueExpression => $"var {valueName} = {valueExpression}; if (!{hasValueName} || {EmitPreferredComparison(valueName, name, declaredType, preferLower: intrinsicName == "min")}) {{ {name} = {valueName}; {hasValueName} = true; }}",
                    out string loweredLoopBody)
                    ? loweredLoopBody
                    : $"var {valueName} = {EmitLambdaBodyExpression(minMaxGenerator.Body, minMaxGenerator.IndexTarget, indexName, minMaxGenerator.ItemTarget, itemName)}; if (!{hasValueName} || {EmitPreferredComparison(valueName, name, declaredType, preferLower: intrinsicName == "min")}) {{ {name} = {valueName}; {hasValueName} = true; }}";
                _writer.WriteLine(EmitLoopOverSource(minMaxGenerator.Source, itemName, loopBody, indexName));
            }
            else
            {
                _writer.WriteLine(EmitLoopOverSource(minMaxSource!, itemName, $"if (!{hasValueName} || {EmitPreferredComparison(itemName, name, declaredType, preferLower: intrinsicName == "min")}) {{ {name} = {itemName}; {hasValueName} = true; }}"));
            }

            EmitDebugEmptySequenceCheckStatement(hasValueName);
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
                LambdaBlockBody blockBody => blockBody.Block.Statements.LastOrDefault() switch
                {
                    ExpressionStatement { HasSemicolon: false } tail => _semantic?.GetExpressionType(tail.Expression),
                    ReturnStatement { Expression: not null } tail => _semantic?.GetExpressionType(tail.Expression),
                    _ => null,
                },
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
            EmitDebugEmptySequenceCheckStatement(hasValueName);
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

        if (intrinsicName == "sort" && TryGetSourceOnlyCall(receiver, call.Arguments, out Expression? sortSource))
        {
            EmitSortedArrayDeclaration(name, typeText, sortSource!);
            _writer.WriteLine($"Array.Sort({name});");
            return true;
        }

        if (intrinsicName == "sortBy" && TryGetSourceAndUnaryLambdaCall(receiver, call.Arguments, out Expression? sortBySource, out LambdaExpression? sortBySelector))
        {
            TypeSyntax? keyType = sortBySelector!.Body switch
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

            EmitSortedArrayDeclaration(name, typeText, sortBySource!);
            string leftName = NextTemporary("left");
            string rightName = NextTemporary("right");
            if (sortBySelector.Body is LambdaExpressionBody selectorBody)
            {
                string leftKeyName = NextTemporary("leftKey");
                string rightKeyName = NextTemporary("rightKey");
                string leftAliases = EmitBindingAliasStatements(null, null, sortBySelector.Parameters[0].Target, leftName);
                string rightAliases = EmitBindingAliasStatements(null, null, sortBySelector.Parameters[0].Target, rightName);
                string keyExpression = EmitExpression(selectorBody.Expression);

                _writer.WriteLine($"Array.Sort({name}, ({leftName}, {rightName}) =>");
                _writer.WriteLine("{");
                _writer.Indent();
                _writer.WriteLine($"{EmitType(keyType)} {leftKeyName};");
                _writer.WriteLine("{");
                _writer.Indent();
                if (!string.IsNullOrWhiteSpace(leftAliases))
                {
                    _writer.WriteLine(leftAliases);
                }

                _writer.WriteLine($"{leftKeyName} = {keyExpression};");
                _writer.Unindent();
                _writer.WriteLine("}");
                _writer.WriteLine($"{EmitType(keyType)} {rightKeyName};");
                _writer.WriteLine("{");
                _writer.Indent();
                if (!string.IsNullOrWhiteSpace(rightAliases))
                {
                    _writer.WriteLine(rightAliases);
                }

                _writer.WriteLine($"{rightKeyName} = {keyExpression};");
                _writer.Unindent();
                _writer.WriteLine("}");
                _writer.WriteLine($"return System.Collections.Generic.Comparer<{EmitType(keyType)}>.Default.Compare({leftKeyName}, {rightKeyName});");
                _writer.Unindent();
                _writer.WriteLine("});");
            }
            else
            {
                string leftKey = EmitLambdaBodyExpression(sortBySelector.Body, null, null, sortBySelector.Parameters[0].Target, leftName);
                string rightKey = EmitLambdaBodyExpression(sortBySelector.Body, null, null, sortBySelector.Parameters[0].Target, rightName);
                _writer.WriteLine($"Array.Sort({name}, ({leftName}, {rightName}) => System.Collections.Generic.Comparer<{EmitType(keyType)}>.Default.Compare({leftKey}, {rightKey}));");
            }

            return true;
        }

        return false;
    }

    private void EmitSortedArrayDeclaration(string name, string typeText, Expression source)
    {
        TypeSyntax? sourceType = _semantic?.GetExpressionType(source);
        string sourceText = EmitExpression(source);
        if (sourceType is ArrayTypeSyntax)
        {
            _writer.WriteLine($"{typeText} {name} = ({typeText}){sourceText}.Clone();");
            return;
        }

        _writer.WriteLine($"{typeText} {name} = System.Linq.Enumerable.ToArray({sourceText});");
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
                _writer.WriteLine(EmitLoopOverSource(
                    aggregation.Source,
                    itemName,
                    EmitAggregationBodyValueStatement(aggregation.Body, prefix, wherePrefix, valueExpression => $"{name} += {valueExpression};"),
                    indexName));
                return true;
            case "count":
                _writer.WriteLine($"{typeText} {name} = 0;");
                _writer.WriteLine(EmitLoopOverSource(
                    aggregation.Source,
                    itemName,
                    EmitAggregationBodyValueStatement(aggregation.Body, prefix, wherePrefix, valueExpression => $"if ({valueExpression}) {{ {name}++; }}"),
                    indexName));
                return true;
            case "min":
            case "max":
            {
                string hasValueName = NextTemporary("hasValue");
                string valueName = NextTemporary("value");
                _writer.WriteLine($"bool {hasValueName} = false;");
                _writer.WriteLine($"{typeText} {name} = default!;");
                _writer.WriteLine(EmitLoopOverSource(
                    aggregation.Source,
                    itemName,
                    EmitAggregationBodyValueStatement(
                        aggregation.Body,
                        prefix,
                        wherePrefix,
                        valueExpression => $"var {valueName} = {valueExpression}; if (!{hasValueName} || {EmitPreferredComparison(valueName, name, declaredType, preferLower: aggregation.AggregatorName == "min")}) {{ {name} = {valueName}; {hasValueName} = true; }}"),
                    indexName));
                EmitDebugEmptySequenceCheckStatement(hasValueName);
                return true;
            }
        }

        return false;
    }

    private void EmitDebugEmptySequenceCheckStatement(string hasValueName)
    {
        _writer.WriteLine("#if DEBUG");
        _writer.WriteLine($"if (!{hasValueName}) throw new InvalidOperationException(\"Sequence contains no elements.\");");
        _writer.WriteLine("#endif");
    }

    private string EmitAggregationBodyValueStatement(
        Expression body,
        string prefix,
        string wherePrefix,
        Func<string, string> emitValueStatement)
    {
        string bodyStatement = body is BlockExpression block && TryEmitBlockValueStatement(block.Block, emitValueStatement, out string loweredBlock)
            ? loweredBlock
            : emitValueStatement(EmitExpression(body));
        return string.Concat(prefix, wherePrefix, bodyStatement);
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

        RegisterStdoutUsage(output.Kind, _semantic.GetExpressionType(call));
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

        RegisterStdoutUsage(output.Kind, aggregationType);
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

    private string EmitOutputInvocation(OutputKind kind, Expression expression)
    {
        RegisterStdoutUsage(kind, _semantic?.GetExpressionType(expression));
        return $"stdout.{(kind == OutputKind.Write ? "write" : "writeln")}({EmitExpression(expression)});";
    }

    private void RegisterExplicitStdoutCall(string memberName, IReadOnlyList<ArgumentSyntax> arguments)
    {
        switch (memberName)
        {
            case "write":
            case "writeln":
                if (arguments.Count == 1 && arguments[0] is ExpressionArgumentSyntax expressionArgument)
                {
                    RegisterStdoutUsage(memberName == "write" ? OutputKind.Write : OutputKind.WriteLine, _semantic?.GetExpressionType(expressionArgument.Expression));
                }
                else if (memberName == "writeln" && arguments.Count == 0)
                {
                    _stdoutNeedsBlankLine = true;
                }
                else
                {
                    _stdoutNeedsFallbackHelpers = true;
                }
                break;
            case "flush":
                break;
            default:
                _stdoutNeedsFallbackHelpers = true;
                break;
        }
    }

    private void RegisterStdoutUsage(OutputKind kind, TypeSyntax? type)
    {
        if (type is NullableTypeSyntax nullable
            && UnwrapNullableType(nullable.InnerType) is NamedTypeSyntax { TypeArguments.Count: 0 } nullableNamed
            && IsDirectStdoutScalar(nullableNamed.Name))
        {
            RegisterStdoutScalarUsage(kind, nullableNamed.Name);
            if (nullableNamed.Name != "string")
            {
                RegisterStdoutNullableScalarUsage(kind, nullableNamed.Name);
            }
            return;
        }

        TypeSyntax? normalized = UnwrapNullableType(type);
        if (normalized is NamedTypeSyntax { TypeArguments.Count: 0 } named && IsDirectStdoutScalar(named.Name))
        {
            RegisterStdoutScalarUsage(kind, named.Name);
            return;
        }

        if (normalized is ArrayTypeSyntax { Depth: 1, ElementType: NamedTypeSyntax { TypeArguments.Count: 0 } elementNamed }
            && IsDirectStdoutScalar(elementNamed.Name))
        {
            _stdoutDirectScalarWriteKinds.Add(elementNamed.Name);
            _stdoutDirectArrayWriteKinds.Add(elementNamed.Name);
            if (kind == OutputKind.WriteLine)
            {
                _stdoutDirectArrayWritelnKinds.Add(elementNamed.Name);
            }
            return;
        }

        _stdoutNeedsFallbackHelpers = true;
    }

    private void RegisterStdoutScalarUsage(OutputKind kind, string scalarKind)
    {
        _stdoutDirectScalarWriteKinds.Add(scalarKind);
        if (kind == OutputKind.WriteLine)
        {
            _stdoutDirectScalarWritelnKinds.Add(scalarKind);
        }
    }

    private void RegisterStdoutNullableScalarUsage(OutputKind kind, string scalarKind)
    {
        _stdoutDirectNullableScalarWriteKinds.Add(scalarKind);
        if (kind == OutputKind.WriteLine)
        {
            _stdoutDirectNullableScalarWritelnKinds.Add(scalarKind);
        }
    }

    private static bool IsDirectStdoutScalar(string name)
        => name is "int" or "long" or "double" or "decimal" or "bool" or "char" or "string";

    private void EmitExplainSourceHeader(string source)
    {
        _writer.WriteLine(
@"/*
PSCP introduce: https://github.com/614project/Pscp
PSCP source:"
        );
        foreach (string line in StripPscpComments(source).Split('\n'))
        {
            string normalized = line.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(normalized)) continue;
            _writer.WriteLine(normalized.Length == 0 ? string.Empty : normalized);
        }
        _writer.WriteLine(
@"*/
// (This file was generated by the PSCP transpiler.)"
        );
    }

    private static string StripPscpComments(string source)
    {
        StringBuilder builder = new(source.Length);
        bool inBlockComment = false;
        bool inString = false;
        bool inInterpolatedString = false;
        bool inChar = false;

        for (int i = 0; i < source.Length; i++)
        {
            char current = source[i];
            char next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                else if (current == '\r' || current == '\n')
                {
                    builder.Append(current);
                }

                continue;
            }

            if (!inString && !inInterpolatedString && !inChar && current == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (!inString && !inInterpolatedString && !inChar && current == '/' && next == '/')
            {
                while (i < source.Length && source[i] is not '\r' and not '\n')
                {
                    i++;
                }

                if (i < source.Length)
                {
                    builder.Append(source[i]);
                }

                continue;
            }

            if (current == '"' && !inChar)
            {
                bool escaped = i > 0 && source[i - 1] == '\\';
                if (!escaped)
                {
                    if (i > 0 && source[i - 1] == '$')
                    {
                        inInterpolatedString = !inInterpolatedString;
                    }
                    else
                    {
                        inString = !inString;
                    }
                }
            }
            else if (current == '\'' && !inString && !inInterpolatedString)
            {
                bool escaped = i > 0 && source[i - 1] == '\\';
                if (!escaped)
                {
                    inChar = !inChar;
                }
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static (bool NeedsStdin, bool NeedsStdout) AnalyzeRuntimeUsage(PscpProgram program)
    {
        bool needsStdin = false;
        bool needsStdout = false;

        foreach (TypeDeclaration type in program.Types)
        {
            AnalyzeRuntimeUsage(type, ref needsStdin, ref needsStdout);
        }

        foreach (FunctionDeclaration function in program.Functions)
        {
            AnalyzeRuntimeUsage(function.Body, ref needsStdin, ref needsStdout);
        }

        foreach (Statement statement in program.GlobalStatements)
        {
            AnalyzeRuntimeUsage(statement, ref needsStdin, ref needsStdout);
        }

        return (needsStdin, needsStdout);
    }

    private static void AnalyzeRuntimeUsage(TypeDeclaration declaration, ref bool needsStdin, ref bool needsStdout)
    {
        foreach (TypeMember member in declaration.Members)
        {
            switch (member)
            {
                case OrderingShorthandMember ordering:
                    AnalyzeRuntimeUsage(ordering.Body, ref needsStdin, ref needsStdout);
                    break;
                case FieldMember field:
                    AnalyzeRuntimeUsage(field.Declaration, ref needsStdin, ref needsStdout);
                    break;
                case PropertyMember property:
                    AnalyzeRuntimeUsage(property.Body, ref needsStdin, ref needsStdout);
                    break;
                case MethodMember method:
                    AnalyzeRuntimeUsage(method.Body, ref needsStdin, ref needsStdout);
                    break;
                case OperatorMember @operator:
                    AnalyzeRuntimeUsage(@operator.Body, ref needsStdin, ref needsStdout);
                    break;
                case NestedTypeMember nested:
                    AnalyzeRuntimeUsage(nested.Declaration, ref needsStdin, ref needsStdout);
                    break;
            }
        }
    }

    private static void AnalyzeRuntimeUsage(MethodBody body, ref bool needsStdin, ref bool needsStdout)
    {
        switch (body)
        {
            case BlockMethodBody blockBody:
                AnalyzeRuntimeUsage(blockBody.Block, ref needsStdin, ref needsStdout);
                break;
            case ExpressionMethodBody expressionBody:
                AnalyzeRuntimeUsage(expressionBody.Expression, ref needsStdin, ref needsStdout);
                break;
        }
    }

    private static void AnalyzeRuntimeUsage(BlockStatement block, ref bool needsStdin, ref bool needsStdout)
    {
        foreach (Statement statement in block.Statements)
        {
            AnalyzeRuntimeUsage(statement, ref needsStdin, ref needsStdout);
        }
    }

    private static void AnalyzeRuntimeUsage(Statement statement, ref bool needsStdin, ref bool needsStdout)
    {
        switch (statement)
        {
            case BlockStatement block:
                AnalyzeRuntimeUsage(block, ref needsStdin, ref needsStdout);
                break;
            case DeclarationStatement declaration:
                if (declaration.IsInputShorthand)
                {
                    needsStdin = true;
                }

                if (declaration.Initializer is not null)
                {
                    AnalyzeRuntimeUsage(declaration.Initializer, ref needsStdin, ref needsStdout);
                }
                break;
            case ExpressionStatement expressionStatement:
                AnalyzeRuntimeUsage(expressionStatement.Expression, ref needsStdin, ref needsStdout);
                break;
            case AssignmentStatement assignment:
                AnalyzeRuntimeUsage(assignment.Target, ref needsStdin, ref needsStdout);
                AnalyzeRuntimeUsage(assignment.Value, ref needsStdin, ref needsStdout);
                break;
            case OutputStatement output:
                needsStdout = true;
                AnalyzeRuntimeUsage(output.Expression, ref needsStdin, ref needsStdout);
                break;
            case ReturnStatement @return when @return.Expression is not null:
                AnalyzeRuntimeUsage(@return.Expression, ref needsStdin, ref needsStdout);
                break;
            case IfStatement ifStatement:
                AnalyzeRuntimeUsage(ifStatement.Condition, ref needsStdin, ref needsStdout);
                AnalyzeRuntimeUsage(ifStatement.ThenBranch, ref needsStdin, ref needsStdout);
                if (ifStatement.ElseBranch is not null)
                {
                    AnalyzeRuntimeUsage(ifStatement.ElseBranch, ref needsStdin, ref needsStdout);
                }
                break;
            case WhileStatement whileStatement:
                AnalyzeRuntimeUsage(whileStatement.Condition, ref needsStdin, ref needsStdout);
                AnalyzeRuntimeUsage(whileStatement.Body, ref needsStdin, ref needsStdout);
                break;
            case CStyleForStatement cStyleFor:
                if (cStyleFor.HeaderText.Contains("stdin", StringComparison.Ordinal))
                {
                    needsStdin = true;
                }

                if (cStyleFor.HeaderText.Contains("stdout", StringComparison.Ordinal))
                {
                    needsStdout = true;
                }

                AnalyzeRuntimeUsage(cStyleFor.Body, ref needsStdin, ref needsStdout);
                break;
            case ForInStatement forIn:
                AnalyzeRuntimeUsage(forIn.Source, ref needsStdin, ref needsStdout);
                AnalyzeRuntimeUsage(forIn.Body, ref needsStdin, ref needsStdout);
                break;
            case FastForStatement fastFor:
                AnalyzeRuntimeUsage(fastFor.Source, ref needsStdin, ref needsStdout);
                AnalyzeRuntimeUsage(fastFor.Body, ref needsStdin, ref needsStdout);
                break;
        }
    }

    private static void AnalyzeRuntimeUsage(Expression expression, ref bool needsStdin, ref bool needsStdout)
    {
        switch (expression)
        {
            case IdentifierExpression identifier:
                if (identifier.Name == "stdin")
                {
                    needsStdin = true;
                }
                else if (identifier.Name == "stdout")
                {
                    needsStdout = true;
                }
                break;
            case InterpolatedStringExpression interpolated:
                foreach (InterpolatedStringPart part in interpolated.Parts)
                {
                    if (part is InterpolatedStringInterpolationPart interpolation)
                    {
                        AnalyzeRuntimeUsage(interpolation.Expression, ref needsStdin, ref needsStdout);
                    }
                }
                break;
            case TupleExpression tuple:
                foreach (Expression element in tuple.Elements)
                {
                    AnalyzeRuntimeUsage(element, ref needsStdin, ref needsStdout);
                }
                break;
            case BlockExpression block:
                AnalyzeRuntimeUsage(block.Block, ref needsStdin, ref needsStdout);
                break;
            case IfExpression ifExpression:
                AnalyzeRuntimeUsage(ifExpression.Condition, ref needsStdin, ref needsStdout);
                AnalyzeRuntimeUsage(ifExpression.ThenExpression, ref needsStdin, ref needsStdout);
                AnalyzeRuntimeUsage(ifExpression.ElseExpression, ref needsStdin, ref needsStdout);
                break;
            case ConditionalExpression conditional:
                AnalyzeRuntimeUsage(conditional.Condition, ref needsStdin, ref needsStdout);
                AnalyzeRuntimeUsage(conditional.WhenTrue, ref needsStdin, ref needsStdout);
                AnalyzeRuntimeUsage(conditional.WhenFalse, ref needsStdin, ref needsStdout);
                break;
            case UnaryExpression unary:
                AnalyzeRuntimeUsage(unary.Operand, ref needsStdin, ref needsStdout);
                break;
            case AssignmentExpression assignment:
                AnalyzeRuntimeUsage(assignment.Target, ref needsStdin, ref needsStdout);
                AnalyzeRuntimeUsage(assignment.Value, ref needsStdin, ref needsStdout);
                break;
            case PrefixExpression prefix:
                AnalyzeRuntimeUsage(prefix.Operand, ref needsStdin, ref needsStdout);
                break;
            case PostfixExpression postfix:
                AnalyzeRuntimeUsage(postfix.Operand, ref needsStdin, ref needsStdout);
                break;
            case BinaryExpression binary:
                AnalyzeRuntimeUsage(binary.Left, ref needsStdin, ref needsStdout);
                AnalyzeRuntimeUsage(binary.Right, ref needsStdin, ref needsStdout);
                break;
            case RangeExpression range:
                AnalyzeRuntimeUsage(range.Start, ref needsStdin, ref needsStdout);
                AnalyzeRuntimeUsage(range.End, ref needsStdin, ref needsStdout);
                if (range.Step is not null)
                {
                    AnalyzeRuntimeUsage(range.Step, ref needsStdin, ref needsStdout);
                }
                break;
            case IsPatternExpression isPattern:
                AnalyzeRuntimeUsage(isPattern.Left, ref needsStdin, ref needsStdout);
                if (isPattern.Pattern is ConstantPatternSyntax constantPattern)
                {
                    AnalyzeRuntimeUsage(constantPattern.Expression, ref needsStdin, ref needsStdout);
                }
                break;
            case CallExpression call:
                AnalyzeRuntimeUsage(call.Callee, ref needsStdin, ref needsStdout);
                foreach (ArgumentSyntax argument in call.Arguments)
                {
                    if (argument is ExpressionArgumentSyntax expressionArgument)
                    {
                        AnalyzeRuntimeUsage(expressionArgument.Expression, ref needsStdin, ref needsStdout);
                    }
                }
                break;
            case MemberAccessExpression member:
                AnalyzeRuntimeUsage(member.Receiver, ref needsStdin, ref needsStdout);
                break;
            case IndexExpression index:
                AnalyzeRuntimeUsage(index.Receiver, ref needsStdin, ref needsStdout);
                foreach (Expression argument in index.Arguments)
                {
                    AnalyzeRuntimeUsage(argument, ref needsStdin, ref needsStdout);
                }
                break;
            case WithExpression @with:
                AnalyzeRuntimeUsage(@with.Receiver, ref needsStdin, ref needsStdout);
                break;
            case SwitchExpression @switch:
                AnalyzeRuntimeUsage(@switch.Receiver, ref needsStdin, ref needsStdout);
                break;
            case FromEndExpression fromEnd:
                AnalyzeRuntimeUsage(fromEnd.Operand, ref needsStdin, ref needsStdout);
                break;
            case SliceExpression slice:
                if (slice.Start is not null)
                {
                    AnalyzeRuntimeUsage(slice.Start, ref needsStdin, ref needsStdout);
                }

                if (slice.End is not null)
                {
                    AnalyzeRuntimeUsage(slice.End, ref needsStdin, ref needsStdout);
                }
                break;
            case TupleProjectionExpression projection:
                AnalyzeRuntimeUsage(projection.Receiver, ref needsStdin, ref needsStdout);
                break;
            case LambdaExpression lambda:
                switch (lambda.Body)
                {
                    case LambdaExpressionBody expressionBody:
                        AnalyzeRuntimeUsage(expressionBody.Expression, ref needsStdin, ref needsStdout);
                        break;
                    case LambdaBlockBody blockBody:
                        AnalyzeRuntimeUsage(blockBody.Block, ref needsStdin, ref needsStdout);
                        break;
                }
                break;
            case NewExpression creation:
                foreach (ArgumentSyntax argument in creation.Arguments)
                {
                    if (argument is ExpressionArgumentSyntax expressionArgument)
                    {
                        AnalyzeRuntimeUsage(expressionArgument.Expression, ref needsStdin, ref needsStdout);
                    }
                }
                break;
            case NewArrayExpression newArray:
                foreach (Expression dimension in newArray.Dimensions)
                {
                    AnalyzeRuntimeUsage(dimension, ref needsStdin, ref needsStdout);
                }
                break;
            case TargetTypedNewArrayExpression targetTypedNewArray:
                foreach (Expression dimension in targetTypedNewArray.Dimensions)
                {
                    AnalyzeRuntimeUsage(dimension, ref needsStdin, ref needsStdout);
                }
                break;
            case CollectionExpression collection:
                foreach (CollectionElement element in collection.Elements)
                {
                    switch (element)
                    {
                        case ExpressionElement expressionElement:
                            AnalyzeRuntimeUsage(expressionElement.Expression, ref needsStdin, ref needsStdout);
                            break;
                        case SpreadElement spreadElement:
                            AnalyzeRuntimeUsage(spreadElement.Expression, ref needsStdin, ref needsStdout);
                            break;
                        case RangeElement rangeElement:
                            AnalyzeRuntimeUsage(rangeElement.Range, ref needsStdin, ref needsStdout);
                            break;
                        case BuilderElement builderElement:
                            AnalyzeRuntimeUsage(builderElement.Source, ref needsStdin, ref needsStdout);
                            switch (builderElement.Body)
                            {
                                case LambdaExpressionBody expressionBody:
                                    AnalyzeRuntimeUsage(expressionBody.Expression, ref needsStdin, ref needsStdout);
                                    break;
                                case LambdaBlockBody blockBody:
                                    AnalyzeRuntimeUsage(blockBody.Block, ref needsStdin, ref needsStdout);
                                    break;
                            }
                            break;
                    }
                }
                break;
            case AggregationExpression aggregation:
                AnalyzeRuntimeUsage(aggregation.Source, ref needsStdin, ref needsStdout);
                if (aggregation.WhereExpression is not null)
                {
                    AnalyzeRuntimeUsage(aggregation.WhereExpression, ref needsStdin, ref needsStdout);
                }
                AnalyzeRuntimeUsage(aggregation.Body, ref needsStdin, ref needsStdout);
                break;
            case GeneratorExpression generator:
                AnalyzeRuntimeUsage(generator.Source, ref needsStdin, ref needsStdout);
                switch (generator.Body)
                {
                    case LambdaExpressionBody expressionBody:
                        AnalyzeRuntimeUsage(expressionBody.Expression, ref needsStdin, ref needsStdout);
                        break;
                    case LambdaBlockBody blockBody:
                        AnalyzeRuntimeUsage(blockBody.Block, ref needsStdin, ref needsStdout);
                        break;
                }
                break;
        }
    }

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

        string iterator = EmitLoopBindingPattern(forIn.Iterator, "iter");
        _writer.WriteLine($"foreach (var {iterator} in {EmitEnumerable(forIn.Source)})");
        EmitWithBindingAliases(null, null, forIn.Iterator, iterator, () =>
        {
            EmitEmbeddedStatement(forIn.Body);
            return 0;
        });
    }

    private void EmitRangeForStatement(BindingTarget iteratorTarget, RangeExpression range, Statement body)
    {
        string iterator = EmitLoopBindingPattern(iteratorTarget, "iter");
        string iteratorType = IsLongRange(range, null) ? "long" : "int";
        string comparison = range.Kind == RangeKind.RightExclusive ? "<" : "<=";
        if (range.Step is null
            && CanInlineDirectRangeExpression(range.Start)
            && CanInlineDirectRangeExpression(range.End))
        {
            _writer.WriteLine($"for ({iteratorType} {iterator} = {EmitExpression(range.Start)}; {iterator} {comparison} {EmitExpression(range.End)}; {iterator}++)");
            EmitWithBindingAliases(null, null, iteratorTarget, iterator, () =>
            {
                EmitEmbeddedStatement(body);
                return 0;
            });
            return;
        }

        string startName = NextTemporary("start");
        string endName = NextTemporary("end");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine($"{iteratorType} {startName} = {EmitExpression(range.Start)};");
        _writer.WriteLine($"{iteratorType} {endName} = {EmitExpression(range.End)};");
        if (range.Step is null)
        {
            _writer.WriteLine($"for ({iteratorType} {iterator} = {startName}; {iterator} {comparison} {endName}; {iterator}++)");
        }
        else
        {
            string stepName = NextTemporary("step");
            _writer.WriteLine($"{iteratorType} {stepName} = {EmitExpression(range.Step)};");
            string decrementComparison = range.Kind == RangeKind.RightExclusive ? ">" : ">=";
            _writer.WriteLine("#if DEBUG");
            _writer.WriteLine($"if ({stepName} == 0) throw new InvalidOperationException(\"Range step cannot be zero.\");");
            _writer.WriteLine("#endif");
            _writer.WriteLine($"for ({iteratorType} {iterator} = {startName}; {stepName} > 0 ? {iterator} {comparison} {endName} : {iterator} {decrementComparison} {endName}; {iterator} += {stepName})");
        }
        EmitWithBindingAliases(null, null, iteratorTarget, iterator, () =>
        {
            EmitEmbeddedStatement(body);
            return 0;
        });
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

        string fallbackItemName = EmitLoopBindingPattern(fastFor.ItemTarget, "item");
        if (fastFor.IndexTarget is null)
        {
            _writer.WriteLine($"foreach (var {fallbackItemName} in {EmitEnumerable(fastFor.Source)})");
            EmitWithBindingAliases(null, null, fastFor.ItemTarget, fallbackItemName, () =>
            {
                EmitEmbeddedStatement(fastFor.Body);
                return 0;
            });
            return;
        }

        string fallbackIndexName = EmitLoopBindingPattern(fastFor.IndexTarget, "index");
        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine($"int {fallbackIndexName} = 0;");
        _writer.WriteLine($"foreach (var {fallbackItemName} in {EmitEnumerable(fastFor.Source)})");
        _writer.WriteLine("{");
        _writer.Indent();
        EmitWithBindingAliases(fastFor.IndexTarget, fallbackIndexName, fastFor.ItemTarget, fallbackItemName, () =>
        {
            EmitStatement(fastFor.Body);
            return 0;
        });
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
        bool useLong = IsLongRange(range, null);
        string iteratorType = useLong ? "long" : "int";
        string comparison = range.Kind == RangeKind.RightExclusive ? "<" : "<=";

        if (range.Step is null
            && CanInlineDirectRangeExpression(range.Start)
            && CanInlineDirectRangeExpression(range.End))
        {
            string directHeader = indexName is null
                ? $"for ({iteratorType} {itemName} = {EmitExpression(range.Start)}; {itemName} {comparison} {EmitExpression(range.End)}; {itemName}++)"
                : $"for ({iteratorType} {itemName} = {EmitExpression(range.Start)}, {indexName} = 0; {itemName} {comparison} {EmitExpression(range.End)}; {itemName}++, {indexName}++)";
            _writer.WriteLine(directHeader);
            _writer.WriteLine("{");
            _writer.Indent();
            EmitWithBindingAliases(fastFor.IndexTarget, indexName, fastFor.ItemTarget, itemName, () =>
            {
                string directAliases = EmitBindingAliasStatements(fastFor.IndexTarget, indexName, fastFor.ItemTarget, itemName);
                if (!string.IsNullOrWhiteSpace(directAliases))
                {
                    _writer.WriteLine(directAliases);
                }

                EmitStatement(fastFor.Body);
                return 0;
            });
            _writer.Unindent();
            _writer.WriteLine("}");
            return;
        }

        string startName = NextTemporary("start");
        string endName = NextTemporary("end");

        _writer.WriteLine("{");
        _writer.Indent();
        _writer.WriteLine($"{iteratorType} {startName} = {EmitExpression(range.Start)};");
        _writer.WriteLine($"{iteratorType} {endName} = {EmitExpression(range.End)};");
        if (indexName is not null)
        {
            _writer.WriteLine($"int {indexName} = 0;");
        }

        if (range.Step is null)
        {
            _writer.WriteLine($"for ({iteratorType} {itemName} = {startName}; {itemName} {comparison} {endName}; {itemName}++)");
        }
        else
        {
            string stepName = NextTemporary("step");
            string decrementComparison = range.Kind == RangeKind.RightExclusive ? ">" : ">=";
            _writer.WriteLine($"{iteratorType} {stepName} = {EmitExpression(range.Step)};");
            _writer.WriteLine("#if DEBUG");
            _writer.WriteLine($"if ({stepName} == 0) throw new InvalidOperationException(\"Range step cannot be zero.\");");
            _writer.WriteLine("#endif");
            _writer.WriteLine($"for ({iteratorType} {itemName} = {startName}; {stepName} > 0 ? {itemName} {comparison} {endName} : {itemName} {decrementComparison} {endName}; {itemName} += {stepName})");
        }
        _writer.WriteLine("{");
        _writer.Indent();
        EmitWithBindingAliases(fastFor.IndexTarget, indexName, fastFor.ItemTarget, itemName, () =>
        {
            string aliases = EmitBindingAliasStatements(fastFor.IndexTarget, indexName, fastFor.ItemTarget, itemName);
            if (!string.IsNullOrWhiteSpace(aliases))
            {
                _writer.WriteLine(aliases);
            }

            EmitStatement(fastFor.Body);
            return 0;
        });
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
