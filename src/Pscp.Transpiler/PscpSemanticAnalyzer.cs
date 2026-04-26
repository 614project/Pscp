using System.Text.RegularExpressions;

namespace Pscp.Transpiler;

internal sealed class SemanticAnalysisResult
{
    private readonly IReadOnlyDictionary<Expression, TypeSyntax?> _expressionTypes;
    private readonly IReadOnlySet<CallExpression> _intrinsicCalls;

    public SemanticAnalysisResult(
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyDictionary<Expression, TypeSyntax?> expressionTypes,
        IReadOnlySet<CallExpression> intrinsicCalls)
    {
        Diagnostics = diagnostics;
        _expressionTypes = expressionTypes;
        _intrinsicCalls = intrinsicCalls;
    }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public TypeSyntax? GetExpressionType(Expression expression)
        => _expressionTypes.TryGetValue(expression, out TypeSyntax? type) ? type : null;

    public bool IsIntrinsicCall(CallExpression call)
        => _intrinsicCalls.Contains(call);
}

internal static class PscpSemanticAnalyzer
{
    public static SemanticAnalysisResult Analyze(IReadOnlyList<Token> tokens, PscpProgram program)
    {
        Analyzer analyzer = new(tokens);
        analyzer.Predeclare(program);
        analyzer.Analyze(program);
        return new SemanticAnalysisResult(analyzer.Diagnostics, analyzer.ExpressionTypes, analyzer.IntrinsicCalls);
    }

    private enum SymbolKind
    {
        Local,
        Field,
        Function,
        Method,
        Intrinsic,
        Type,
    }

    private sealed record Symbol(SymbolKind Kind, TypeSyntax? Type, bool IsMutable, TypeInfo? TypeInfo = null);

    private sealed class TypeInfo
    {
        public TypeInfo(string name, bool isValueType)
        {
            Name = name;
            IsValueType = isValueType;
            Members = new Dictionary<string, Symbol>(StringComparer.Ordinal);
            NestedTypes = new Dictionary<string, TypeInfo>(StringComparer.Ordinal);
        }

        public string Name { get; }
        public bool IsValueType { get; }
        public Dictionary<string, Symbol> Members { get; }
        public Dictionary<string, TypeInfo> NestedTypes { get; }
    }

    private sealed class Scope
    {
        private readonly Dictionary<string, Symbol> _values = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TypeInfo> _types = new(StringComparer.Ordinal);

        public Scope(Scope? parent) => Parent = parent;

        public Scope? Parent { get; }

        public void DeclareValue(string name, Symbol symbol) => _values[name] = symbol;
        public void DeclareType(string name, TypeInfo typeInfo) => _types[name] = typeInfo;

        public bool TryResolveValue(string name, out Symbol? symbol)
        {
            if (_values.TryGetValue(name, out symbol)) return true;
            if (Parent is not null) return Parent.TryResolveValue(name, out symbol);
            symbol = null;
            return false;
        }

        public bool TryResolveType(string name, out TypeInfo? typeInfo)
        {
            if (_types.TryGetValue(name, out typeInfo)) return true;
            if (Parent is not null) return Parent.TryResolveType(name, out typeInfo);
            typeInfo = null;
            return false;
        }
    }

    private sealed class TokenTracker
    {
        private readonly IReadOnlyList<Token> _tokens;
        private int _index;

        public TokenTracker(IReadOnlyList<Token> tokens) => _tokens = tokens;

        public TextSpan Take(string name)
        {
            name = PscpIntrinsicCatalog.StripGenericSuffix(name);
            for (int i = _index; i < _tokens.Count; i++)
            {
                if (_tokens[i].Kind == TokenKind.Identifier && _tokens[i].Text == name)
                {
                    _index = i + 1;
                    return _tokens[i].Span;
                }
            }

            for (int i = 0; i < _tokens.Count; i++)
            {
                if (_tokens[i].Kind == TokenKind.Identifier && _tokens[i].Text == name)
                {
                    return _tokens[i].Span;
                }
            }

            return default;
        }
    }

    private sealed class Analyzer
    {
        private readonly TokenTracker _tracker;
        private readonly Scope _global = new(null);
        private readonly Dictionary<string, TypeInfo> _types = new(StringComparer.Ordinal);

        public Analyzer(IReadOnlyList<Token> tokens)
        {
            _tracker = new(tokens);
            Diagnostics = [];
            ExpressionTypes = new Dictionary<Expression, TypeSyntax?>(ReferenceEqualityComparer.Instance);
            IntrinsicCalls = new HashSet<CallExpression>(ReferenceEqualityComparer.Instance);

            foreach (string builtin in PscpIntrinsicCatalog.BuiltinTypes)
            {
                _global.DeclareType(builtin, new TypeInfo(builtin, isValueType: builtin is "int" or "long" or "double" or "decimal" or "bool" or "char"));
            }

            _global.DeclareValue("stdin", new Symbol(SymbolKind.Intrinsic, TypeName("stdin"), false));
            _global.DeclareValue("stdout", new Symbol(SymbolKind.Intrinsic, TypeName("stdout"), false));
            _global.DeclareValue("Array", new Symbol(SymbolKind.Intrinsic, TypeName("Array"), false));
            foreach (string intrinsic in PscpIntrinsicCatalog.IntrinsicCallNames)
            {
                _global.DeclareValue(intrinsic, new Symbol(SymbolKind.Intrinsic, null, false));
            }
        }

        public List<Diagnostic> Diagnostics { get; }
        public Dictionary<Expression, TypeSyntax?> ExpressionTypes { get; }
        public HashSet<CallExpression> IntrinsicCalls { get; }

        public void Predeclare(PscpProgram program)
        {
            foreach (TypeDeclaration type in program.Types) CollectType(type, null);
            foreach ((string name, TypeInfo info) in _types) _global.DeclareType(name, info);
            foreach (FunctionDeclaration function in program.Functions)
            {
                _global.DeclareValue(function.Name, new Symbol(SymbolKind.Function, Normalize(function.ReturnType), false));
            }

            foreach (DeclarationStatement declaration in program.GlobalStatements.OfType<DeclarationStatement>())
            {
                PredeclareGlobalDeclaration(declaration, _global);
            }
        }

        private void CollectType(TypeDeclaration declaration, TypeInfo? parent)
        {
            string fullName = parent is null ? declaration.Name : parent.Name + "." + declaration.Name;
            if (_types.ContainsKey(fullName)) return;
            TypeInfo info = new(fullName, IsValueTypeDeclarationHeader(declaration.HeaderText));
            _types[fullName] = info;
            _types.TryAdd(declaration.Name, info);
            if (parent is not null) parent.NestedTypes.TryAdd(declaration.Name, info);

            foreach ((string primaryMember, TypeSyntax? primaryType) in GetPrimaryConstructorMembers(declaration))
            {
                info.Members.TryAdd(primaryMember, new Symbol(SymbolKind.Field, Normalize(primaryType), true));
            }

            foreach (TypeMember member in declaration.Members)
            {
                switch (member)
                {
                    case FieldMember field:
                        foreach ((string name, TypeSyntax? type) in EnumerateNamedBindings(field.Declaration))
                        {
                            info.Members[name] = new Symbol(SymbolKind.Field, type, true);
                        }
                        break;
                    case PropertyMember property:
                        info.Members[property.Name] = new Symbol(SymbolKind.Field, Normalize(property.Type), false);
                        break;
                    case MethodMember method when !method.IsConstructor:
                        info.Members[method.Name] = new Symbol(SymbolKind.Method, Normalize(method.ReturnType), false);
                        break;
                    case OrderingShorthandMember:
                        info.Members["CompareTo"] = new Symbol(SymbolKind.Method, TypeName("int"), false);
                        break;
                    case NestedTypeMember nested:
                        CollectType(nested.Declaration, info);
                        break;
                }
            }
        }

        public void Analyze(PscpProgram program)
        {
            foreach (TypeDeclaration type in program.Types) AnalyzeType(type, _global);
            foreach (FunctionDeclaration function in program.Functions) AnalyzeFunction(function, _global);
            AnalyzeStatements(program.GlobalStatements, _global);
        }

        private void AnalyzeType(TypeDeclaration declaration, Scope parent)
        {
            WarnOnDeclarationName(declaration.Name, _tracker.Take(declaration.Name), "type");
            Scope scope = new(parent);
            TypeSyntax currentType = TypeName(declaration.Name);
            if (TryResolveType(declaration.Name, out TypeInfo? typeInfo) && typeInfo is not null)
            {
                currentType = TypeName(typeInfo.Name);
                foreach ((string name, TypeInfo nested) in typeInfo.NestedTypes) scope.DeclareType(name, nested);
                foreach ((string name, Symbol symbol) in typeInfo.Members) scope.DeclareValue(name, symbol);
            }

            foreach (TypeMember member in declaration.Members)
            {
                switch (member)
                {
                    case NestedTypeMember nested:
                        AnalyzeType(nested.Declaration, scope);
                        break;
                    case FieldMember field:
                        ConsumeDeclarationSignature(field.Declaration);
                        if (field.Declaration.Initializer is not null) AnalyzeExpression(field.Declaration.Initializer, scope);
                        break;
                    case MethodMember method:
                        AnalyzeMethod(method, scope, currentType);
                        break;
                    case PropertyMember property:
                        AnalyzeProperty(property, scope, currentType);
                        break;
                    case OperatorMember @operator:
                        AnalyzeOperator(@operator, scope, currentType);
                        break;
                    case OrderingShorthandMember ordering:
                        AnalyzeOrderingShorthand(ordering, scope, currentType);
                        break;
                }
            }
        }

        private void AnalyzeFunction(FunctionDeclaration function, Scope parent)
        {
            ConsumeType(function.ReturnType);
            TextSpan functionSpan = _tracker.Take(function.Name);
            WarnOnDeclarationName(function.Name, functionSpan, "function");
            Scope scope = new(parent);
            foreach (ParameterSyntax parameter in function.Parameters)
            {
                ConsumeType(parameter.Type);
                ConsumeBinding(parameter.Target);
                DeclareBinding(parameter.Target, Normalize(parameter.Type), scope, parameter.Modifier is ArgumentModifier.Ref or ArgumentModifier.Out);
            }

            AnalyzeBlock(function.Body, scope);
            ValidateRecursion(function.Name, function.IsRecursive, function.Body, functionSpan);
            ValidateValueReturningBody(function.Name, Normalize(function.ReturnType), function.Body, functionSpan);
        }

        private void AnalyzeMethod(MethodMember method, Scope parent, TypeSyntax? thisType = null)
        {
            if (!method.IsConstructor && method.ReturnType is not null) ConsumeType(method.ReturnType);
            TextSpan methodSpan = _tracker.Take(method.Name);
            if (!method.IsConstructor)
            {
                WarnOnDeclarationName(method.Name, methodSpan, "method");
            }

            Scope scope = new(parent);
            if (thisType is not null)
            {
                scope.DeclareValue("this", new Symbol(SymbolKind.Local, thisType, false));
                scope.DeclareValue("base", new Symbol(SymbolKind.Local, thisType, false));
            }

            foreach (ParameterSyntax parameter in method.Parameters)
            {
                ConsumeType(parameter.Type);
                ConsumeBinding(parameter.Target);
                DeclareBinding(parameter.Target, Normalize(parameter.Type), scope, parameter.Modifier is ArgumentModifier.Ref or ArgumentModifier.Out);
            }

            AnalyzeMethodBody(method.Body, scope);

            if (!method.IsConstructor && method.ReturnType is not null)
            {
                ValidateMethodBody(method.Name, Normalize(method.ReturnType) ?? TypeName("void"), method.Body, methodSpan);
            }
        }

        private void AnalyzeProperty(PropertyMember property, Scope parent, TypeSyntax thisType)
        {
            ConsumeType(property.Type);
            TextSpan propertySpan = _tracker.Take(property.Name);
            WarnOnDeclarationName(property.Name, propertySpan, "property");
            Scope scope = new(parent);
            scope.DeclareValue("this", new Symbol(SymbolKind.Local, thisType, false));
            scope.DeclareValue("base", new Symbol(SymbolKind.Local, thisType, false));
            AnalyzeMethodBody(property.Body, scope);
            ValidateMethodBody(property.Name, Normalize(property.Type) ?? TypeName("void"), property.Body, propertySpan);
        }

        private void AnalyzeOperator(OperatorMember @operator, Scope parent, TypeSyntax thisType)
        {
            ConsumeType(@operator.ReturnType);
            TextSpan operatorSpan = _tracker.Take("operator");
            Scope scope = new(parent);
            scope.DeclareValue("this", new Symbol(SymbolKind.Local, thisType, false));
            scope.DeclareValue("base", new Symbol(SymbolKind.Local, thisType, false));
            foreach (ParameterSyntax parameter in @operator.Parameters)
            {
                ConsumeType(parameter.Type);
                ConsumeBinding(parameter.Target);
                DeclareBinding(parameter.Target, Normalize(parameter.Type), scope, parameter.Modifier is ArgumentModifier.Ref or ArgumentModifier.Out);
            }

            AnalyzeMethodBody(@operator.Body, scope);
            ValidateMethodBody("operator", Normalize(@operator.ReturnType) ?? TypeName("void"), @operator.Body, operatorSpan);
        }

        private void AnalyzeOrderingShorthand(OrderingShorthandMember ordering, Scope parent, TypeSyntax thisType)
        {
            _tracker.Take("operator");
            Scope scope = new(parent);
            if (ordering.ParameterNames.Count <= 1)
            {
                scope.DeclareValue("this", new Symbol(SymbolKind.Local, thisType, false));
            }

            foreach (string parameterName in ordering.ParameterNames)
            {
                _tracker.Take(parameterName);
                scope.DeclareValue(parameterName, new Symbol(SymbolKind.Local, thisType, false));
            }

            AnalyzeMethodBody(ordering.Body, scope);
        }

        private void AnalyzeMethodBody(MethodBody body, Scope scope)
        {
            switch (body)
            {
                case BlockMethodBody blockBody:
                    AnalyzeBlock(blockBody.Block, scope);
                    break;
                case ExpressionMethodBody expressionBody:
                    AnalyzeExpression(expressionBody.Expression, scope);
                    break;
            }
        }

        private void AnalyzeBlock(BlockStatement block, Scope parent)
        {
            Scope scope = new(parent);
            foreach (Statement statement in block.Statements)
            {
                if (statement is LocalFunctionStatement localFunction)
                {
                    scope.DeclareValue(localFunction.Function.Name, new Symbol(SymbolKind.Function, Normalize(localFunction.Function.ReturnType), false));
                }
            }

            AnalyzeStatements(block.Statements, scope);
        }

        private void AnalyzeStatements(IReadOnlyList<Statement> statements, Scope scope)
        {
            foreach (Statement statement in statements) AnalyzeStatement(statement, scope);
        }

        private void AnalyzeStatement(Statement statement, Scope scope)
        {
            switch (statement)
            {
                case BlockStatement block:
                    AnalyzeBlock(block, scope);
                    break;
                case DeclarationStatement declaration:
                    AnalyzeDeclaration(declaration, scope);
                    break;
                case ExpressionStatement expressionStatement:
                    AnalyzeExpression(expressionStatement.Expression, scope);
                    break;
                case AssignmentStatement assignment:
                    AnalyzeAssignmentTarget(assignment.Target, scope);
                    AnalyzeExpression(assignment.Value, scope);
                    break;
                case OutputStatement output:
                    AnalyzeExpression(output.Expression, scope);
                    break;
                case IfStatement ifStatement:
                    AnalyzeExpression(ifStatement.Condition, scope);
                    AnalyzeStatement(ifStatement.ThenBranch, CreateConditionScope(scope, ifStatement.Condition, assumeTrue: true));
                    if (ifStatement.ElseBranch is not null) AnalyzeStatement(ifStatement.ElseBranch, CreateConditionScope(scope, ifStatement.Condition, assumeTrue: false));
                    break;
                case WhileStatement whileStatement:
                    AnalyzeExpression(whileStatement.Condition, scope);
                    AnalyzeStatement(whileStatement.Body, CreateConditionScope(scope, whileStatement.Condition, assumeTrue: true));
                    break;
                case ForInStatement forIn:
                    AnalyzeExpression(forIn.Source, scope);
                    Scope forScope = new(scope);
                    ConsumeBinding(forIn.Iterator);
                    DeclareBinding(forIn.Iterator, EnumerableElement(GetType(forIn.Source)), forScope, false);
                    AnalyzeStatement(forIn.Body, forScope);
                    break;
                case CStyleForStatement cStyleFor:
                    Scope cStyleScope = new(scope);
                    foreach (string headerBinding in GetCStyleForHeaderBindings(cStyleFor.HeaderText))
                    {
                        cStyleScope.DeclareValue(headerBinding, new Symbol(SymbolKind.Local, TypeName("int"), true));
                    }

                    AnalyzeStatement(cStyleFor.Body, cStyleScope);
                    break;
                case FastForStatement fastFor:
                    AnalyzeExpression(fastFor.Source, scope);
                    Scope fastScope = new(scope);
                    if (fastFor.IndexTarget is not null)
                    {
                        ConsumeBinding(fastFor.IndexTarget);
                        DeclareBinding(fastFor.IndexTarget, TypeName("int"), fastScope, false);
                    }

                    ConsumeBinding(fastFor.ItemTarget);
                    DeclareBinding(fastFor.ItemTarget, EnumerableElement(GetType(fastFor.Source)), fastScope, false);
                    AnalyzeStatement(fastFor.Body, fastScope);
                    break;
                case ReturnStatement returnStatement when returnStatement.Expression is not null:
                    AnalyzeExpression(returnStatement.Expression, scope);
                    break;
                case LocalFunctionStatement localFunction:
                    AnalyzeFunction(localFunction.Function, scope);
                    break;
            }
        }

        private void AnalyzeDeclaration(DeclarationStatement declaration, Scope scope)
        {
            ConsumeDeclarationSignature(declaration);
            if (declaration.Initializer is not null)
            {
                AnalyzeExpression(declaration.Initializer, scope);
                if (declaration.ExplicitType is not null)
                {
                    WarnIfNullabilityMismatch(declaration.Initializer, Normalize(declaration.ExplicitType), default);
                }
            }

            if (declaration.Initializer is TargetTypedNewArrayExpression targetTypedNewArray)
            {
                TypeSyntax? normalizedExplicitType = Normalize(declaration.ExplicitType);
                if (normalizedExplicitType is not ArrayTypeSyntax arrayType)
                {
                    Error("Target-typed array allocation requires an explicit array target type.", default);
                }
                else
                {
                    ExpressionTypes[targetTypedNewArray] = arrayType;
                    if (targetTypedNewArray.AutoConstructElements
                        && !IsKnownAutoConstructType(arrayType.ElementType))
                    {
                        Error("`new![n]` requires a known auto-constructible collection element type.", default);
                    }
                }
            }

            TypeSyntax? inferred = Normalize(declaration.ExplicitType) ?? GetType(declaration.Initializer);
            if (declaration.Targets.Count == 1)
            {
                DeclareBinding(declaration.Targets[0], inferred, scope, declaration.Mutability == MutabilityKind.Mutable);
                return;
            }

            if (inferred is not TupleTypeSyntax)
            {
                foreach (BindingTarget target in declaration.Targets)
                {
                    DeclareBinding(target, inferred, scope, declaration.Mutability == MutabilityKind.Mutable);
                }

                return;
            }

            for (int i = 0; i < declaration.Targets.Count; i++)
            {
                DeclareBinding(declaration.Targets[i], TupleElement(inferred, i), scope, declaration.Mutability == MutabilityKind.Mutable);
            }
        }

        private void AnalyzeAssignmentTarget(Expression expression, Scope scope, bool allowImmutableBindingTarget = false)
        {
            switch (expression)
            {
                case IdentifierExpression identifier:
                {
                    TextSpan span = _tracker.Take(identifier.Name);
                    if (!scope.TryResolveValue(identifier.Name, out Symbol? symbol) || symbol is null)
                    {
                        Error($"Undefined name `{identifier.Name}`.", span);
                    }
                    break;
                }
                case TupleExpression tuple:
                    foreach (Expression element in tuple.Elements) AnalyzeAssignmentTarget(element, scope, allowImmutableBindingTarget);
                    break;
                case MemberAccessExpression member:
                {
                    TypeSyntax? receiverType = AnalyzeExpression(member.Receiver, scope);
                    if (receiverType is NullableTypeSyntax nullable && IsValueTypeLike(nullable.InnerType))
                    {
                        Error("Assigning through a member of a nullable value type is not supported directly. Store it in a non-nullable temporary first.", default);
                    }

                    AnalyzeMember(member, scope);
                    break;
                }
                case IndexExpression index:
                    AnalyzeExpression(index.Receiver, scope);
                    foreach (Expression argument in index.Arguments) AnalyzeExpression(argument, scope);
                    break;
                case TupleProjectionExpression projection:
                    AnalyzeExpression(projection.Receiver, scope);
                    break;
                default:
                    AnalyzeExpression(expression, scope);
                    break;
            }
        }

        private static bool IsKnownDataStructureMutationTarget(TypeSyntax? targetType, AssignmentOperator op)
            => op is AssignmentOperator.AddAssign or AssignmentOperator.SubtractAssign
                && targetType is NamedTypeSyntax named
                && named.Name is "List" or "System.Collections.Generic.List"
                    or "LinkedList" or "System.Collections.Generic.LinkedList"
                    or "HashSet" or "System.Collections.Generic.HashSet"
                    or "SortedSet" or "System.Collections.Generic.SortedSet"
                    or "Dictionary" or "System.Collections.Generic.Dictionary"
                    or "Queue" or "System.Collections.Generic.Queue"
                    or "Stack" or "System.Collections.Generic.Stack"
                    or "PriorityQueue" or "System.Collections.Generic.PriorityQueue";

        private TypeSyntax? AnalyzeExpression(Expression? expression, Scope scope)
        {
            if (expression is null) return null;
            TypeSyntax? type = expression switch
            {
                LiteralExpression literal => LiteralType(literal),
                InterpolatedStringExpression interpolated => AnalyzeInterpolatedString(interpolated, scope),
                IdentifierExpression identifier => AnalyzeIdentifier(identifier, scope),
                TupleExpression tuple => new TupleTypeSyntax(tuple.Elements.Select(e => AnalyzeExpression(e, scope) ?? TypeName("object")).ToArray()),
                BlockExpression block => AnalyzeBlockLike(block.Block, scope),
                IfExpression ifExpression => Merge(AnalyzeExpression(ifExpression.ThenExpression, scope), AnalyzeExpression(ifExpression.ElseExpression, scope)),
                ConditionalExpression conditional => Merge(AnalyzeExpression(conditional.WhenTrue, scope), AnalyzeExpression(conditional.WhenFalse, scope)),
                UnaryExpression unary => AnalyzeUnaryExpression(unary, scope),
                AssignmentExpression assignment => AnalyzeAssignmentExpression(assignment, scope),
                PrefixExpression prefix => AnalyzePrefixExpression(prefix, scope),
                PostfixExpression postfix => AnalyzeExpression(postfix.Operand, scope),
                BinaryExpression binary => AnalyzeBinary(binary, scope),
                RangeExpression range => AnalyzeRange(range, scope),
                IsPatternExpression isPattern => AnalyzeIsPattern(isPattern, scope),
                CallExpression call => AnalyzeCall(call, scope),
                MemberAccessExpression member => AnalyzeMember(member, scope),
                IndexExpression index => AnalyzeIndex(index, scope),
                WithExpression @with => AnalyzeWithExpression(@with, scope),
                SwitchExpression @switch => AnalyzeSwitchExpression(@switch, scope),
                FromEndExpression fromEnd => AnalyzeFromEnd(fromEnd, scope),
                SliceExpression slice => AnalyzeSlice(slice, scope),
                TupleProjectionExpression projection => AnalyzeTupleProjection(projection, scope),
                LambdaExpression lambda => AnalyzeLambda(lambda, scope),
                NewExpression creation => AnalyzeNew(creation, scope),
                NewArrayExpression newArray => AnalyzeNewArray(newArray, scope),
                TargetTypedNewArrayExpression targetTypedNewArray => AnalyzeTargetTypedNewArray(targetTypedNewArray, scope),
                CollectionExpression collection => AnalyzeCollection(collection, scope),
                AggregationExpression aggregation => AnalyzeAggregation(aggregation, scope),
                GeneratorExpression generator => AnalyzeGenerator(generator, scope),
                _ => null,
            };

            ExpressionTypes[expression] = type;
            return type;
        }

        private TypeSyntax? AnalyzeIdentifier(IdentifierExpression identifier, Scope scope)
        {
            TextSpan span = _tracker.Take(identifier.Name);
            if (scope.TryResolveValue(identifier.Name, out Symbol? symbol) && symbol is not null) return symbol.Type;
            if (scope.TryResolveType(identifier.Name, out TypeInfo? typeInfo) && typeInfo is not null) return TypeName(typeInfo.Name);
            if (PscpIntrinsicCatalog.BuiltinTypes.Contains(identifier.Name) || PscpIntrinsicCatalog.IntrinsicCallNames.Contains(identifier.Name)) return TypeName(identifier.Name);
            Error($"Undefined name `{identifier.Name}`.", span);
            return null;
        }

        private TypeSyntax AnalyzeInterpolatedString(InterpolatedStringExpression interpolated, Scope scope)
        {
            foreach (InterpolatedStringPart part in interpolated.Parts)
            {
                if (part is InterpolatedStringInterpolationPart interpolation)
                {
                    AnalyzeExpression(interpolation.Expression, scope);
                }
            }

            return TypeName("string");
        }

        private TypeSyntax? AnalyzeAssignmentExpression(AssignmentExpression assignment, Scope scope)
        {
            TypeSyntax? targetType = GetType(assignment.Target) ?? InferAssignmentTargetType(assignment.Target, scope);
            AnalyzeAssignmentTarget(assignment.Target, scope, IsKnownDataStructureMutationTarget(targetType, assignment.Operator));
            AnalyzeExpression(assignment.Value, scope);
            WarnIfNullabilityMismatch(assignment.Value, Normalize(targetType), default);

            if (assignment.Value is TargetTypedNewArrayExpression targetTypedNewArray)
            {
                if (Normalize(targetType) is not ArrayTypeSyntax arrayType)
                {
                    Error("Target-typed array allocation requires an array target type.", default);
                }
                else
                {
                    ExpressionTypes[targetTypedNewArray] = arrayType;
                    if (targetTypedNewArray.AutoConstructElements
                        && !IsKnownAutoConstructType(arrayType.ElementType))
                    {
                        Error("`new![n]` requires a known auto-constructible collection element type.", default);
                    }
                }
            }

            if (targetType is NamedTypeSyntax named)
            {
                if (named.Name is "HashSet" or "System.Collections.Generic.HashSet"
                    or "SortedSet" or "System.Collections.Generic.SortedSet"
                    or "Dictionary" or "System.Collections.Generic.Dictionary"
                    && assignment.Operator is AssignmentOperator.AddAssign or AssignmentOperator.SubtractAssign)
                {
                    return TypeName("bool");
                }

                if (assignment.Operator == AssignmentOperator.AddAssign
                    && named.Name is "List" or "System.Collections.Generic.List"
                        or "LinkedList" or "System.Collections.Generic.LinkedList"
                        or "Queue" or "System.Collections.Generic.Queue"
                        or "Stack" or "System.Collections.Generic.Stack"
                        or "PriorityQueue" or "System.Collections.Generic.PriorityQueue")
                {
                    return TypeName("void");
                }
            }

            return targetType;
        }

        private TypeSyntax? InferAssignmentTargetType(Expression target, Scope scope)
        {
            TypeSyntax? type = target switch
            {
                IdentifierExpression identifier => scope.TryResolveValue(identifier.Name, out Symbol? symbol) ? symbol?.Type : null,
                MemberAccessExpression member => AnalyzeMember(member, scope),
                IndexExpression index => AnalyzeIndex(index, scope),
                TupleProjectionExpression projection => AnalyzeTupleProjection(projection, scope),
                TupleExpression tuple => new TupleTypeSyntax(tuple.Elements.Select(element => InferAssignmentTargetType(element, scope) ?? TypeName("object")).ToArray()),
                _ => null,
            };

            ExpressionTypes[target] = type;
            return type;
        }

        private TypeSyntax? AnalyzePrefixExpression(PrefixExpression prefix, Scope scope)
        {
            TypeSyntax? operandType = AnalyzeExpression(prefix.Operand, scope);
            if (operandType is not NamedTypeSyntax named || prefix.Operator != PostfixOperator.Decrement)
            {
                return operandType;
            }

            return named.Name switch
            {
                "Stack" or "System.Collections.Generic.Stack" => EnumerableElement(operandType),
                "Queue" or "System.Collections.Generic.Queue" => EnumerableElement(operandType),
                "PriorityQueue" or "System.Collections.Generic.PriorityQueue" => PriorityQueueElement(operandType),
                _ => operandType,
            };
        }

        private TypeSyntax? AnalyzeUnaryExpression(UnaryExpression unary, Scope scope)
        {
            TypeSyntax? operandType = AnalyzeExpression(unary.Operand, scope);
            if (unary.Operator == UnaryOperator.LogicalNot)
            {
                return TypeName("bool");
            }

            if (unary.Operator != UnaryOperator.Peek || operandType is not NamedTypeSyntax named)
            {
                return operandType;
            }

            return named.Name switch
            {
                "Stack" or "System.Collections.Generic.Stack" => EnumerableElement(operandType),
                "Queue" or "System.Collections.Generic.Queue" => EnumerableElement(operandType),
                "PriorityQueue" or "System.Collections.Generic.PriorityQueue" => PriorityQueueElement(operandType),
                _ => operandType,
            };
        }

        private TypeSyntax? AnalyzeBinary(BinaryExpression binary, Scope scope)
        {
            TypeSyntax? left = AnalyzeExpression(binary.Left, scope);
            TypeSyntax? right = AnalyzeExpression(binary.Right, scope);
            return binary.Operator switch
            {
                BinaryOperator.Add when IsNamed(left, "string") || IsNamed(right, "string") => TypeName("string"),
                BinaryOperator.Add or BinaryOperator.Subtract or BinaryOperator.Multiply or BinaryOperator.Divide or BinaryOperator.Modulo => Promote(left, right),
                BinaryOperator.ShiftLeft or BinaryOperator.ShiftRight => left,
                BinaryOperator.LessThan or BinaryOperator.LessThanOrEqual or BinaryOperator.GreaterThan or BinaryOperator.GreaterThanOrEqual or BinaryOperator.Equal or BinaryOperator.NotEqual or BinaryOperator.LogicalAnd or BinaryOperator.LogicalOr or BinaryOperator.LogicalXor => TypeName("bool"),
                BinaryOperator.Spaceship => TypeName("int"),
                _ => right ?? left,
            };
        }

        private TypeSyntax AnalyzeRange(RangeExpression range, Scope scope)
        {
            TypeSyntax? start = AnalyzeExpression(range.Start, scope);
            AnalyzeExpression(range.End, scope);
            if (range.Step is not null) AnalyzeExpression(range.Step, scope);
            return new NamedTypeSyntax("IEnumerable", Immutable.List(IsNamed(start, "long") ? TypeName("long") : TypeName("int")));
        }

        private TypeSyntax AnalyzeIsPattern(IsPatternExpression expression, Scope scope)
        {
            AnalyzeExpression(expression.Left, scope);
            if (expression.Pattern is ConstantPatternSyntax constantPattern) AnalyzeExpression(constantPattern.Expression, scope);
            return TypeName("bool");
        }

        private TypeSyntax? AnalyzeCall(CallExpression call, Scope scope)
        {
            TypeSyntax? calleeType = AnalyzeExpression(call.Callee, scope);
            if (call.Callee is IdentifierExpression conversionIdentifier && IsConversionKeyword(conversionIdentifier.Name))
            {
                AnalyzeArgumentsNormally(call.Arguments, scope);
                return TypeName(conversionIdentifier.Name);
            }

            if (TryAnalyzeIntrinsicCall(call, scope, out TypeSyntax? intrinsicType))
            {
                IntrinsicCalls.Add(call);
                return intrinsicType;
            }

            if (call.Callee is MemberAccessExpression stdinMember
                && stdinMember.Receiver is IdentifierExpression { Name: "stdin" })
            {
                AnalyzeArgumentsNormally(call.Arguments, scope);
                TypeSyntax? stdinType = AnalyzeKnownStdinCall(stdinMember.MemberName, call.Arguments);
                if (stdinType is not null)
                {
                    return stdinType;
                }
            }
            else
            {
                AnalyzeArgumentsNormally(call.Arguments, scope);
            }

            if (call.Callee is MemberAccessExpression member)
            {
                string memberName = PscpIntrinsicCatalog.StripGenericSuffix(member.MemberName);
                TypeSyntax? receiverType = UnwrapNullable(GetType(member.Receiver));
                if (receiverType is ArrayTypeSyntax && memberName == "Add")
                {
                    Error("Arrays do not contain an `Add` method. Use indexing or a growable collection type.", default);
                }

                if (receiverType is NamedTypeSyntax namedReceiver
                    && namedReceiver.Name is "PriorityQueue" or "System.Collections.Generic.PriorityQueue"
                    && memberName is "TryPeek" or "TryDequeue"
                    && (call.Arguments.Count != 2 || call.Arguments.Any(argument => !IsOutLike(argument))))
                {
                    Error($"`{namedReceiver.Name}.{memberName}` requires two `out` arguments: item and priority.", default);
                }
            }

            return calleeType;
        }

        private void AnalyzeArgumentsNormally(IReadOnlyList<ArgumentSyntax> arguments, Scope scope)
        {
            foreach (ArgumentSyntax argument in arguments)
            {
                switch (argument)
                {
                    case ExpressionArgumentSyntax expressionArgument:
                        AnalyzeExpression(expressionArgument.Expression, scope);
                        break;
                    case OutDeclarationArgumentSyntax outDeclaration:
                        ConsumeType(outDeclaration.Type);
                        ConsumeBinding(outDeclaration.Target);
                        DeclareBinding(outDeclaration.Target, Normalize(outDeclaration.Type), scope, true);
                        break;
                }
            }
        }

        private bool TryAnalyzeIntrinsicCall(CallExpression call, Scope scope, out TypeSyntax? intrinsicType)
        {
            intrinsicType = null;
            string? intrinsicName = null;
            if (call.Callee is IdentifierExpression identifier
                && PscpIntrinsicCatalog.IntrinsicCallNames.Contains(identifier.Name))
            {
                if (!scope.TryResolveValue(identifier.Name, out Symbol? symbol)
                    || symbol is null
                    || symbol.Kind == SymbolKind.Intrinsic)
                {
                    intrinsicName = identifier.Name;
                }
            }
            else if (call.Callee is MemberAccessExpression memberAccess)
            {
                string memberName = PscpIntrinsicCatalog.StripGenericSuffix(memberAccess.MemberName);
                if (PscpIntrinsicCatalog.IntrinsicCallNames.Contains(memberName)
                    && IsIntrinsicMemberCall(memberAccess, memberName, scope))
                {
                    intrinsicName = memberName;
                }
            }

            if (intrinsicName is null)
            {
                return false;
            }

            Expression? receiver = call.Callee is MemberAccessExpression receiverMember ? receiverMember.Receiver : null;
            if (!TryAnalyzeIntrinsicLambdaArguments(intrinsicName, receiver, call.Arguments, scope))
            {
                AnalyzeArgumentsNormally(call.Arguments, scope);
            }

            intrinsicType = IntrinsicType(intrinsicName, call.Arguments, receiver is null ? null : GetType(receiver));
            return true;
        }

        private bool IsIntrinsicMemberCall(MemberAccessExpression memberAccess, string memberName, Scope scope)
        {
            if (memberAccess.Receiver is IdentifierExpression receiverIdentifier
                && scope.TryResolveValue(receiverIdentifier.Name, out Symbol? receiverSymbol)
                && receiverSymbol is { Kind: SymbolKind.Intrinsic })
            {
                return true;
            }

            TypeSyntax? receiverType = GetType(memberAccess.Receiver) ?? AnalyzeExpression(memberAccess.Receiver, scope);
            if (memberName is "sum" or "sumBy" or "min" or "max" or "minBy" or "maxBy"
                or "count" or "any" or "all" or "find" or "findIndex" or "findLastIndex"
                or "sort" or "sortBy" or "sortWith" or "distinct" or "reverse" or "copy"
                or "groupCount" or "freq" or "index"
                or "map" or "filter" or "fold" or "scan" or "mapFold")
            {
                return EnumerableElement(receiverType) is not null;
            }

            return false;
        }

        private bool TryAnalyzeIntrinsicLambdaArguments(string intrinsicName, Expression? receiver, IReadOnlyList<ArgumentSyntax> arguments, Scope scope)
        {
            if (intrinsicName is "sumBy" or "minBy" or "maxBy" or "count" or "any" or "all" or "find" or "findIndex" or "findLastIndex" or "sortBy")
            {
                if (!TryGetSourceAndUnaryLambdaCall(receiver, arguments, out Expression? source, out LambdaExpression? lambda))
                {
                    return false;
                }

                AnalyzeExpression(source!, scope);
                TypeSyntax? elementType = EnumerableElement(GetType(source));
                AnalyzeLambdaWithParameterTypes(lambda!, scope, [elementType]);
                return true;
            }

            if (intrinsicName == "sortWith")
            {
                if (!TryGetSourceAndBinaryLambdaCall(receiver, arguments, out Expression? source, out LambdaExpression? lambda))
                {
                    return false;
                }

                AnalyzeExpression(source!, scope);
                TypeSyntax? elementType = EnumerableElement(GetType(source));
                AnalyzeLambdaWithParameterTypes(lambda!, scope, [elementType, elementType]);
                return true;
            }

            return false;
        }

        private void AnalyzeLambdaWithParameterTypes(LambdaExpression lambda, Scope scope, IReadOnlyList<TypeSyntax?> parameterTypes)
        {
            Scope lambdaScope = new(scope);
            for (int i = 0; i < lambda.Parameters.Count; i++)
            {
                TypeSyntax? parameterType = i < parameterTypes.Count ? parameterTypes[i] : Normalize(lambda.Parameters[i].Type);
                DeclareBinding(lambda.Parameters[i].Target, parameterType, lambdaScope, lambda.Parameters[i].Modifier is ArgumentModifier.Ref or ArgumentModifier.Out);
            }

            switch (lambda.Body)
            {
                case LambdaExpressionBody expressionBody:
                    AnalyzeExpression(expressionBody.Expression, lambdaScope);
                    break;
                case LambdaBlockBody blockBody:
                    AnalyzeBlock(blockBody.Block, lambdaScope);
                    break;
            }
        }

        private TypeSyntax? AnalyzeKnownStdinCall(string memberName, IReadOnlyList<ArgumentSyntax> arguments)
        {
            string root = PscpIntrinsicCatalog.StripGenericSuffix(memberName);
            return root switch
            {
                "int" or "readInt" => TypeName("int"),
                "long" or "readLong" => TypeName("long"),
                "double" or "readDouble" => TypeName("double"),
                "decimal" or "readDecimal" => TypeName("decimal"),
                "bool" or "readBool" => TypeName("bool"),
                "char" or "readChar" => TypeName("char"),
                "str" or "readString" or "line" or "readLine" or "readRestOfLine" => TypeName("string"),
                "lines" or "readLines" => new ArrayTypeSyntax(TypeName("string"), 1),
                "words" or "readWords" => new ArrayTypeSyntax(TypeName("string"), 1),
                "chars" or "readChars" => new ArrayTypeSyntax(TypeName("char"), 1),
                "array" or "readArray" when TryParseGenericTypeArguments(memberName, out IReadOnlyList<TypeSyntax>? arrayArgs) && arrayArgs is not null && arrayArgs.Count == 1
                    => new ArrayTypeSyntax(arrayArgs[0], 1),
                "list" or "readList" when TryParseGenericTypeArguments(memberName, out IReadOnlyList<TypeSyntax>? listArgs) && listArgs is not null && listArgs.Count == 1
                    => new NamedTypeSyntax("List", Immutable.List(listArgs[0])),
                "linkedList" or "readLinkedList" when TryParseGenericTypeArguments(memberName, out IReadOnlyList<TypeSyntax>? linkedListArgs) && linkedListArgs is not null && linkedListArgs.Count == 1
                    => new NamedTypeSyntax("LinkedList", Immutable.List(linkedListArgs[0])),
                "tuple2" or "readTuple2" when TryParseGenericTypeArguments(memberName, out IReadOnlyList<TypeSyntax>? tuple2Args) && tuple2Args is not null && tuple2Args.Count == 2
                    => new TupleTypeSyntax(tuple2Args),
                "tuple3" or "readTuple3" when TryParseGenericTypeArguments(memberName, out IReadOnlyList<TypeSyntax>? tuple3Args) && tuple3Args is not null && tuple3Args.Count == 3
                    => new TupleTypeSyntax(tuple3Args),
                "tuples2" or "readTuples2" when TryParseGenericTypeArguments(memberName, out IReadOnlyList<TypeSyntax>? tuples2Args) && tuples2Args is not null && tuples2Args.Count == 2
                    => new ArrayTypeSyntax(new TupleTypeSyntax(tuples2Args), 1),
                "tuples3" or "readTuples3" when TryParseGenericTypeArguments(memberName, out IReadOnlyList<TypeSyntax>? tuples3Args) && tuples3Args is not null && tuples3Args.Count == 3
                    => new ArrayTypeSyntax(new TupleTypeSyntax(tuples3Args), 1),
                "nestedArray" or "readNestedArray" when TryParseGenericTypeArguments(memberName, out IReadOnlyList<TypeSyntax>? nestedArrayArgs) && nestedArrayArgs is not null && nestedArrayArgs.Count == 1
                    => new ArrayTypeSyntax(nestedArrayArgs[0], 2),
                "gridInt" or "readGridInt" => new ArrayTypeSyntax(TypeName("int"), 2),
                "gridLong" or "readGridLong" => new ArrayTypeSyntax(TypeName("long"), 2),
                "charGrid" or "readCharGrid" => new ArrayTypeSyntax(TypeName("char"), 2),
                "wordGrid" or "readWordGrid" => new ArrayTypeSyntax(TypeName("string"), 2),
                _ => null,
            };
        }

        private static bool TryGetSourceAndUnaryLambdaCall(Expression? receiver, IReadOnlyList<ArgumentSyntax> arguments, out Expression? source, out LambdaExpression? lambda)
        {
            lambda = null;
            if (receiver is not null)
            {
                if (arguments.Count == 1
                    && arguments[0] is ExpressionArgumentSyntax expressionArgument
                    && expressionArgument.Modifier == ArgumentModifier.None
                    && string.IsNullOrWhiteSpace(expressionArgument.Name)
                    && expressionArgument.Expression is LambdaExpression lambdaExpression
                    && lambdaExpression.Parameters.Count == 1
                    && lambdaExpression.Parameters[0].Modifier == ArgumentModifier.None)
                {
                    source = receiver;
                    lambda = lambdaExpression;
                    return true;
                }

                source = null;
                return false;
            }

            if (arguments.Count == 2
                && arguments[0] is ExpressionArgumentSyntax sourceArgument
                && arguments[1] is ExpressionArgumentSyntax lambdaArgument
                && sourceArgument.Modifier == ArgumentModifier.None
                && lambdaArgument.Modifier == ArgumentModifier.None
                && string.IsNullOrWhiteSpace(sourceArgument.Name)
                && string.IsNullOrWhiteSpace(lambdaArgument.Name)
                && lambdaArgument.Expression is LambdaExpression argLambda
                && argLambda.Parameters.Count == 1
                && argLambda.Parameters[0].Modifier == ArgumentModifier.None)
            {
                source = sourceArgument.Expression;
                lambda = argLambda;
                return true;
            }

            source = null;
            return false;
        }

        private static bool TryGetSourceAndBinaryLambdaCall(Expression? receiver, IReadOnlyList<ArgumentSyntax> arguments, out Expression? source, out LambdaExpression? lambda)
        {
            lambda = null;
            if (receiver is not null)
            {
                if (arguments.Count == 1
                    && arguments[0] is ExpressionArgumentSyntax expressionArgument
                    && expressionArgument.Modifier == ArgumentModifier.None
                    && string.IsNullOrWhiteSpace(expressionArgument.Name)
                    && expressionArgument.Expression is LambdaExpression lambdaExpression
                    && lambdaExpression.Parameters.Count == 2
                    && lambdaExpression.Parameters.All(parameter => parameter.Modifier == ArgumentModifier.None))
                {
                    source = receiver;
                    lambda = lambdaExpression;
                    return true;
                }

                source = null;
                return false;
            }

            if (arguments.Count == 2
                && arguments[0] is ExpressionArgumentSyntax sourceArgument
                && arguments[1] is ExpressionArgumentSyntax lambdaArgument
                && sourceArgument.Modifier == ArgumentModifier.None
                && lambdaArgument.Modifier == ArgumentModifier.None
                && string.IsNullOrWhiteSpace(sourceArgument.Name)
                && string.IsNullOrWhiteSpace(lambdaArgument.Name)
                && lambdaArgument.Expression is LambdaExpression argLambda
                && argLambda.Parameters.Count == 2
                && argLambda.Parameters.All(parameter => parameter.Modifier == ArgumentModifier.None))
            {
                source = sourceArgument.Expression;
                lambda = argLambda;
                return true;
            }

            source = null;
            return false;
        }

        private static bool TryParseGenericTypeArguments(string memberName, out IReadOnlyList<TypeSyntax>? types)
        {
            types = null;
            int open = memberName.IndexOf('<');
            if (open < 0 || !memberName.EndsWith(">", StringComparison.Ordinal))
            {
                return false;
            }

            List<string> parts = SplitTopLevelCommaSeparated(memberName[(open + 1)..^1]);
            if (parts.Count == 0)
            {
                return false;
            }

            List<TypeSyntax> parsed = [];
            foreach (string part in parts)
            {
                TypeSyntax? type = ParseTypeText(part);
                if (type is null)
                {
                    return false;
                }

                parsed.Add(type);
            }

            types = parsed;
            return true;
        }

        private static TypeSyntax? ParseTypeText(string text)
        {
            text = text.Trim();
            if (text.Length == 0)
            {
                return null;
            }

            if (text.EndsWith("?", StringComparison.Ordinal))
            {
                TypeSyntax? inner = ParseTypeText(text[..^1]);
                return inner is null ? null : new NullableTypeSyntax(inner);
            }

            int arrayDepth = 0;
            while (text.EndsWith("[]", StringComparison.Ordinal))
            {
                arrayDepth++;
                text = text[..^2].TrimEnd();
            }

            TypeSyntax? parsed = null;
            if (text.StartsWith("(", StringComparison.Ordinal) && text.EndsWith(")", StringComparison.Ordinal))
            {
                List<string> tupleParts = SplitTopLevelCommaSeparated(text[1..^1]);
                if (tupleParts.Count > 0)
                {
                    List<TypeSyntax> tupleTypes = [];
                    foreach (string part in tupleParts)
                    {
                        TypeSyntax? tupleType = ParseTypeText(part);
                        if (tupleType is null)
                        {
                            return null;
                        }

                        tupleTypes.Add(tupleType);
                    }

                    parsed = new TupleTypeSyntax(tupleTypes);
                }
            }
            else
            {
                int genericOpen = text.IndexOf('<');
                if (genericOpen >= 0 && text.EndsWith(">", StringComparison.Ordinal))
                {
                    string name = text[..genericOpen].Trim();
                    List<string> genericParts = SplitTopLevelCommaSeparated(text[(genericOpen + 1)..^1]);
                    List<TypeSyntax> genericTypes = [];
                    foreach (string part in genericParts)
                    {
                        TypeSyntax? genericType = ParseTypeText(part);
                        if (genericType is null)
                        {
                            return null;
                        }

                        genericTypes.Add(genericType);
                    }

                    parsed = new NamedTypeSyntax(name, Immutable.List(genericTypes.ToArray()));
                }
                else
                {
                    parsed = new NamedTypeSyntax(text, Immutable.List<TypeSyntax>());
                }
            }

            if (parsed is null)
            {
                return null;
            }

            while (arrayDepth-- > 0)
            {
                parsed = new ArrayTypeSyntax(parsed, 1);
            }

            return parsed;
        }

        private static List<string> SplitTopLevelCommaSeparated(string text)
        {
            List<string> parts = [];
            int angleDepth = 0;
            int parenDepth = 0;
            int bracketDepth = 0;
            int start = 0;

            for (int i = 0; i < text.Length; i++)
            {
                switch (text[i])
                {
                    case '<':
                        angleDepth++;
                        break;
                    case '>':
                        angleDepth = Math.Max(0, angleDepth - 1);
                        break;
                    case '(':
                        parenDepth++;
                        break;
                    case ')':
                        parenDepth = Math.Max(0, parenDepth - 1);
                        break;
                    case '[':
                        bracketDepth++;
                        break;
                    case ']':
                        bracketDepth = Math.Max(0, bracketDepth - 1);
                        break;
                    case ',' when angleDepth == 0 && parenDepth == 0 && bracketDepth == 0:
                        parts.Add(text[start..i].Trim());
                        start = i + 1;
                        break;
                }
            }

            string last = text[start..].Trim();
            if (last.Length > 0)
            {
                parts.Add(last);
            }

            return parts;
        }

        private TypeSyntax? AnalyzeMember(MemberAccessExpression member, Scope scope)
        {
            string memberName = PscpIntrinsicCatalog.StripGenericSuffix(member.MemberName);
            TextSpan span = _tracker.Take(member.MemberName);

            if (memberName is "asc" or "desc")
            {
                if (!TryGetTypeLikeReceiverName(member.Receiver, scope, out string? typeLikeName))
                {
                    Error($"`{memberName}` comparator sugar requires a type receiver such as `int.{memberName}` or `MyType.{memberName}`.", span);
                    return new NamedTypeSyntax("IComparer", Immutable.List(TypeName("object")));
                }

                return new NamedTypeSyntax("IComparer", Immutable.List(TypeName(typeLikeName!)));
            }

            bool hasTypeLikeReceiver = TryGetTypeLikeReceiverName(member.Receiver, scope, out string? typeLikeReceiverName);
            TypeSyntax? receiverType = hasTypeLikeReceiver ? TypeName(typeLikeReceiverName!) : AnalyzeExpression(member.Receiver, scope);
            TypeSyntax? effectiveReceiverType = UnwrapNullable(receiverType);
            if (receiverType is NullableTypeSyntax)
            {
                Warning($"Possible null dereference on nullable receiver when accessing `{memberName}`.", span);
            }

            string receiverName = hasTypeLikeReceiver
                ? typeLikeReceiverName!
                : member.Receiver is IdentifierExpression id ? id.Name : (effectiveReceiverType as NamedTypeSyntax)?.Name ?? string.Empty;

            if (PscpIntrinsicCatalog.TryGetKnownMembers(receiverName, out IReadOnlySet<string>? members) && members is not null)
            {
                if (!members.Contains(memberName))
                {
                    if (PscpIntrinsicCatalog.StrictIntrinsicReceivers.Contains(receiverName))
                    {
                        Error($"Unknown intrinsic member `{receiverName}.{memberName}`.", span);
                    }

                    return null;
                }

                return receiverName switch
                {
                    "stdout" => TypeName("void"),
                    "stdin" when memberName is "int" or "readInt" => TypeName("int"),
                    "stdin" when memberName is "long" or "readLong" => TypeName("long"),
                    "stdin" when memberName is "double" or "readDouble" => TypeName("double"),
                    "stdin" when memberName is "decimal" or "readDecimal" => TypeName("decimal"),
                    "stdin" when memberName is "bool" or "readBool" => TypeName("bool"),
                    "stdin" when memberName is "char" or "readChar" => TypeName("char"),
                    "stdin" when memberName is "str" or "readString" or "line" or "readLine" or "readRestOfLine" => TypeName("string"),
                    _ => null,
                };
            }

            if (hasTypeLikeReceiver)
            {
                if (TryResolveType(receiverName, out TypeInfo? receiverTypeInfo) && receiverTypeInfo is not null)
                {
                    if (!receiverTypeInfo.Members.TryGetValue(memberName, out Symbol? staticSymbol))
                    {
                        Error($"Type `{receiverTypeInfo.Name}` does not contain a member named `{memberName}`.", span);
                        return null;
                    }

                    return staticSymbol.Type;
                }

                return null;
            }

            if (effectiveReceiverType is NamedTypeSyntax receiverNamed && TryResolveType(receiverNamed.Name, out TypeInfo? typeInfo) && typeInfo is not null)
            {
                if (!typeInfo.Members.TryGetValue(memberName, out Symbol? symbol))
                {
                    Error($"Type `{typeInfo.Name}` does not contain a member named `{memberName}`.", span);
                    return null;
                }

                return symbol.Type;
            }

            if (effectiveReceiverType is ArrayTypeSyntax && memberName == "Length")
            {
                return TypeName("int");
            }

            if (effectiveReceiverType is NamedTypeSyntax named
                && memberName == "Count"
                && named.Name is "List" or "LinkedList" or "Queue" or "Stack" or "HashSet" or "Dictionary" or "PriorityQueue" or "SortedSet")
            {
                return TypeName("int");
            }

            if (TryInferKnownExternalMemberType(receiverName, effectiveReceiverType, hasTypeLikeReceiver, memberName, out TypeSyntax? knownExternalType))
            {
                return knownExternalType;
            }

            return null;
        }

        private TypeSyntax? AnalyzeIndex(IndexExpression index, Scope scope)
        {
            TypeSyntax? receiverType = AnalyzeExpression(index.Receiver, scope);
            if (receiverType is NullableTypeSyntax)
            {
                Warning("Possible null dereference on nullable receiver when indexing.", default);
            }

            foreach (Expression argument in index.Arguments) AnalyzeExpression(argument, scope);
            if (index.Arguments.Any(argument => argument is SliceExpression))
            {
                if (receiverType is not NamedTypeSyntax { Name: "string" } && receiverType is not ArrayTypeSyntax)
                {
                    Error("Slicing is supported only on strings and arrays.", default);
                }

                return receiverType switch
                {
                    NamedTypeSyntax { Name: "string" } => TypeName("string"),
                    ArrayTypeSyntax array => new ArrayTypeSyntax(array.ElementType, array.Depth),
                    _ => receiverType,
                };
            }

            return receiverType switch
                {
                    ArrayTypeSyntax { Depth: 1 } array => array.ElementType,
                    ArrayTypeSyntax array => new ArrayTypeSyntax(array.ElementType, array.Depth - 1),
                    NamedTypeSyntax named when named.TypeArguments.Count == 2
                        && named.Name is "Dictionary" or "System.Collections.Generic.Dictionary" => named.TypeArguments[1],
                    NamedTypeSyntax named when named.TypeArguments.Count == 1 => named.TypeArguments[0],
                    _ => null,
                };
        }

        private TypeSyntax? AnalyzeWithExpression(WithExpression withExpression, Scope scope)
            => AnalyzeExpression(withExpression.Receiver, scope);

        private TypeSyntax? AnalyzeSwitchExpression(SwitchExpression switchExpression, Scope scope)
            => AnalyzeExpression(switchExpression.Receiver, scope);

        private TypeSyntax AnalyzeFromEnd(FromEndExpression fromEnd, Scope scope)
        {
            AnalyzeExpression(fromEnd.Operand, scope);
            return TypeName("int");
        }

        private TypeSyntax? AnalyzeSlice(SliceExpression slice, Scope scope)
        {
            if (slice.Start is not null) AnalyzeExpression(slice.Start, scope);
            if (slice.End is not null) AnalyzeExpression(slice.End, scope);
            return null;
        }

        private TypeSyntax? AnalyzeTupleProjection(TupleProjectionExpression projection, Scope scope)
        {
            TypeSyntax? receiverType = AnalyzeExpression(projection.Receiver, scope);
            TypeSyntax? elementType = TupleElement(receiverType, projection.Position - 1);
            if (elementType is null)
            {
                Error($"Invalid tuple projection `. {projection.Position}`.".Replace(". ", "."), default);
            }

            return elementType;
        }

        private TypeSyntax? AnalyzeLambda(LambdaExpression lambda, Scope scope)
        {
            Scope lambdaScope = new(scope);
            foreach (LambdaParameter parameter in lambda.Parameters)
            {
                if (parameter.Type is not null) ConsumeType(parameter.Type);
                ConsumeBinding(parameter.Target);
                DeclareBinding(parameter.Target, Normalize(parameter.Type), lambdaScope, parameter.Modifier is ArgumentModifier.Ref or ArgumentModifier.Out);
            }

            return lambda.Body switch
            {
                LambdaExpressionBody expressionBody => AnalyzeExpression(expressionBody.Expression, lambdaScope),
                LambdaBlockBody blockBody => AnalyzeBlockLike(blockBody.Block, lambdaScope),
                _ => null,
            };
        }

        private TypeSyntax? AnalyzeNew(NewExpression creation, Scope scope)
        {
            if (creation.Type is not null) ConsumeType(creation.Type);
            foreach (ArgumentSyntax argument in creation.Arguments) if (argument is ExpressionArgumentSyntax ea) AnalyzeExpression(ea.Expression, scope);
            return Normalize(creation.Type);
        }

        private TypeSyntax AnalyzeNewArray(NewArrayExpression newArray, Scope scope)
        {
            ConsumeType(newArray.ElementType);
            foreach (Expression dimension in newArray.Dimensions) AnalyzeExpression(dimension, scope);
            return new ArrayTypeSyntax(newArray.ElementType, newArray.Dimensions.Count);
        }

        private TypeSyntax? AnalyzeTargetTypedNewArray(TargetTypedNewArrayExpression targetTypedNewArray, Scope scope)
        {
            foreach (Expression dimension in targetTypedNewArray.Dimensions) AnalyzeExpression(dimension, scope);
            return null;
        }

        private TypeSyntax AnalyzeCollection(CollectionExpression collection, Scope scope)
        {
            TypeSyntax? elementType = null;
            foreach (CollectionElement element in collection.Elements)
            {
                elementType = element switch
                {
                    ExpressionElement expressionElement => Merge(elementType, AnalyzeExpression(expressionElement.Expression, scope)),
                    RangeElement rangeElement => Merge(elementType, EnumerableElement(AnalyzeRange(rangeElement.Range, scope))),
                    SpreadElement spreadElement => Merge(elementType, EnumerableElement(AnalyzeExpression(spreadElement.Expression, scope))),
                    BuilderElement builderElement => Merge(elementType, AnalyzeBuilder(builderElement, scope)),
                    _ => elementType,
                };
            }

            return new ArrayTypeSyntax(elementType ?? TypeName("object"), 1);
        }

        private TypeSyntax? AnalyzeBuilder(BuilderElement builder, Scope scope)
        {
            AnalyzeExpression(builder.Source, scope);
            Scope builderScope = new(scope);
            if (builder.IndexTarget is not null)
            {
                ConsumeBinding(builder.IndexTarget);
                DeclareBinding(builder.IndexTarget, TypeName("int"), builderScope, false);
            }

            ConsumeBinding(builder.ItemTarget);
            DeclareBinding(builder.ItemTarget, EnumerableElement(GetType(builder.Source)), builderScope, false);
            return builder.Body switch
            {
                LambdaExpressionBody expressionBody => AnalyzeExpression(expressionBody.Expression, builderScope),
                LambdaBlockBody blockBody => AnalyzeBlockLike(blockBody.Block, builderScope),
                _ => null,
            };
        }

        private TypeSyntax? AnalyzeAggregation(AggregationExpression aggregation, Scope scope)
        {
            _tracker.Take(aggregation.AggregatorName);
            AnalyzeExpression(aggregation.Source, scope);
            Scope aggrScope = new(scope);
            if (aggregation.IndexTarget is not null)
            {
                ConsumeBinding(aggregation.IndexTarget);
                DeclareBinding(aggregation.IndexTarget, TypeName("int"), aggrScope, false);
            }

            ConsumeBinding(aggregation.ItemTarget);
            DeclareBinding(aggregation.ItemTarget, EnumerableElement(GetType(aggregation.Source)), aggrScope, false);
            if (aggregation.WhereExpression is not null) AnalyzeExpression(aggregation.WhereExpression, aggrScope);
            TypeSyntax? bodyType = AnalyzeExpression(aggregation.Body, aggrScope);
            return aggregation.AggregatorName switch
            {
                "count" => TypeName("int"),
                _ => bodyType,
            };
        }

        private TypeSyntax AnalyzeGenerator(GeneratorExpression generator, Scope scope)
        {
            AnalyzeExpression(generator.Source, scope);
            Scope generatorScope = new(scope);
            if (generator.IndexTarget is not null)
            {
                ConsumeBinding(generator.IndexTarget);
                DeclareBinding(generator.IndexTarget, TypeName("int"), generatorScope, false);
            }

            ConsumeBinding(generator.ItemTarget);
            DeclareBinding(generator.ItemTarget, EnumerableElement(GetType(generator.Source)), generatorScope, false);
            TypeSyntax? bodyType = generator.Body switch
            {
                LambdaExpressionBody expressionBody => AnalyzeExpression(expressionBody.Expression, generatorScope),
                LambdaBlockBody blockBody => AnalyzeBlockLike(blockBody.Block, generatorScope),
                _ => null,
            };
            return new NamedTypeSyntax("IEnumerable", Immutable.List(bodyType ?? TypeName("object")));
        }

        private TypeSyntax? AnalyzeBlockLike(BlockStatement block, Scope scope)
        {
            AnalyzeBlock(block, scope);
            return block.Statements.LastOrDefault() switch
            {
                ExpressionStatement { HasSemicolon: false } tail => GetType(tail.Expression),
                ReturnStatement { Expression: not null } tail => GetType(tail.Expression),
                _ => null,
            };
        }

        private void ValidateRecursion(string functionName, bool isRecursive, BlockStatement body, TextSpan functionSpan)
        {
            if (isRecursive || !ContainsSelfCall(body, functionName))
            {
                return;
            }

            Error($"Recursive self-reference to `{functionName}` requires the `rec` modifier.", functionSpan);
        }

        private void ValidateMethodBody(string name, TypeSyntax returnType, MethodBody body, TextSpan declarationSpan)
        {
            if (IsNamed(returnType, "void"))
            {
                return;
            }

            if (body is ExpressionMethodBody expressionBody)
            {
                TypeSyntax? expressionType = GetType(expressionBody.Expression);
                if (expressionType is not null && !CanImplicitlyConvert(expressionType, returnType))
                {
                    Error($"Expression-bodied member `{name}` returns `{DisplayType(expressionType)}`, but `{DisplayType(returnType)}` is required.", declarationSpan);
                }

                return;
            }

            if (body is BlockMethodBody blockBody)
            {
                ValidateValueReturningBody(name, returnType, blockBody.Block, declarationSpan);
            }
        }

        private void ValidateValueReturningBody(string name, TypeSyntax? returnType, BlockStatement body, TextSpan declarationSpan)
        {
            if (returnType is null || IsNamed(returnType, "void"))
            {
                return;
            }

            foreach (ReturnStatement returnStatement in EnumerateReturnStatements(body))
            {
                TypeSyntax? returnExpressionType = returnStatement.Expression is null ? null : GetType(returnStatement.Expression);
                if (returnStatement.Expression is null)
                {
                    Error($"`return` in `{name}` requires a value of type `{DisplayType(returnType)}`.", declarationSpan);
                    continue;
                }

                if (returnExpressionType is not null && !CanImplicitlyConvert(returnExpressionType, returnType))
                {
                    Error($"`return` in `{name}` returns `{DisplayType(returnExpressionType)}`, but `{DisplayType(returnType)}` is required.", declarationSpan);
                }
            }

            if (body.Statements.LastOrDefault() is ExpressionStatement { HasSemicolon: false } tail)
            {
                TypeSyntax? tailType = GetType(tail.Expression);
                if (tailType is not null && !CanImplicitlyConvert(tailType, returnType))
                {
                    Error($"Final expression in `{name}` returns `{DisplayType(tailType)}`, but `{DisplayType(returnType)}` is required.", declarationSpan);
                }

                return;
            }
        }

        private static IEnumerable<ReturnStatement> EnumerateReturnStatements(BlockStatement block)
        {
            foreach (Statement statement in block.Statements)
            {
                foreach (ReturnStatement returnStatement in EnumerateReturnStatements(statement))
                {
                    yield return returnStatement;
                }
            }
        }

        private static IEnumerable<ReturnStatement> EnumerateReturnStatements(Statement statement)
        {
            switch (statement)
            {
                case ReturnStatement returnStatement:
                    yield return returnStatement;
                    yield break;
                case BlockStatement block:
                    foreach (ReturnStatement returnStatement in EnumerateReturnStatements(block))
                    {
                        yield return returnStatement;
                    }

                    yield break;
                case IfStatement ifStatement:
                    foreach (ReturnStatement returnStatement in EnumerateReturnStatements(ifStatement.ThenBranch))
                    {
                        yield return returnStatement;
                    }

                    if (ifStatement.ElseBranch is not null)
                    {
                        foreach (ReturnStatement returnStatement in EnumerateReturnStatements(ifStatement.ElseBranch))
                        {
                            yield return returnStatement;
                        }
                    }

                    yield break;
                case WhileStatement whileStatement:
                    foreach (ReturnStatement returnStatement in EnumerateReturnStatements(whileStatement.Body))
                    {
                        yield return returnStatement;
                    }

                    yield break;
                case ForInStatement forIn:
                    foreach (ReturnStatement returnStatement in EnumerateReturnStatements(forIn.Body))
                    {
                        yield return returnStatement;
                    }

                    yield break;
                case CStyleForStatement cStyleFor:
                    foreach (ReturnStatement returnStatement in EnumerateReturnStatements(cStyleFor.Body))
                    {
                        yield return returnStatement;
                    }

                    yield break;
                case FastForStatement fastFor:
                    foreach (ReturnStatement returnStatement in EnumerateReturnStatements(fastFor.Body))
                    {
                        yield return returnStatement;
                    }

                    yield break;
            }
        }

        private static bool ContainsExplicitReturn(BlockStatement block)
            => EnumerateReturnStatements(block).Any();

        private static bool ContainsSelfCall(BlockStatement block, string functionName)
            => block.Statements.Any(statement => ContainsSelfCall(statement, functionName));

        private static bool ContainsSelfCall(Statement statement, string functionName)
            => statement switch
            {
                BlockStatement block => ContainsSelfCall(block, functionName),
                DeclarationStatement declaration => ContainsSelfCall(declaration.Initializer, functionName),
                ExpressionStatement expressionStatement => ContainsSelfCall(expressionStatement.Expression, functionName),
                AssignmentStatement assignment => ContainsSelfCall(assignment.Target, functionName) || ContainsSelfCall(assignment.Value, functionName),
                OutputStatement output => ContainsSelfCall(output.Expression, functionName),
                IfStatement ifStatement => ContainsSelfCall(ifStatement.Condition, functionName)
                    || ContainsSelfCall(ifStatement.ThenBranch, functionName)
                    || (ifStatement.ElseBranch is not null && ContainsSelfCall(ifStatement.ElseBranch, functionName)),
                WhileStatement whileStatement => ContainsSelfCall(whileStatement.Condition, functionName) || ContainsSelfCall(whileStatement.Body, functionName),
                ForInStatement forIn => ContainsSelfCall(forIn.Source, functionName) || ContainsSelfCall(forIn.Body, functionName),
                FastForStatement fastFor => ContainsSelfCall(fastFor.Source, functionName) || ContainsSelfCall(fastFor.Body, functionName),
                ReturnStatement returnStatement => ContainsSelfCall(returnStatement.Expression, functionName),
                LocalFunctionStatement => false,
                _ => false,
            };

        private static bool ContainsSelfCall(Expression? expression, string functionName)
            => expression switch
            {
                null => false,
                CallExpression { Callee: IdentifierExpression identifier } call when identifier.Name == functionName => true,
                CallExpression call => ContainsSelfCall(call.Callee, functionName) || call.Arguments.Any(argument => ContainsSelfCall(argument, functionName)),
                InterpolatedStringExpression interpolated => interpolated.Parts.OfType<InterpolatedStringInterpolationPart>().Any(part => ContainsSelfCall(part.Expression, functionName)),
                TupleExpression tuple => tuple.Elements.Any(element => ContainsSelfCall(element, functionName)),
                BlockExpression block => ContainsSelfCall(block.Block, functionName),
                IfExpression ifExpression => ContainsSelfCall(ifExpression.Condition, functionName) || ContainsSelfCall(ifExpression.ThenExpression, functionName) || ContainsSelfCall(ifExpression.ElseExpression, functionName),
                ConditionalExpression conditional => ContainsSelfCall(conditional.Condition, functionName) || ContainsSelfCall(conditional.WhenTrue, functionName) || ContainsSelfCall(conditional.WhenFalse, functionName),
                UnaryExpression unary => ContainsSelfCall(unary.Operand, functionName),
                AssignmentExpression assignment => ContainsSelfCall(assignment.Target, functionName) || ContainsSelfCall(assignment.Value, functionName),
                PrefixExpression prefix => ContainsSelfCall(prefix.Operand, functionName),
                PostfixExpression postfix => ContainsSelfCall(postfix.Operand, functionName),
                BinaryExpression binary => ContainsSelfCall(binary.Left, functionName) || ContainsSelfCall(binary.Right, functionName),
                RangeExpression range => ContainsSelfCall(range.Start, functionName) || ContainsSelfCall(range.Step, functionName) || ContainsSelfCall(range.End, functionName),
                IsPatternExpression isPattern => ContainsSelfCall(isPattern.Left, functionName) || (isPattern.Pattern is ConstantPatternSyntax constantPattern && ContainsSelfCall(constantPattern.Expression, functionName)),
                MemberAccessExpression member => ContainsSelfCall(member.Receiver, functionName),
                IndexExpression index => ContainsSelfCall(index.Receiver, functionName) || index.Arguments.Any(argument => ContainsSelfCall(argument, functionName)),
                SwitchExpression @switch => ContainsSelfCall(@switch.Receiver, functionName),
                FromEndExpression fromEnd => ContainsSelfCall(fromEnd.Operand, functionName),
                SliceExpression slice => ContainsSelfCall(slice.Start, functionName) || ContainsSelfCall(slice.End, functionName),
                TupleProjectionExpression projection => ContainsSelfCall(projection.Receiver, functionName),
                LambdaExpression lambda => lambda.Body switch
                {
                    LambdaExpressionBody expressionBody => ContainsSelfCall(expressionBody.Expression, functionName),
                    LambdaBlockBody blockBody => ContainsSelfCall(blockBody.Block, functionName),
                    _ => false,
                },
                NewExpression creation => creation.Arguments.Any(argument => ContainsSelfCall(argument, functionName)),
                NewArrayExpression newArray => newArray.Dimensions.Any(dimension => ContainsSelfCall(dimension, functionName)),
                TargetTypedNewArrayExpression targetTypedNewArray => targetTypedNewArray.Dimensions.Any(dimension => ContainsSelfCall(dimension, functionName)),
                CollectionExpression collection => collection.Elements.Any(element => element switch
                {
                    ExpressionElement expressionElement => ContainsSelfCall(expressionElement.Expression, functionName),
                    RangeElement rangeElement => ContainsSelfCall(rangeElement.Range, functionName),
                    SpreadElement spreadElement => ContainsSelfCall(spreadElement.Expression, functionName),
                    BuilderElement builderElement => ContainsSelfCall(builderElement.Source, functionName) || builderElement.Body switch
                    {
                        LambdaExpressionBody expressionBody => ContainsSelfCall(expressionBody.Expression, functionName),
                        LambdaBlockBody blockBody => ContainsSelfCall(blockBody.Block, functionName),
                        _ => false,
                    },
                    _ => false,
                }),
                AggregationExpression aggregation => ContainsSelfCall(aggregation.Source, functionName)
                    || ContainsSelfCall(aggregation.WhereExpression, functionName)
                    || ContainsSelfCall(aggregation.Body, functionName),
                GeneratorExpression generator => ContainsSelfCall(generator.Source, functionName) || generator.Body switch
                {
                    LambdaExpressionBody expressionBody => ContainsSelfCall(expressionBody.Expression, functionName),
                    LambdaBlockBody blockBody => ContainsSelfCall(blockBody.Block, functionName),
                    _ => false,
                },
                _ => false,
            };

        private static bool ContainsSelfCall(ArgumentSyntax argument, string functionName)
            => argument switch
            {
                ExpressionArgumentSyntax expressionArgument => ContainsSelfCall(expressionArgument.Expression, functionName),
                _ => false,
            };

        private static bool IsOutLike(ArgumentSyntax argument)
            => argument switch
            {
                OutDeclarationArgumentSyntax => true,
                ExpressionArgumentSyntax { Modifier: ArgumentModifier.Out } => true,
                _ => false,
            };

        private static bool CanImplicitlyConvert(TypeSyntax? source, TypeSyntax target)
        {
            if (source is null)
            {
                return false;
            }

            if (Equals(source, target))
            {
                return true;
            }

            return source is NamedTypeSyntax sourceNamed
                && target is NamedTypeSyntax targetNamed
                && sourceNamed.Name switch
                {
                    "int" => targetNamed.Name is "long" or "double" or "decimal",
                    "long" => targetNamed.Name is "double" or "decimal",
                    "char" => targetNamed.Name is "int" or "long" or "double" or "decimal",
                    _ => false,
                };
        }

        private static string DisplayType(TypeSyntax? type)
            => type switch
            {
                null => "void",
                NamedTypeSyntax named when named.TypeArguments.Count == 0 => named.Name,
                NamedTypeSyntax named => $"{named.Name}<{string.Join(", ", named.TypeArguments.Select(DisplayType))}>",
                TupleTypeSyntax tuple => $"({string.Join(", ", tuple.Elements.Select(DisplayType))})",
                ArrayTypeSyntax array => DisplayType(array.ElementType) + string.Concat(Enumerable.Repeat("[]", array.Depth)),
                SizedArrayTypeSyntax sized => DisplayType(sized.ElementType) + string.Concat(Enumerable.Repeat("[]", sized.Dimensions.Count)),
                _ => "object",
            };

        private TypeSyntax? IntrinsicType(string name, IReadOnlyList<ArgumentSyntax> args, TypeSyntax? receiverType)
        {
            TypeSyntax? first = receiverType ?? ArgType(args, 0);
            return name switch
            {
                "min" or "max" when receiverType is null && args.Count == 2 => Merge(ArgType(args, 0), ArgType(args, 1)),
                "min" or "max" or "sum" => EnumerableElement(first),
                "count" or "findIndex" or "findLastIndex" => TypeName("int"),
                "any" or "all" or "chmin" or "chmax" => TypeName("bool"),
                "find" when EnumerableElement(first) is TypeSyntax findElement => new NullableTypeSyntax(findElement),
                "minBy" or "maxBy" => EnumerableElement(first),
                "sort" or "sortBy" or "sortWith" or "distinct" or "reverse" or "copy" when EnumerableElement(first) is TypeSyntax elem => new ArrayTypeSyntax(elem, 1),
                "groupCount" or "freq" or "index" when EnumerableElement(first) is TypeSyntax key => new NamedTypeSyntax("Dictionary", Immutable.List(key, TypeName("int"))),
                "abs" => ArgType(args, 0),
                "sqrt" or "pow" => TypeName("double"),
                "clamp" => Merge(Merge(ArgType(args, 0), ArgType(args, 1)), ArgType(args, 2)),
                "gcd" or "lcm" => Merge(ArgType(args, 0), ArgType(args, 1)),
                "floor" or "ceil" or "round" => InferMathRoundingType(ArgType(args, 0)),
                "popcount" or "bitLength" => TypeName("int"),
                _ => null,
            };
        }

        private static TypeSyntax InferMathRoundingType(TypeSyntax? sourceType)
            => sourceType is NamedTypeSyntax { Name: "decimal" } ? TypeName("decimal") : TypeName("double");

        private static bool IsConversionKeyword(string name)
            => name is "int" or "long" or "double" or "decimal" or "bool" or "char" or "string";

        private TypeSyntax? ArgType(IReadOnlyList<ArgumentSyntax> args, int index)
            => index >= 0 && index < args.Count && args[index] is ExpressionArgumentSyntax expressionArgument ? GetType(expressionArgument.Expression) : null;

        private TypeSyntax? GetType(Expression? expression)
            => expression is not null && ExpressionTypes.TryGetValue(expression, out TypeSyntax? type) ? type : null;

        private void ConsumeDeclarationSignature(DeclarationStatement declaration)
        {
            if (declaration.ExplicitType is not null) ConsumeType(declaration.ExplicitType);
            foreach (BindingTarget target in declaration.Targets) ConsumeBinding(target);
        }

        private void ConsumeType(TypeSyntax type)
        {
            switch (type)
            {
                case NamedTypeSyntax named:
                    foreach (string part in named.Name.Split('.', StringSplitOptions.RemoveEmptyEntries)) _tracker.Take(part);
                    foreach (TypeSyntax argument in named.TypeArguments) ConsumeType(argument);
                    break;
                case TupleTypeSyntax tuple:
                    foreach (TypeSyntax element in tuple.Elements) ConsumeType(element);
                    break;
                case ArrayTypeSyntax array:
                    ConsumeType(array.ElementType);
                    break;
                case SizedArrayTypeSyntax sized:
                    ConsumeType(sized.ElementType);
                    break;
            }
        }

        private void ConsumeBinding(BindingTarget target)
        {
            switch (target)
            {
                case NameTarget nameTarget:
                    WarnOnDeclarationName(nameTarget.Name, _tracker.Take(nameTarget.Name), "binding");
                    break;
                case DiscardTarget:
                    _tracker.Take("_");
                    break;
                case TupleTarget tupleTarget:
                    foreach (BindingTarget element in tupleTarget.Elements) ConsumeBinding(element);
                    break;
            }
        }

        private static void DeclareBinding(BindingTarget target, TypeSyntax? type, Scope scope, bool isMutable)
        {
            switch (target)
            {
                case NameTarget nameTarget:
                    scope.DeclareValue(nameTarget.Name, new Symbol(SymbolKind.Local, type, isMutable));
                    break;
                case TupleTarget tupleTarget:
                    for (int i = 0; i < tupleTarget.Elements.Count; i++) DeclareBinding(tupleTarget.Elements[i], TupleElement(type, i), scope, isMutable);
                    break;
            }
        }

        private bool TryGetTypeLikeReceiverName(Expression expression, Scope scope, out string? name)
        {
            switch (expression)
            {
                case IdentifierExpression identifier when scope.TryResolveType(identifier.Name, out TypeInfo? typeInfo) && typeInfo is not null:
                    name = typeInfo.Name;
                    return true;
                case IdentifierExpression identifier when scope.TryResolveValue(identifier.Name, out Symbol? valueSymbol) && valueSymbol is not null:
                    name = null;
                    return false;
                case IdentifierExpression identifier when PscpIntrinsicCatalog.BuiltinTypes.Contains(identifier.Name):
                    name = identifier.Name;
                    return true;
                case IdentifierExpression identifier when PscpIntrinsicCatalog.IsLikelyExternalTypeLikeRoot(identifier.Name):
                    name = identifier.Name;
                    return true;
                case MemberAccessExpression member
                    when TryGetTypeLikeReceiverName(member.Receiver, scope, out string? receiverName)
                    && (TryResolveType(receiverName!, out _) || PscpIntrinsicCatalog.IsLikelyExternalTypeLikeSegment(member.MemberName)):
                    name = receiverName + "." + PscpIntrinsicCatalog.StripGenericSuffix(member.MemberName);
                    return true;
                default:
                    name = null;
                    return false;
            }
        }

        private bool TryResolveType(string name, out TypeInfo? typeInfo)
            => _types.TryGetValue(name, out typeInfo);

        private static TypeSyntax TypeName(string name) => new NamedTypeSyntax(name, Immutable.List<TypeSyntax>());
        private static TypeSyntax? Normalize(TypeSyntax? type) => type switch
        {
            SizedArrayTypeSyntax sized => new ArrayTypeSyntax(sized.ElementType, sized.Dimensions.Count),
            NullableTypeSyntax nullable => new NullableTypeSyntax(Normalize(nullable.InnerType) ?? nullable.InnerType),
            _ => type,
        };
        private static bool IsNamed(TypeSyntax? type, string name) => type is NamedTypeSyntax named && named.Name == name;
        private static TypeSyntax LiteralType(LiteralExpression literal) => literal.Kind switch
        {
            LiteralKind.Integer => literal.RawText.EndsWith("L", StringComparison.OrdinalIgnoreCase) ? TypeName("long") : TypeName("int"),
            LiteralKind.Float => literal.RawText.EndsWith("m", StringComparison.OrdinalIgnoreCase) ? TypeName("decimal") : TypeName("double"),
            LiteralKind.String => TypeName("string"),
            LiteralKind.Char => TypeName("char"),
            LiteralKind.True or LiteralKind.False => TypeName("bool"),
            _ => TypeName("object"),
        };
        private static TypeSyntax? TupleElement(TypeSyntax? type, int index) => type is TupleTypeSyntax tuple && index >= 0 && index < tuple.Elements.Count ? tuple.Elements[index] : null;
        private static TypeSyntax? EnumerableElement(TypeSyntax? type) => type switch
        {
            ArrayTypeSyntax { Depth: 1 } array => array.ElementType,
            ArrayTypeSyntax array => new ArrayTypeSyntax(array.ElementType, array.Depth - 1),
            NullableTypeSyntax nullable => EnumerableElement(nullable.InnerType),
            NamedTypeSyntax { Name: "string" } => TypeName("char"),
            NamedTypeSyntax named when named.TypeArguments.Count > 0 && named.Name is "IEnumerable" or "List" or "LinkedList" or "Queue" or "Stack" or "HashSet" or "SortedSet" => named.TypeArguments[0],
            NamedTypeSyntax named when named.TypeArguments.Count > 0 && named.Name is "PriorityQueue" or "System.Collections.Generic.PriorityQueue" => named.TypeArguments[0],
            _ => null,
        };

        private static TypeSyntax? PriorityQueueElement(TypeSyntax? type)
            => type is NamedTypeSyntax named && named.TypeArguments.Count > 0 ? named.TypeArguments[0] : null;

        private static TypeSyntax? UnwrapNullable(TypeSyntax? type)
            => type is NullableTypeSyntax nullable ? nullable.InnerType : type;

        private bool IsValueTypeLike(TypeSyntax? type)
        {
            TypeSyntax? normalized = UnwrapNullable(type);
            return normalized switch
            {
                TupleTypeSyntax => true,
                NamedTypeSyntax { Name: "int" or "long" or "double" or "decimal" or "bool" or "char" } => true,
                NamedTypeSyntax named when TryResolveType(named.Name, out TypeInfo? typeInfo) && typeInfo is not null => typeInfo.IsValueType,
                _ => false,
            };
        }

        private static bool IsValueTypeDeclarationHeader(string headerText)
        {
            string normalized = headerText.TrimStart();
            return normalized.StartsWith("struct ", StringComparison.Ordinal)
                || normalized.StartsWith("readonly struct ", StringComparison.Ordinal)
                || normalized.StartsWith("record struct ", StringComparison.Ordinal)
                || normalized.StartsWith("readonly record struct ", StringComparison.Ordinal);
        }

        private static bool IsKnownAutoConstructType(TypeSyntax? type)
            => type is NamedTypeSyntax named
                && named.Name is "List" or "System.Collections.Generic.List"
                    or "LinkedList" or "System.Collections.Generic.LinkedList"
                    or "Queue" or "System.Collections.Generic.Queue"
                    or "Stack" or "System.Collections.Generic.Stack"
                    or "HashSet" or "System.Collections.Generic.HashSet"
                    or "Dictionary" or "System.Collections.Generic.Dictionary"
                    or "SortedSet" or "System.Collections.Generic.SortedSet"
                    or "PriorityQueue" or "System.Collections.Generic.PriorityQueue";
        private static TypeSyntax? Merge(TypeSyntax? left, TypeSyntax? right) => Equals(left, right) ? left : Promote(left, right) ?? left ?? right;
        private static TypeSyntax? Promote(TypeSyntax? left, TypeSyntax? right)
        {
            if (left is not NamedTypeSyntax l || right is not NamedTypeSyntax r) return null;
            int Rank(string name) => name switch { "int" => 0, "long" => 1, "double" => 2, "decimal" => 3, _ => -1 };
            int lr = Rank(l.Name);
            int rr = Rank(r.Name);
            return lr < 0 || rr < 0 ? null : (lr >= rr ? left : right);
        }

        private void PredeclareGlobalDeclaration(DeclarationStatement declaration, Scope scope)
        {
            foreach ((string name, TypeSyntax? type) in EnumerateNamedBindings(declaration))
            {
                scope.DeclareValue(name, new Symbol(SymbolKind.Field, type, declaration.Mutability == MutabilityKind.Mutable));
            }
        }

        private static IEnumerable<(string Name, TypeSyntax? Type)> EnumerateNamedBindings(DeclarationStatement declaration)
        {
            TypeSyntax? normalizedType = Normalize(declaration.ExplicitType);
            if (declaration.Targets.Count == 1)
            {
                foreach (string name in EnumerateBindingNames(declaration.Targets[0]))
                {
                    yield return (name, normalizedType);
                }

                yield break;
            }

            if (normalizedType is TupleTypeSyntax tupleType)
            {
                for (int i = 0; i < declaration.Targets.Count; i++)
                {
                    foreach (string name in EnumerateBindingNames(declaration.Targets[i]))
                    {
                        yield return (name, i < tupleType.Elements.Count ? tupleType.Elements[i] : null);
                    }
                }

                yield break;
            }

            foreach (BindingTarget target in declaration.Targets)
            {
                foreach (string name in EnumerateBindingNames(target))
                {
                    yield return (name, normalizedType);
                }
            }
        }

        private static IEnumerable<string> EnumerateBindingNames(BindingTarget target)
        {
            switch (target)
            {
                case NameTarget nameTarget:
                    yield return nameTarget.Name;
                    break;
                case TupleTarget tupleTarget:
                    foreach (BindingTarget element in tupleTarget.Elements)
                    {
                        foreach (string name in EnumerateBindingNames(element))
                        {
                            yield return name;
                        }
                    }
                    break;
            }
        }

        private Scope CreateConditionScope(Scope parent, Expression condition, bool assumeTrue)
        {
            Scope narrowed = new(parent);
            ApplyNonNullConditionNarrowing(parent, narrowed, condition, assumeTrue);
            return narrowed;
        }

        private static void ApplyNonNullConditionNarrowing(Scope sourceScope, Scope targetScope, Expression condition, bool assumeTrue)
        {
            if (!TryGetNullComparisonCandidate(condition, out Expression? candidate, out bool isNonNullWhenTrue)
                || candidate is null
                || assumeTrue != isNonNullWhenTrue
                || !TryGetNarrowableIdentifier(candidate, out string? name)
                || name is null
                || !sourceScope.TryResolveValue(name, out Symbol? symbol)
                || symbol?.Type is not NullableTypeSyntax nullable)
            {
                return;
            }

            targetScope.DeclareValue(name, symbol with { Type = nullable.InnerType });
        }

        private static bool TryGetNullComparisonCandidate(Expression condition, out Expression? candidate, out bool isNonNullWhenTrue)
        {
            candidate = null;
            isNonNullWhenTrue = false;
            if (condition is not BinaryExpression binary
                || binary.Operator is not (BinaryOperator.Equal or BinaryOperator.NotEqual))
            {
                return false;
            }

            if (IsNullLiteral(binary.Left))
            {
                candidate = binary.Right;
            }
            else if (IsNullLiteral(binary.Right))
            {
                candidate = binary.Left;
            }
            else
            {
                return false;
            }

            isNonNullWhenTrue = binary.Operator == BinaryOperator.NotEqual;
            return true;
        }

        private static bool TryGetNarrowableIdentifier(Expression expression, out string? name)
        {
            switch (expression)
            {
                case IdentifierExpression identifier:
                    name = identifier.Name;
                    return true;
                case AssignmentExpression { Target: IdentifierExpression identifier, Operator: AssignmentOperator.Assign }:
                    name = identifier.Name;
                    return true;
                default:
                    name = null;
                    return false;
            }
        }

        private static bool IsNullLiteral(Expression expression)
            => expression is LiteralExpression { Kind: LiteralKind.Null };

        private static bool TryInferKnownExternalMemberType(
            string receiverName,
            TypeSyntax? receiverType,
            bool hasTypeLikeReceiver,
            string memberName,
            out TypeSyntax? type)
        {
            type = null;
            if (hasTypeLikeReceiver)
            {
                type = receiverName switch
                {
                    "Console" when memberName == "ReadLine" => new NullableTypeSyntax(TypeName("string")),
                    "Console" when memberName is "Write" or "WriteLine" => TypeName("void"),
                    "Math" when memberName is "Sqrt" or "Pow" or "Log" or "Log10" or "Sin" or "Cos" or "Tan" or "Asin" or "Acos" or "Atan" or "Atan2" => TypeName("double"),
                    "Math" when memberName is "Abs" or "Min" or "Max" => null,
                    _ => null,
                };
                return type is not null;
            }

            if (receiverType is NamedTypeSyntax { Name: "string" or "String" })
            {
                type = memberName switch
                {
                    "Length" or "IndexOf" or "LastIndexOf" => TypeName("int"),
                    "Contains" or "StartsWith" or "EndsWith" => TypeName("bool"),
                    "Split" => new ArrayTypeSyntax(TypeName("string"), 1),
                    "ToCharArray" => new ArrayTypeSyntax(TypeName("char"), 1),
                    "Substring" or "Replace" or "Trim" or "TrimStart" or "TrimEnd" or "ToLower" or "ToUpper" => TypeName("string"),
                    _ => null,
                };
                return type is not null;
            }

            return false;
        }


        private static IEnumerable<(string Name, TypeSyntax? Type)> GetPrimaryConstructorMembers(TypeDeclaration declaration)
        {
            int open = declaration.HeaderText.IndexOf('(');
            int close = declaration.HeaderText.LastIndexOf(')');
            if (open < 0 || close <= open) yield break;

            string parameterText = declaration.HeaderText[(open + 1)..close];
            foreach (string parameter in SplitTopLevelCommaSeparated(parameterText))
            {
                if (TryParsePrimaryConstructorParameter(parameter, out string? name, out TypeSyntax? type))
                {
                    yield return (name!, type);
                }
            }
        }

        private static bool TryParsePrimaryConstructorParameter(string parameterText, out string? name, out TypeSyntax? type)
        {
            name = null;
            type = null;

            string trimmed = parameterText.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            trimmed = Regex.Replace(trimmed, @"^(?:ref|out|in)\s+", string.Empty);
            Match match = Regex.Match(trimmed, @"([A-Za-z_][A-Za-z0-9_]*)\s*(?:=.*)?$");
            if (!match.Success)
            {
                return false;
            }

            name = match.Groups[1].Value;
            string typeText = trimmed[..match.Index].Trim();
            type = ParseTypeText(typeText);
            return type is not null;
        }

        private static IEnumerable<string> GetCStyleForHeaderBindings(string headerText)
        {
            string firstSegment = headerText.Split(';')[0];
            Match match = Regex.Match(firstSegment, @"^\s*(?:var\s+|let\s+|mut\s+)?[A-Za-z_][A-Za-z0-9_<>.,\[\]]*\s+([A-Za-z_][A-Za-z0-9_]*)\s*=");
            if (match.Success && match.Groups.Count > 1)
            {
                yield return match.Groups[1].Value;
            }
        }
        private void WarnOnDeclarationName(string name, TextSpan span, string role)
        {
            if (PscpIntrinsicCatalog.CSharpReservedKeywords.Contains(name))
            {
                Warning($"`{name}` is also a reserved C# keyword. Rename it or expect escaping in generated C#.", span);
            }

            if (PscpIntrinsicCatalog.GlobalValues.Contains(name))
            {
                Warning($"`{name}` shadows a PSCP intrinsic {role} name.", span);
            }
        }

        private void WarnIfNullabilityMismatch(Expression expression, TypeSyntax? targetType, TextSpan span)
        {
            if (targetType is null || !IsNonNullableReferenceType(targetType))
            {
                return;
            }

            if (expression is LiteralExpression { Kind: LiteralKind.Null })
            {
                Warning($"Assigning `null` to non-nullable `{DisplayType(targetType)}` may trigger nullable warnings in generated C#.", span);
                return;
            }

            TypeSyntax? sourceType = GetType(expression);
            if (sourceType is NullableTypeSyntax nullable && IsNonNullableReferenceType(nullable.InnerType) && IsNonNullableReferenceType(targetType))
            {
                Warning($"Assigning nullable `{DisplayType(sourceType)}` to non-nullable `{DisplayType(targetType)}` may trigger nullable warnings in generated C#.", span);
            }
        }

        private bool IsNonNullableReferenceType(TypeSyntax? type)
        {
            if (type is null || type is NullableTypeSyntax)
            {
                return false;
            }

            TypeSyntax normalized = Normalize(type) ?? type;
            return normalized switch
            {
                ArrayTypeSyntax => true,
                NamedTypeSyntax named => !IsValueTypeLike(named) && named.Name != "void",
                _ => false,
            };
        }

        private void Error(string message, TextSpan span) => Diagnostics.Add(new Diagnostic(message, span, DiagnosticSeverity.Error));
        private void Warning(string message, TextSpan span) => Diagnostics.Add(new Diagnostic(message, span, DiagnosticSeverity.Warning));
    }
}



