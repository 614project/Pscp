using System.Text.RegularExpressions;

namespace Pscp.Transpiler;

internal sealed class SemanticAnalysisResult
{
    private readonly IReadOnlyDictionary<Expression, TypeSyntax?> _expressionTypes;
    private readonly IReadOnlySet<CallExpression> _intrinsicCalls;

    public SemanticAnalysisResult(
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyDictionary<Expression, TypeSyntax?> expressionTypes,
        IReadOnlySet<CallExpression> intrinsicCalls,
        IReadOnlySet<string>? reassignedImmutableNames = null,
        IReadOnlySet<string>? mutatedNames = null)
    {
        Diagnostics = diagnostics;
        _expressionTypes = expressionTypes;
        _intrinsicCalls = intrinsicCalls;
        ReassignedImmutableNames = reassignedImmutableNames ?? new HashSet<string>(StringComparer.Ordinal);
        MutatedNames = mutatedNames ?? new HashSet<string>(StringComparer.Ordinal);
    }

    // Names of immutable bindings that are nevertheless assigned, incremented, or passed by ref/out somewhere.
    // Such bindings must not be lowered to C# `const`.
    public IReadOnlySet<string> ReassignedImmutableNames { get; }

    // Names of any bindings (mutable or not) that are assigned, incremented, or passed by ref/out after their
    // declaration. Values read through such names cannot be assumed loop-invariant.
    public IReadOnlySet<string> MutatedNames { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public TypeSyntax? GetExpressionType(Expression expression)
        => _expressionTypes.TryGetValue(expression, out TypeSyntax? type) ? type : null;

    public bool IsIntrinsicCall(CallExpression call)
        => _intrinsicCalls.Contains(call);

    // Top-level `let` constants that code inside type declarations reads; they become class-level constants.
    public IReadOnlySet<string> ConstantsUsedByTypes { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    // Fields declared immutable that are nevertheless assigned outside a constructor (`Type.field`).
    public IReadOnlySet<string> MutatedImmutableFields { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    // Spec §10.5: in tail position every expression statement returns its value except a plain assignment,
    // compound assignment or increment. Known data-structure rewrites are calls: `set += x` (HashSet, SortedSet,
    // Dictionary) and `--q` (Stack, Queue, PriorityQueue) return their value.
    public bool IsReturnEligible(Expression expression)
        => expression switch
        {
            AssignmentExpression assignment => assignment.IsExplicitValueAssignment
                || (assignment.Operator is AssignmentOperator.AddAssign or AssignmentOperator.SubtractAssign
                    && PscpSemanticAnalyzer.IsValueReturningSetLike(GetExpressionType(assignment.Target))),
            PrefixExpression { Operator: PostfixOperator.Decrement } prefix => PscpSemanticAnalyzer.IsPoppable(GetExpressionType(prefix.Operand)),
            PrefixExpression or PostfixExpression => false,
            _ => true,
        };
}

internal static class PscpSemanticAnalyzer
{
    public static SemanticAnalysisResult Analyze(PscpProgram program, SyntaxSpans spans, IReadOnlySet<string>? constantsUsedByTypes = null)
    {
        Analyzer analyzer = new(spans);
        analyzer.Predeclare(program);
        analyzer.Analyze(program);
        return new SemanticAnalysisResult(analyzer.Diagnostics, analyzer.ExpressionTypes, analyzer.IntrinsicCalls, analyzer.ReassignedImmutableNames, analyzer.MutatedNames)
        {
            ConstantsUsedByTypes = constantsUsedByTypes ?? new HashSet<string>(StringComparer.Ordinal),
            MutatedImmutableFields = analyzer.MutatedImmutableFields,
        };
    }

    internal static bool IsValueReturningSetLike(TypeSyntax? type)
        => type is NamedTypeSyntax { Name: "HashSet" or "System.Collections.Generic.HashSet" or "SortedSet" or "System.Collections.Generic.SortedSet" or "Dictionary" or "System.Collections.Generic.Dictionary" };

    internal static bool IsPoppable(TypeSyntax? type)
        => type is NamedTypeSyntax { Name: "Stack" or "System.Collections.Generic.Stack" or "Queue" or "System.Collections.Generic.Queue" or "PriorityQueue" or "System.Collections.Generic.PriorityQueue" };

    // Names under which the intrinsic receivers appear after binding, and how diagnostics show them.
    internal static string DisplayName(string name)
        => name switch
        {
            PscpBinder.StdinName => "stdin",
            PscpBinder.StdoutName => "stdout",
            _ => name,
        };

    private enum SymbolKind
    {
        Local,
        Field,
        Function,
        Method,
        Intrinsic,
        Type,
    }

    // `IsImmutableField` marks a type field declared without `mut`/`var` (spec §28.2): assignments outside a
    // constructor are reported, and the field is then generated as an ordinary field.
    private sealed record Symbol(SymbolKind Kind, TypeSyntax? Type, bool IsMutable, TypeInfo? TypeInfo = null, bool IsImmutableField = false, string? OwnerType = null);

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
        public bool HasParameterlessConstructor { get; set; } = true;
        public bool HasOrdering { get; set; }
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

    private sealed class Analyzer
    {
        private readonly SyntaxSpans _spans;
        private readonly Scope _global = new(null);
        private readonly Dictionary<string, TypeInfo> _types = new(StringComparer.Ordinal);
        private readonly Stack<string> _typeStack = new();
        private readonly HashSet<AssignmentExpression> _statementAssignments = new(ReferenceEqualityComparer.Instance);
        private bool _inConstructor;

        public Analyzer(SyntaxSpans spans)
        {
            _spans = spans;
            Diagnostics = [];
            ExpressionTypes = new Dictionary<Expression, TypeSyntax?>(ReferenceEqualityComparer.Instance);
            IntrinsicCalls = new HashSet<CallExpression>(ReferenceEqualityComparer.Instance);

            foreach (string builtin in PscpIntrinsicCatalog.BuiltinTypes)
            {
                _global.DeclareType(builtin, new TypeInfo(builtin, isValueType: builtin is "int" or "long" or "double" or "decimal" or "bool" or "char"));
            }

            _global.DeclareValue(PscpBinder.StdinName, new Symbol(SymbolKind.Intrinsic, TypeName("stdin"), false));
            _global.DeclareValue(PscpBinder.StdoutName, new Symbol(SymbolKind.Intrinsic, TypeName("stdout"), false));
            _global.DeclareValue("Array", new Symbol(SymbolKind.Intrinsic, TypeName("Array"), false));
            foreach (string intrinsic in PscpIntrinsicCatalog.IntrinsicCallNames)
            {
                _global.DeclareValue(intrinsic, new Symbol(SymbolKind.Intrinsic, null, false));
            }
        }

        public List<Diagnostic> Diagnostics { get; }
        public Dictionary<Expression, TypeSyntax?> ExpressionTypes { get; }
        public HashSet<CallExpression> IntrinsicCalls { get; }

        public HashSet<string> ReassignedImmutableNames { get; } = new(StringComparer.Ordinal);

        public HashSet<string> MutatedNames { get; } = new(StringComparer.Ordinal);

        public HashSet<string> MutatedImmutableFields { get; } = new(StringComparer.Ordinal);

        public void Predeclare(PscpProgram program)
        {
            foreach (TypeDeclaration type in program.Types) CollectType(type, null);
            foreach ((string name, TypeInfo info) in _types) _global.DeclareType(name, info);
            foreach (FunctionDeclaration function in program.Functions)
            {
                _global.DeclareValue(function.Name, new Symbol(SymbolKind.Function, Normalize(function.ReturnType), false));
                _functionArity[function.Name] = function.Parameters.Count;
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
            MethodMember[] constructors = declaration.Members.OfType<MethodMember>().Where(method => method.IsConstructor).ToArray();
            bool positional = declaration.HeaderText.IndexOf('(') is int open && open >= 0 && declaration.HeaderText.IndexOf(')', open) > open + 1;
            info.HasParameterlessConstructor = info.IsValueType
                || (constructors.Length == 0 ? !positional : constructors.Any(constructor => constructor.Parameters.Count == 0));
            info.HasOrdering = declaration.Members.Any(member => member is OrderingShorthandMember)
                || declaration.HeaderText.Contains("IComparable", StringComparison.Ordinal);
            _types[fullName] = info;
            _types.TryAdd(declaration.Name, info);
            if (parent is not null) parent.NestedTypes.TryAdd(declaration.Name, info);

            foreach ((string primaryMember, TypeSyntax? primaryType) in GetPrimaryConstructorMembers(declaration))
            {
                info.Members.TryAdd(primaryMember, new Symbol(SymbolKind.Field, Normalize(primaryType), true, OwnerType: fullName));
            }

            foreach (TypeMember member in declaration.Members)
            {
                switch (member)
                {
                    case FieldMember field:
                        foreach ((string name, TypeSyntax? type) in EnumerateNamedBindings(field.Declaration))
                        {
                            TypeSyntax? fieldType = type ?? (field.Declaration.Initializer is LiteralExpression literal ? LiteralType(literal) : null);
                            info.Members[name] = new Symbol(SymbolKind.Field, fieldType, field.Declaration.Mutability == MutabilityKind.Mutable, IsImmutableField: field.Declaration.Mutability == MutabilityKind.Immutable, OwnerType: fullName);
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

            // A statement in tail position returns its value, so it is only known to be discarded once the
            // enclosing function has been checked.
            foreach ((ExpressionStatement statement, string code, string message, TextSpan span) in _discardedValueWarnings)
            {
                if (!_tailStatements.Contains(statement))
                {
                    Warning(code, message, span);
                }
            }
        }

        private readonly HashSet<Statement> _tailStatements = new(ReferenceEqualityComparer.Instance);
        private readonly List<(ExpressionStatement Statement, string Code, string Message, TextSpan Span)> _discardedValueWarnings = [];

        // Expression statements whose value is the result of the enclosing function or value block (spec §10.5).
        private void MarkTailStatements(Statement? statement)
        {
            switch (statement)
            {
                case ExpressionStatement:
                    _tailStatements.Add(statement);
                    break;
                case BlockStatement { Statements.Count: > 0 } block:
                    MarkTailStatements(block.Statements[^1]);
                    break;
                case IfStatement { ElseBranch: not null } ifStatement:
                    MarkTailStatements(ifStatement.ThenBranch);
                    MarkTailStatements(ifStatement.ElseBranch);
                    break;
            }
        }

        private void AnalyzeType(TypeDeclaration declaration, Scope parent)
        {
            WarnOnDeclarationName(declaration.Name, _spans.GetName(declaration), "type");
            _typeStack.Push(declaration.Name);
            HashSet<string> constructorAssigned = new(StringComparer.Ordinal);
            _constructorAssignedFields.Push(constructorAssigned);
            try
            {
                AnalyzeTypeMembers(declaration, parent);
            }
            finally
            {
                _constructorAssignedFields.Pop();
                _typeStack.Pop();
            }

            ReportUnassignedImmutableFields(declaration, constructorAssigned);
        }

        private readonly Stack<HashSet<string>> _constructorAssignedFields = new();

        // Spec §28.2: an immutable field without an initializer must be assigned in a constructor.
        private void ReportUnassignedImmutableFields(TypeDeclaration declaration, HashSet<string> constructorAssigned)
        {
            foreach (FieldMember field in declaration.Members.OfType<FieldMember>())
            {
                DeclarationStatement fieldDeclaration = field.Declaration;
                if (fieldDeclaration.Mutability != MutabilityKind.Immutable
                    || fieldDeclaration.Initializer is not null
                    || fieldDeclaration.IsInputShorthand
                    || fieldDeclaration.ExplicitType is SizedArrayTypeSyntax
                    || IsKnownAutoConstructType(Normalize(fieldDeclaration.ExplicitType))
                    || field.Modifiers.Contains("static", StringComparer.Ordinal))
                {
                    continue;
                }

                foreach ((string name, _) in EnumerateNamedBindings(fieldDeclaration))
                {
                    if (!constructorAssigned.Contains(name) && !MutatedImmutableFields.Contains(declaration.Name + "." + name))
                    {
                        Warning(
                            DiagnosticCodes.UnassignedImmutableField,
                            $"Field `{name}` is immutable and never assigned, so it always keeps its default value. Give it an initializer, assign it in a constructor, or declare it `mut`.",
                            _spans.Get(fieldDeclaration.Targets.Count > 0 ? fieldDeclaration.Targets[0] : fieldDeclaration));
                    }
                }
            }
        }

        private void AnalyzeTypeMembers(TypeDeclaration declaration, Scope parent)
        {
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
            TextSpan functionSpan = _spans.GetName(function);
            WarnOnDeclarationName(function.Name, functionSpan, "function");
            if (ReferenceEquals(parent, _global) && function.Name == "Main" && function.Parameters.Count == 0)
            {
                Error("`Main()` is reserved for the generated program entry point. Rename the function.", functionSpan);
            }

            Scope scope = new(parent);
            DeclareParameters(function.Parameters, scope);
            bool savedConstructor = _inConstructor;
            _inConstructor = false;
            TypeSyntax? savedReturnType = _currentReturnType;
            _currentReturnType = Normalize(function.ReturnType);
            try
            {
                AnalyzeBlock(function.Body, scope);
            }
            finally
            {
                _inConstructor = savedConstructor;
                _currentReturnType = savedReturnType;
            }

            ValidateValueReturningBody(function.Name, Normalize(function.ReturnType), function.Body, functionSpan);
        }

        private TypeSyntax? _currentReturnType;

        private void DeclareParameters(IReadOnlyList<ParameterSyntax> parameters, Scope scope)
        {
            foreach (ParameterSyntax parameter in parameters)
            {
                CheckBindingNames(parameter.Target);
                DeclareBinding(parameter.Target, Normalize(parameter.Type), scope, parameter.Modifier is ArgumentModifier.Ref or ArgumentModifier.Out);
            }
        }

        private void AnalyzeMethod(MethodMember method, Scope parent, TypeSyntax? thisType = null)
        {
            TextSpan methodSpan = _spans.GetName(method);
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

            DeclareParameters(method.Parameters, scope);
            bool savedConstructor = _inConstructor;
            _inConstructor = method.IsConstructor;
            TypeSyntax? savedReturnType = _currentReturnType;
            _currentReturnType = method.IsConstructor ? TypeName("void") : Normalize(method.ReturnType);
            try
            {
                AnalyzeMethodBody(method.Body, scope);
            }
            finally
            {
                _inConstructor = savedConstructor;
                _currentReturnType = savedReturnType;
            }

            if (!method.IsConstructor && method.ReturnType is not null)
            {
                ValidateMethodBody(method.Name, Normalize(method.ReturnType) ?? TypeName("void"), method.Body, methodSpan);
            }
        }

        private void AnalyzeProperty(PropertyMember property, Scope parent, TypeSyntax thisType)
        {
            TextSpan propertySpan = _spans.GetName(property);
            WarnOnDeclarationName(property.Name, propertySpan, "property");
            Scope scope = new(parent);
            scope.DeclareValue("this", new Symbol(SymbolKind.Local, thisType, false));
            scope.DeclareValue("base", new Symbol(SymbolKind.Local, thisType, false));
            AnalyzeMethodBody(property.Body, scope);
            ValidateMethodBody(property.Name, Normalize(property.Type) ?? TypeName("void"), property.Body, propertySpan);
        }

        private void AnalyzeOperator(OperatorMember @operator, Scope parent, TypeSyntax thisType)
        {
            TextSpan operatorSpan = _spans.Get(@operator);
            Scope scope = new(parent);
            scope.DeclareValue("this", new Symbol(SymbolKind.Local, thisType, false));
            scope.DeclareValue("base", new Symbol(SymbolKind.Local, thisType, false));
            DeclareParameters(@operator.Parameters, scope);

            AnalyzeMethodBody(@operator.Body, scope);
            ValidateMethodBody("operator", Normalize(@operator.ReturnType) ?? TypeName("void"), @operator.Body, operatorSpan);
        }

        private void AnalyzeOrderingShorthand(OrderingShorthandMember ordering, Scope parent, TypeSyntax thisType)
        {
            Scope scope = new(parent);
            if (ordering.ParameterNames.Count <= 1)
            {
                scope.DeclareValue("this", new Symbol(SymbolKind.Local, thisType, false));
            }

            foreach (string parameterName in ordering.ParameterNames)
            {
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
                    if (expressionBody.Expression is AssignmentExpression bodyAssignment)
                    {
                        _statementAssignments.Add(bodyAssignment);
                    }

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
                    _functionArity[localFunction.Function.Name] = localFunction.Function.Parameters.Count;
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
                    if (expressionStatement.Expression is AssignmentExpression statementAssignment)
                    {
                        _statementAssignments.Add(statementAssignment);
                    }

                    AnalyzeExpression(expressionStatement.Expression, scope);
                    CheckExpressionStatement(expressionStatement);
                    break;
                case AssignmentStatement assignment:
                    AnalyzeAssignmentTarget(assignment.Target, scope);
                    AnalyzeExpression(assignment.Value, scope);
                    break;
                case OutputStatement output:
                    CheckRenderable(AnalyzeExpression(output.Expression, scope), output.Expression);
                    break;
                case IfStatement ifStatement:
                    AnalyzeExpression(ifStatement.Condition, scope);
                    AnalyzeEmbedded(ifStatement.ThenBranch, CreateConditionScope(scope, ifStatement.Condition, assumeTrue: true));
                    if (ifStatement.ElseBranch is not null) AnalyzeEmbedded(ifStatement.ElseBranch, CreateConditionScope(scope, ifStatement.Condition, assumeTrue: false));
                    break;
                case WhileStatement whileStatement:
                    AnalyzeExpression(whileStatement.Condition, scope);
                    AnalyzeEmbedded(whileStatement.Body, CreateConditionScope(scope, whileStatement.Condition, assumeTrue: true));
                    break;
                case ForInStatement forIn:
                {
                    AnalyzeExpression(forIn.Source, scope);
                    Scope forScope = new(scope);
                    CheckBindingNames(forIn.Iterator);
                    DeclareBinding(forIn.Iterator, IterationElement(GetType(forIn.Source)), forScope, false);
                    AnalyzeEmbedded(forIn.Body, forScope);
                    break;
                }
                case CStyleForStatement cStyleFor:
                {
                    Scope cStyleScope = new(scope);
                    foreach (string headerBinding in PscpSyntaxFacts.GetCStyleForHeaderBindings(cStyleFor.HeaderText))
                    {
                        cStyleScope.DeclareValue(headerBinding, new Symbol(SymbolKind.Local, TypeName("int"), true));
                    }

                    AnalyzeEmbedded(cStyleFor.Body, cStyleScope);
                    break;
                }
                case FastForStatement fastFor:
                {
                    AnalyzeExpression(fastFor.Source, scope);
                    Scope fastScope = new(scope);
                    if (fastFor.IndexTarget is not null)
                    {
                        CheckBindingNames(fastFor.IndexTarget);
                        DeclareBinding(fastFor.IndexTarget, TypeName("int"), fastScope, false);
                    }

                    CheckBindingNames(fastFor.ItemTarget);
                    DeclareBinding(fastFor.ItemTarget, IterationElement(GetType(fastFor.Source)), fastScope, false);
                    AnalyzeEmbedded(fastFor.Body, fastScope);
                    break;
                }
                case ReturnStatement returnStatement when returnStatement.Expression is not null:
                    AnalyzeExpression(returnStatement.Expression, scope);
                    break;
                case LocalFunctionStatement localFunction:
                    AnalyzeFunction(localFunction.Function, scope);
                    break;
                case TryStatement tryStatement:
                    AnalyzeBlock(tryStatement.Body, scope);
                    foreach (CatchClause catchClause in tryStatement.Catches)
                    {
                        Scope catchScope = new(scope);
                        if (catchClause.Name is not null)
                        {
                            catchScope.DeclareValue(catchClause.Name, new Symbol(SymbolKind.Local, Normalize(catchClause.Type) ?? TypeName("Exception"), false));
                        }

                        if (catchClause.Filter is not null) AnalyzeExpression(catchClause.Filter, catchScope);
                        AnalyzeBlock(catchClause.Body, catchScope);
                    }

                    if (tryStatement.Finally is not null) AnalyzeBlock(tryStatement.Finally, scope);
                    break;
                case ThrowStatement throwStatement when throwStatement.Expression is not null:
                    AnalyzeExpression(throwStatement.Expression, scope);
                    break;
            }
        }

        // The body of `if`, `while`, `for`, `->`: a single statement gets its own scope like a block.
        private void AnalyzeEmbedded(Statement statement, Scope scope)
        {
            if (statement is BlockStatement)
            {
                AnalyzeStatement(statement, scope);
                return;
            }

            AnalyzeStatement(statement, new Scope(scope));
        }

        // Warnings about expression statements whose value is lost.
        private void CheckExpressionStatement(ExpressionStatement statement)
        {
            Expression expression = statement.Expression;
            switch (expression)
            {
                case UnaryExpression { Operator: UnaryOperator.Plus or UnaryOperator.Negate or UnaryOperator.LogicalNot } unary:
                case UnaryExpression { Operator: UnaryOperator.Peek } peek when !IsPoppable(GetType(peek.Operand)):
                    _discardedValueWarnings.Add((
                        statement,
                        DiagnosticCodes.UnusedUnaryStatement,
                        "This statement computes a value and discards it. A line that starts with `+` or `-` begins a new statement; to continue the previous line, end that line with the operator.",
                        _spans.Get(expression)));
                    break;
                case CallExpression { Callee: MemberAccessExpression member } call
                    when IsIntrinsicCallNode(call)
                        && PscpIntrinsicCatalog.StripGenericSuffix(member.MemberName) is "sort" or "sortBy" or "sortWith" or "distinct" or "reverse" or "copy" or "map" or "filter" or "scan":
                {
                    string receiverText = member.Receiver is IdentifierExpression receiver ? DisplayName(receiver.Name) : "the receiver";
                    string helper = PscpIntrinsicCatalog.StripGenericSuffix(member.MemberName);
                    string suggestion = helper == "sort" && member.Receiver is IdentifierExpression sorted
                        ? GetType(member.Receiver) is NamedTypeSyntax { Name: "List" } ? $" Use `{sorted.Name}.Sort()` to sort in place, or `{sorted.Name} = {sorted.Name}.sort()`." : $" Use `Array.Sort({sorted.Name})` to sort in place, or `{sorted.Name} = {sorted.Name}.sort()`."
                        : " Use the result, for example `let result = ...`.";
                    _discardedValueWarnings.Add((
                        statement,
                        DiagnosticCodes.DiscardedHelperResult,
                        $"`{helper}()` returns a new array and does not change {receiverText}; the result is discarded.{suggestion}",
                        _spans.Get(expression)));
                    break;
                }
            }
        }

        private bool IsIntrinsicCallNode(CallExpression call) => IntrinsicCalls.Contains(call);

        private void AnalyzeDeclaration(DeclarationStatement declaration, Scope scope)
        {
            foreach (BindingTarget target in declaration.Targets)
            {
                CheckBindingNames(target);
            }

            TypeSyntax? explicitType = Normalize(declaration.ExplicitType);
            if (declaration.ExplicitType is SizedArrayTypeSyntax sized)
            {
                foreach (Expression dimension in sized.Dimensions) AnalyzeExpression(dimension, scope);
            }

            if (declaration.IsInputShorthand)
            {
                CheckInputType(declaration);
            }
            else if (declaration.Initializer is null
                && declaration.Mutability == MutabilityKind.Immutable
                && declaration.ExplicitType is not SizedArrayTypeSyntax
                && !IsKnownAutoConstructType(explicitType))
            {
                Error(
                    DiagnosticCodes.UninitializedImmutable,
                    $"`{DescribeTargets(declaration.Targets)}` is immutable but has no value. Give it an initializer, read it from input with `{DisplayType(explicitType)} {DescribeTargets(declaration.Targets)} =`, or declare it `mut`.",
                    _spans.Get(declaration));
            }

            if (declaration.Initializer is not null)
            {
                TypeSyntax? initializerType = AnalyzeExpression(declaration.Initializer, scope, explicitType);
                if (explicitType is not null)
                {
                    WarnIfNullabilityMismatch(declaration.Initializer, explicitType, _spans.Get(declaration.Initializer));
                }
                else if (declaration.Initializer is CollectionExpression { Elements.Count: 0 })
                {
                    Error(
                        DiagnosticCodes.EmptyCollectionWithoutType,
                        "The element type of an empty `[]` is unknown. Declare the type: `int[] xs = []` or `List<int> xs = []`.",
                        _spans.Get(declaration.Initializer));
                }
                else if (initializerType is NamedTypeSyntax { Name: "void" })
                {
                    Error(DiagnosticCodes.SurfaceTypeError, "This expression produces no value, so it cannot initialize a binding.", _spans.Get(declaration.Initializer));
                }
            }

            if (declaration.Initializer is TargetTypedNewArrayExpression targetTypedNewArray)
            {
                CheckTargetTypedNewArray(targetTypedNewArray, explicitType);
            }

            // A binding declared without a value (`mut string? line`) is assigned later.
            bool allowsReassignment = declaration.Mutability == MutabilityKind.Mutable;
            TypeSyntax? inferred = explicitType ?? GetType(declaration.Initializer);
            if (declaration.Targets.Count == 1)
            {
                DeclareBinding(declaration.Targets[0], inferred, scope, allowsReassignment);
                return;
            }

            if (inferred is not TupleTypeSyntax)
            {
                foreach (BindingTarget target in declaration.Targets)
                {
                    DeclareBinding(target, declaration.IsInputShorthand || explicitType is not null ? inferred : null, scope, allowsReassignment);
                }

                return;
            }

            for (int i = 0; i < declaration.Targets.Count; i++)
            {
                DeclareBinding(declaration.Targets[i], TupleElement(inferred, i), scope, allowsReassignment);
            }
        }

        private static string DescribeTargets(IReadOnlyList<BindingTarget> targets)
            => string.Join(", ", targets.Select(target => target switch
            {
                NameTarget name => name.Name,
                DiscardTarget => "_",
                TupleTarget => "(...)",
                _ => "x",
            }));

        private void CheckTargetTypedNewArray(TargetTypedNewArrayExpression newArray, TypeSyntax? targetType)
        {
            if (targetType is not ArrayTypeSyntax arrayType)
            {
                Error(
                    DiagnosticCodes.NewArrayWithoutTarget,
                    $"`{(newArray.AutoConstructElements ? "new!" : "new")}[n]` needs an array target type: `int[] a = new[n]`.",
                    _spans.Get(newArray));
                return;
            }

            ExpressionTypes[newArray] = arrayType;
            if (!newArray.AutoConstructElements)
            {
                return;
            }

            TypeSyntax elementType = arrayType.Depth > 1 ? new ArrayTypeSyntax(arrayType.ElementType, arrayType.Depth - 1) : arrayType.ElementType;
            if (elementType is ArrayTypeSyntax || !HasParameterlessConstructor(elementType))
            {
                Error(
                    DiagnosticCodes.AutoConstructWithoutConstructor,
                    elementType is ArrayTypeSyntax
                        ? "`new![n]` fills each element with `new()`, which an array element cannot use. Use `new[n][m]` for a jagged array."
                        : $"`new![n]` fills each element with `new()`, but `{DisplayType(elementType)}` has no parameterless constructor.",
                    _spans.Get(newArray));
            }
        }

        private bool HasParameterlessConstructor(TypeSyntax type)
        {
            if (type is TupleTypeSyntax)
            {
                return true;
            }

            if (type is not NamedTypeSyntax named)
            {
                return false;
            }

            if (named.Name is "string")
            {
                return false;
            }

            if (!TryResolveType(named.Name, out TypeInfo? typeInfo) || typeInfo is null)
            {
                return true;
            }

            return typeInfo.IsValueType || typeInfo.HasParameterlessConstructor;
        }

        // Spec §17.6: scalars and flat tuples of 2 to 7 scalars.
        private void CheckInputType(DeclarationStatement declaration)
        {
            if (declaration.ExplicitType is null)
            {
                return;
            }

            TypeSyntax elementType = declaration.ExplicitType is SizedArrayTypeSyntax sized ? sized.ElementType : declaration.ExplicitType;
            if (!IsTokenReadable(elementType))
            {
                Error(
                    DiagnosticCodes.NotTokenReadable,
                    $"`{DisplayType(Normalize(declaration.ExplicitType))}` cannot be read from input. Input declarations read `int`, `long`, `double`, `decimal`, `bool`, `char`, `string`, flat tuples of those, and sized arrays of them (`int[n] a =`).",
                    _spans.Get(declaration));
            }
        }

        internal static bool IsTokenReadable(TypeSyntax type)
            => type switch
            {
                NamedTypeSyntax { TypeArguments.Count: 0, Name: "int" or "long" or "double" or "decimal" or "bool" or "char" or "string" } => true,
                TupleTypeSyntax tuple => tuple.Elements.Count is >= 2 and <= 7
                    && tuple.Elements.All(element => element is NamedTypeSyntax { TypeArguments.Count: 0, Name: "int" or "long" or "double" or "decimal" or "bool" or "char" or "string" }),
                _ => false,
            };

        private void AnalyzeAssignmentTarget(Expression expression, Scope scope, bool allowImmutableBindingTarget = false)
        {
            switch (expression)
            {
                case IdentifierExpression identifier:
                {
                    TextSpan span = _spans.Get(identifier);
                    if (!scope.TryResolveValue(identifier.Name, out Symbol? symbol) || symbol is null)
                    {
                        Error(DiagnosticCodes.UndefinedName, $"Undefined name `{identifier.Name}`.", span);
                    }
                    else
                    {
                        ExpressionTypes[identifier] = symbol.Type;
                        if (!allowImmutableBindingTarget)
                        {
                            NoteImmutableMutation(identifier.Name, symbol, span);
                        }
                    }
                    break;
                }
                case DiscardExpression:
                    break;
                case TupleExpression tuple:
                    foreach (Expression element in tuple.Elements) AnalyzeAssignmentTarget(element, scope, allowImmutableBindingTarget);
                    break;
                case MemberAccessExpression member:
                {
                    TypeSyntax? receiverType = AnalyzeExpression(member.Receiver, scope);
                    if (receiverType is NullableTypeSyntax nullable && IsValueTypeLike(nullable.InnerType))
                    {
                        Error(DiagnosticCodes.SurfaceTypeError, "Assigning through a member of a nullable value type is not supported directly. Store it in a non-nullable temporary first.", _spans.Get(member));
                    }

                    ExpressionTypes[member] = AnalyzeMember(member, scope);
                    if (!allowImmutableBindingTarget)
                    {
                        NoteFieldMutation(UnwrapNullable(receiverType), member.MemberName, _spans.GetName(member), member.Receiver is IdentifierExpression { Name: "this" });
                    }

                    break;
                }
                case IndexExpression index:
                    ExpressionTypes[index] = AnalyzeIndex(index, scope);
                    break;
                case TupleProjectionExpression projection:
                    ExpressionTypes[projection] = AnalyzeTupleProjection(projection, scope);
                    break;
                default:
                    AnalyzeExpression(expression, scope);
                    break;
            }
        }

        // Assigning an immutable field outside a constructor (spec §28.2).
        private void NoteFieldMutation(TypeSyntax? receiverType, string memberName, TextSpan span, bool throughThis)
        {
            if (receiverType is not NamedTypeSyntax named
                || !TryResolveType(named.Name, out TypeInfo? typeInfo)
                || typeInfo is null
                || !typeInfo.Members.TryGetValue(memberName, out Symbol? symbol))
            {
                return;
            }

            NoteFieldSymbolMutation(typeInfo.Name, memberName, symbol, span, throughThis);
        }

        private void NoteFieldSymbolMutation(string typeName, string memberName, Symbol symbol, TextSpan span, bool inOwnType)
        {
            if (!symbol.IsImmutableField)
            {
                return;
            }

            if (_inConstructor && inOwnType)
            {
                if (_constructorAssignedFields.Count > 0)
                {
                    _constructorAssignedFields.Peek().Add(memberName);
                }

                return;
            }

            if (MutatedImmutableFields.Add(typeName + "." + memberName))
            {
                Warning(
                    DiagnosticCodes.ImmutableMutation,
                    $"Field `{memberName}` is immutable but is assigned here. Declare it `mut {memberName}` (or with `var`) to make it mutable.",
                    span);
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

        private TypeSyntax? AnalyzeExpression(Expression? expression, Scope scope, TypeSyntax? targetType = null)
        {
            if (expression is null) return null;
            TypeSyntax? type = expression switch
            {
                CollectionExpression collection when targetType is not null && Normalize(targetType) is ArrayTypeSyntax or NamedTypeSyntax { TypeArguments.Count: > 0 }
                    => AnalyzeTargetTypedCollection(collection, scope, Normalize(targetType)!),
                TargetTypedNewArrayExpression targetTypedNewArray when targetType is not null => AnalyzeTargetTypedNewArray(targetTypedNewArray, scope) ?? Normalize(targetType),
                CastExpression cast => AnalyzeCast(cast, scope),
                AsExpression asExpression => AnalyzeAs(asExpression, scope),
                NullForgivingExpression nullForgiving => UnwrapNullable(AnalyzeExpression(nullForgiving.Operand, scope)),
                ThrowExpression throwExpression => AnalyzeThrowExpression(throwExpression, scope),
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
                PostfixExpression postfix => AnalyzePostfixExpression(postfix, scope),
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
            if (scope.TryResolveValue(identifier.Name, out Symbol? symbol) && symbol is not null)
            {
                if (symbol.Kind == SymbolKind.Field && symbol.OwnerType is not null)
                {
                    FieldReferences[identifier] = symbol.OwnerType;
                }

                return symbol.Type;
            }

            if (scope.TryResolveType(identifier.Name, out TypeInfo? typeInfo) && typeInfo is not null) return TypeName(typeInfo.Name);
            if (PscpIntrinsicCatalog.BuiltinTypes.Contains(identifier.Name) || PscpIntrinsicCatalog.IntrinsicCallNames.Contains(identifier.Name)) return TypeName(identifier.Name);
            if (identifier.Name is "default" or "typeof" or "nameof" or "sizeof" or "checked" or "unchecked" || PscpIntrinsicCatalog.IsLikelyExternalTypeLikeRoot(identifier.Name)) return null;
            Error(DiagnosticCodes.UndefinedName, $"Undefined name `{identifier.Name}`.", _spans.Get(identifier));
            return null;
        }

        // Identifiers that name a field of the enclosing type, for the field mutability rule.
        public Dictionary<IdentifierExpression, string> FieldReferences { get; } = new(ReferenceEqualityComparer.Instance);

        private TypeSyntax? AnalyzeCast(CastExpression cast, Scope scope)
        {
            AnalyzeExpression(cast.Operand, scope);
            return Normalize(cast.Type);
        }

        private TypeSyntax? AnalyzeAs(AsExpression expression, Scope scope)
        {
            AnalyzeExpression(expression.Operand, scope);
            return Normalize(expression.Type);
        }

        private TypeSyntax? AnalyzeThrowExpression(ThrowExpression expression, Scope scope)
        {
            AnalyzeExpression(expression.Expression, scope);
            return null;
        }

        // `List<int> xs = [...]`, `int[] empty = []`: the target type decides the collection (spec §16.1).
        private TypeSyntax AnalyzeTargetTypedCollection(CollectionExpression collection, Scope scope, TypeSyntax targetType)
        {
            TypeSyntax? elementHint = targetType switch
            {
                ArrayTypeSyntax { Depth: 1 } array => array.ElementType,
                ArrayTypeSyntax array => new ArrayTypeSyntax(array.ElementType, array.Depth - 1),
                NamedTypeSyntax named when named.TypeArguments.Count > 0 => named.TypeArguments[0],
                _ => null,
            };
            AnalyzeCollectionElements(collection, scope, elementHint);
            return targetType;
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
            bool knownRewrite = IsKnownDataStructureMutationTarget(targetType, assignment.Operator);
            AnalyzeAssignmentTarget(assignment.Target, scope, knownRewrite);
            TypeSyntax? valueType = AnalyzeExpression(assignment.Value, scope, knownRewrite ? null : Normalize(targetType));
            WarnIfNullabilityMismatch(assignment.Value, Normalize(targetType), _spans.Get(assignment.Value));

            if (assignment.Operator == AssignmentOperator.Assign
                && !assignment.IsExplicitValueAssignment
                && !_statementAssignments.Contains(assignment))
            {
                Warning(
                    DiagnosticCodes.AssignmentInExpression,
                    "`=` inside an expression assigns and then uses the value. Write `:=` to make the value-producing assignment explicit, or move the assignment to its own statement.",
                    _spans.Get(assignment));
            }

            if (assignment.Value is TargetTypedNewArrayExpression targetTypedNewArray)
            {
                CheckTargetTypedNewArray(targetTypedNewArray, Normalize(targetType));
            }

            if (knownRewrite && targetType is NamedTypeSyntax named)
            {
                CheckRewriteOperand(assignment, named, valueType);
                if (named.Name is "HashSet" or "System.Collections.Generic.HashSet"
                    or "SortedSet" or "System.Collections.Generic.SortedSet"
                    or "Dictionary" or "System.Collections.Generic.Dictionary")
                {
                    return TypeName("bool");
                }

                return TypeName("void");
            }

            return targetType;
        }

        // Spec §26.2: the operand of a known data-structure `+=`/`-=` must be an element, not a collection.
        private void CheckRewriteOperand(AssignmentExpression assignment, NamedTypeSyntax target, TypeSyntax? valueType)
        {
            if (valueType is null || target.TypeArguments.Count == 0)
            {
                return;
            }

            TypeSyntax element = target.Name is "Dictionary" or "System.Collections.Generic.Dictionary" or "PriorityQueue" or "System.Collections.Generic.PriorityQueue"
                    && assignment.Operator == AssignmentOperator.AddAssign
                    && target.TypeArguments.Count == 2
                ? new TupleTypeSyntax(target.TypeArguments)
                : target.TypeArguments[0];
            if (assignment.Operator == AssignmentOperator.SubtractAssign
                && target.Name is "Dictionary" or "System.Collections.Generic.Dictionary")
            {
                element = target.TypeArguments[0];
            }

            bool elementIsCollection = element is ArrayTypeSyntax || element is NamedTypeSyntax { Name: "List" or "HashSet" or "IEnumerable" };
            if (!elementIsCollection && (valueType is ArrayTypeSyntax || valueType is NamedTypeSyntax { Name: "List" or "HashSet" or "SortedSet" or "IEnumerable" or "LinkedList" or "Queue" or "Stack" }))
            {
                string targetText = assignment.Target is IdentifierExpression id ? id.Name : "xs";
                Error(
                    DiagnosticCodes.RewriteOperandMismatch,
                    $"`{targetText} {(assignment.Operator == AssignmentOperator.AddAssign ? "+=" : "-=")} ...` adds one element of type `{DisplayType(element)}`, but the value is a whole `{DisplayType(valueType)}`. Use `{targetText}.AddRange(...)` or a spread such as `[..{targetText}, ..other]`.",
                    _spans.Get(assignment));
            }
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

        private TypeSyntax? AnalyzePostfixExpression(PostfixExpression postfix, Scope scope)
        {
            TypeSyntax? operandType = AnalyzeExpression(postfix.Operand, scope);
            if (IsPoppable(operandType) || IsKnownDataStructureMutationTarget(operandType, AssignmentOperator.AddAssign))
            {
                string op = postfix.Operator == PostfixOperator.Increment ? "++" : "--";
                Error(
                    DiagnosticCodes.PostfixOnKnownCollection,
                    $"Postfix `{op}` cannot be used on a `{DisplayType(operandType)}`. Use prefix `--q` to pop or dequeue, `~q` to peek.",
                    _spans.Get(postfix));
                return operandType;
            }

            NoteImmutableMutation(postfix.Operand, scope);
            return operandType;
        }

        private void NoteImmutableMutation(Expression target, Scope scope)
        {
            if (target is IdentifierExpression identifier
                && scope.TryResolveValue(identifier.Name, out Symbol? symbol)
                && symbol is not null)
            {
                NoteImmutableMutation(identifier.Name, symbol, _spans.Get(target));
            }
        }

        private void NoteImmutableMutation(string name, Symbol symbol, TextSpan span)
        {
            MutatedNames.Add(name);
            if (symbol.Kind == SymbolKind.Field && symbol.OwnerType is not null)
            {
                NoteFieldSymbolMutation(symbol.OwnerType, name, symbol, span, inOwnType: true);
                return;
            }

            bool binding = symbol.Kind == SymbolKind.Local || (symbol.Kind == SymbolKind.Field && symbol.OwnerType is null);
            if (!binding || symbol.IsMutable)
            {
                return;
            }

            if (ReassignedImmutableNames.Add(name))
            {
                Warning(
                    DiagnosticCodes.ImmutableMutation,
                    $"`{name}` is immutable but is modified here. Declare it with `mut` (or `var`) to make the mutation explicit.",
                    span);
            }
        }

        private TypeSyntax? AnalyzePrefixExpression(PrefixExpression prefix, Scope scope)
        {
            TypeSyntax? operandType = AnalyzeExpression(prefix.Operand, scope);
            bool isKnownPop = prefix.Operator == PostfixOperator.Decrement
                && operandType is NamedTypeSyntax { Name: "Stack" or "System.Collections.Generic.Stack" or "Queue" or "System.Collections.Generic.Queue" or "PriorityQueue" or "System.Collections.Generic.PriorityQueue" };
            if (!isKnownPop)
            {
                NoteImmutableMutation(prefix.Operand, scope);
            }

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
            if (binary.Operator == BinaryOperator.Subtract)
            {
                CheckSubtractionFromFunction(binary, scope);
            }

            return binary.Operator switch
            {
                BinaryOperator.Coalesce => Merge(UnwrapNullable(left), right) ?? UnwrapNullable(left),
                BinaryOperator.Add when IsNamed(left, "string") || IsNamed(right, "string") => TypeName("string"),
                BinaryOperator.Add or BinaryOperator.Subtract or BinaryOperator.Multiply or BinaryOperator.Divide or BinaryOperator.Modulo => Promote(left, right),
                BinaryOperator.ShiftLeft or BinaryOperator.ShiftRight => left,
                BinaryOperator.BitwiseAnd or BinaryOperator.BitwiseXor or BinaryOperator.BitwiseOr => InferBitwiseResult(left, right),
                BinaryOperator.LessThan or BinaryOperator.LessThanOrEqual or BinaryOperator.GreaterThan or BinaryOperator.GreaterThanOrEqual or BinaryOperator.Equal or BinaryOperator.NotEqual or BinaryOperator.LogicalAnd or BinaryOperator.LogicalOr => TypeName("bool"),
                BinaryOperator.Spaceship => TypeName("int"),
                _ => right ?? left,
            };
        }

        // Spec §11.3: `f -1` is `f - 1`, which is meaningless when `f` is a function.
        private void CheckSubtractionFromFunction(BinaryExpression binary, Scope scope)
        {
            string? functionName = binary.Left switch
            {
                IdentifierExpression identifier when IsFunctionName(identifier.Name, scope) => identifier.Name,
                MemberAccessExpression member when GetType(binary.Left) is null && IsIntrinsicMemberName(member.MemberName) => null,
                _ => null,
            };

            if (functionName is not null)
            {
                string rightText = binary.Right is LiteralExpression literal ? literal.RawText : "x";
                Error(
                    DiagnosticCodes.SubtractFromFunction,
                    $"`{functionName} -{rightText}` subtracts {rightText} from the function `{functionName}`. To pass a negative argument write `{functionName} (-{rightText})`.",
                    _spans.Get(binary));
                return;
            }

            if (binary.Left is CallExpression { IsSpaceSeparated: true, Callee: IdentifierExpression callee } application
                && scope.TryResolveValue(callee.Name, out Symbol? symbol)
                && symbol is { Kind: SymbolKind.Function }
                && _functionArity.TryGetValue(callee.Name, out int arity)
                && application.Arguments.Count < arity)
            {
                string rightText = binary.Right is LiteralExpression literal ? literal.RawText : "x";
                Error(
                    DiagnosticCodes.SubtractFromFunction,
                    $"`{callee.Name}` takes {arity} arguments, so `- {rightText}` subtracts from a partial call. To pass a negative argument write `({(binary.Right is LiteralExpression ? "-" + rightText : "-x")})`.",
                    _spans.Get(binary));
            }
        }

        private readonly Dictionary<string, int> _functionArity = new(StringComparer.Ordinal);

        private static bool IsIntrinsicMemberName(string name) => false;

        private bool IsFunctionName(string name, Scope scope)
        {
            if (scope.TryResolveValue(name, out Symbol? symbol) && symbol is not null)
            {
                return symbol.Kind is SymbolKind.Function or SymbolKind.Method
                    || (symbol.Kind == SymbolKind.Intrinsic && PscpIntrinsicCatalog.IntrinsicCallNames.Contains(name));
            }

            return false;
        }

        private TypeSyntax? InferBitwiseResult(TypeSyntax? left, TypeSyntax? right)
            => IsNamed(left, "bool") && IsNamed(right, "bool")
                ? TypeName("bool")
                : Promote(left, right);

        // Spec §14.2: the element type is `char` when both bounds are `char`, `long` when any bound or the step is
        // `long`, and `int` otherwise. A constant step of 0 and mixed `char`/integer bounds are errors.
        private TypeSyntax AnalyzeRange(RangeExpression range, Scope scope)
        {
            TypeSyntax? start = AnalyzeExpression(range.Start, scope);
            TypeSyntax? step = range.Step is null ? null : AnalyzeExpression(range.Step, scope);
            TypeSyntax? end = AnalyzeExpression(range.End, scope);
            if (range.Step is not null && PscpSyntaxFacts.TryEvaluateIntegerConstant(range.Step, out long stepValue) && stepValue == 0)
            {
                Error(DiagnosticCodes.ZeroRangeStep, "The step of this range is 0, so it would never advance. Use a non-zero step.", _spans.Get(range.Step));
            }

            bool startChar = IsNamed(start, "char");
            bool endChar = IsNamed(end, "char");
            if (startChar != endChar && start is not null && end is not null)
            {
                Error(
                    DiagnosticCodes.MixedCharRange,
                    "A range cannot mix a `char` bound with an integer bound. Use two `char` bounds (`'a'..'z'`) or convert with `int c` / `char n`.",
                    _spans.Get(range));
            }

            if (startChar && endChar)
            {
                return new NamedTypeSyntax("IEnumerable", Immutable.List(TypeName("char")));
            }

            bool isLong = IsNamed(start, "long") || IsNamed(end, "long") || IsNamed(step, "long");
            return new NamedTypeSyntax("IEnumerable", Immutable.List(isLong ? TypeName("long") : TypeName("int")));
        }

        // Pattern variables are in scope for the rest of the enclosing statement list, as in C#.
        private TypeSyntax AnalyzeIsPattern(IsPatternExpression expression, Scope scope)
        {
            TypeSyntax? operandType = AnalyzeExpression(expression.Left, scope);
            DeclarePatternDesignations(expression.Pattern, operandType, scope);
            return TypeName("bool");
        }

        private static void DeclarePatternDesignations(PatternSyntax pattern, TypeSyntax? operandType, Scope scope)
        {
            foreach (PatternDesignation designation in pattern.Designations)
            {
                TypeSyntax? type = Normalize(designation.Type)
                    ?? (pattern.Designations.Count == 1 && pattern.Text.StartsWith("var ", StringComparison.Ordinal) ? UnwrapNullable(operandType) : null);
                scope.DeclareValue(designation.Name, new Symbol(SymbolKind.Local, type, false));
            }
        }

        private TypeSyntax? AnalyzeCall(CallExpression call, Scope scope)
        {
            foreach (ArgumentSyntax argument in call.Arguments)
            {
                if (argument is ExpressionArgumentSyntax { Modifier: ArgumentModifier.Ref or ArgumentModifier.Out } byReference)
                {
                    NoteImmutableMutation(byReference.Expression, scope);
                    if (byReference.Expression is MemberAccessExpression referencedMember)
                    {
                        AnalyzeExpression(referencedMember.Receiver, scope);
                        NoteFieldMutation(UnwrapNullable(GetType(referencedMember.Receiver)), referencedMember.MemberName, _spans.GetName(referencedMember), referencedMember.Receiver is IdentifierExpression { Name: "this" });
                    }
                }
            }

            if (call.Pipe == PipeKind.Delegate && call.Callee is LambdaExpression pipedLambda && call.Arguments.Count == 1 && call.Arguments[0] is ExpressionArgumentSyntax pipedValue)
            {
                TypeSyntax? valueType = AnalyzeExpression(pipedValue.Expression, scope);
                return AnalyzeLambdaWithParameterTypes(pipedLambda, scope, [valueType]);
            }

            TypeSyntax? calleeType = AnalyzeExpression(call.Callee, scope);
            if (call.Callee is IdentifierExpression conversionIdentifier && IsConversionKeyword(conversionIdentifier.Name)
                && !(scope.TryResolveValue(conversionIdentifier.Name, out Symbol? shadow) && shadow is not null))
            {
                AnalyzeArgumentsNormally(call.Arguments, scope);
                CheckConversion(call, conversionIdentifier.Name);
                return TypeName(conversionIdentifier.Name);
            }

            if (TryAnalyzeIntrinsicCall(call, scope, out TypeSyntax? intrinsicType))
            {
                IntrinsicCalls.Add(call);
                return intrinsicType;
            }

            if (call.Callee is MemberAccessExpression stdinMember
                && stdinMember.Receiver is IdentifierExpression { Name: PscpBinder.StdinName })
            {
                AnalyzeArgumentsNormally(call.Arguments, scope);
                TypeSyntax? stdinType = AnalyzeKnownStdinCall(stdinMember, call.Arguments);
                if (stdinType is not null)
                {
                    return stdinType;
                }
            }
            else
            {
                AnalyzeArgumentsNormally(call.Arguments, scope);
            }

            CheckCallable(call, scope, calleeType);

            if (call.Callee is MemberAccessExpression member)
            {
                string memberName = PscpIntrinsicCatalog.StripGenericSuffix(member.MemberName);
                TypeSyntax? receiverType = UnwrapNullable(GetType(member.Receiver));
                if (receiverType is ArrayTypeSyntax && memberName == "Add")
                {
                    Error(DiagnosticCodes.SurfaceTypeError, "Arrays do not have an `Add` method. Use indexing, or a growable collection such as `List<T>`.", _spans.GetName(member));
                }

                if (receiverType is NamedTypeSyntax namedReceiver
                    && namedReceiver.Name is "PriorityQueue" or "System.Collections.Generic.PriorityQueue"
                    && memberName is "TryPeek" or "TryDequeue"
                    && (call.Arguments.Count != 2 || call.Arguments.Any(argument => !IsOutLike(argument))))
                {
                    Error(DiagnosticCodes.SurfaceTypeError, $"`{namedReceiver.Name}.{memberName}` requires two `out` arguments: item and priority.", _spans.Get(call));
                }

                CheckCultureOrderedSort(call, member, memberName, receiverType);
                if (receiverType is NamedTypeSyntax { Name: "Dictionary" or "System.Collections.Generic.Dictionary" } dictionary
                    && dictionary.TypeArguments.Count == 2)
                {
                    TypeSyntax? dictionaryResult = memberName switch
                    {
                        "ContainsKey" or "ContainsValue" or "Remove" or "TryAdd" or "TryGetValue" => TypeName("bool"),
                        "GetValueOrDefault" => dictionary.TypeArguments[1],
                        _ => null,
                    };
                    if (dictionaryResult is not null)
                    {
                        return dictionaryResult;
                    }
                }

                if (receiverType is NamedTypeSyntax { TypeArguments.Count: 1 } generic
                    && memberName is "Contains" or "Add" or "Remove"
                    && generic.Name is "HashSet" or "SortedSet" or "List")
                {
                    return memberName == "Add" && generic.Name == "List" ? TypeName("void") : TypeName("bool");
                }
            }

            return calleeType;
        }

        // Spec §11.2, §13.3: the head of a space-call and the target of a pipe must be callable.
        private void CheckCallable(CallExpression call, Scope scope, TypeSyntax? calleeType)
        {
            if (!call.IsSpaceSeparated && call.Pipe == PipeKind.None)
            {
                return;
            }

            bool notCallable = call.Callee switch
            {
                LiteralExpression or InterpolatedStringExpression or CollectionExpression or TupleExpression => true,
                IdentifierExpression identifier when scope.TryResolveValue(identifier.Name, out Symbol? symbol) && symbol is { Kind: SymbolKind.Local or SymbolKind.Field }
                    => IsDefinitelyNotDelegate(calleeType),
                _ => false,
            };

            if (!notCallable)
            {
                return;
            }

            string calleeText = call.Callee is IdentifierExpression named ? $"`{named.Name}`" : "this value";
            Error(
                DiagnosticCodes.NotCallable,
                call.Pipe != PipeKind.None
                    ? $"The pipe target {calleeText} is not a function. The right side of `|>` must be a function, a method, or a call such as `f(y)`."
                    : $"{calleeText} is not a function, so it cannot take arguments. Separate the values with an operator or a comma.",
                _spans.Get(call.Callee));
        }

        private static bool IsDefinitelyNotDelegate(TypeSyntax? type)
            => type switch
            {
                ArrayTypeSyntax or TupleTypeSyntax => true,
                NamedTypeSyntax { Name: "int" or "long" or "double" or "decimal" or "bool" or "char" or "string" } => true,
                NamedTypeSyntax { Name: "List" or "LinkedList" or "Queue" or "Stack" or "HashSet" or "SortedSet" or "Dictionary" or "PriorityQueue" or "IEnumerable" } => true,
                _ => false,
            };

        // Spec §25.4: .NET sorts of strings follow the current culture.
        private void CheckCultureOrderedSort(CallExpression call, MemberAccessExpression member, string memberName, TypeSyntax? receiverType)
        {
            bool stringListSort = memberName == "Sort"
                && call.Arguments.Count == 0
                && receiverType is NamedTypeSyntax { Name: "List", TypeArguments: [var element] }
                && ContainsString(element);
            bool stringArraySort = memberName == "Sort"
                && member.Receiver is IdentifierExpression { Name: "Array" }
                && call.Arguments.Count == 1
                && call.Arguments[0] is ExpressionArgumentSyntax sortedArgument
                && GetType(sortedArgument.Expression) is ArrayTypeSyntax { Depth: 1 } sortedArray
                && ContainsString(sortedArray.ElementType);
            if (stringListSort || stringArraySort)
            {
                Warning(
                    DiagnosticCodes.CultureOrderedStrings,
                    "This .NET sort orders strings by the current culture (`a A b B`), not ordinally. Pass `string.asc` (or `StringComparer.Ordinal`), or use `.sort()`.",
                    _spans.Get(call));
            }
        }

        private static bool ContainsString(TypeSyntax? type)
            => type switch
            {
                NamedTypeSyntax { Name: "string" } => true,
                TupleTypeSyntax tuple => tuple.Elements.Any(ContainsString),
                _ => false,
            };

        // Spec §21: `bool "text"` parses; the conversion of a non-renderable value is an error.
        private void CheckConversion(CallExpression call, string keyword)
        {
            if (call.Arguments.Count != 1)
            {
                Error(DiagnosticCodes.SurfaceTypeError, $"`{keyword}` converts exactly one value.", _spans.Get(call));
                return;
            }

            if (keyword == "string" && call.Arguments[0] is ExpressionArgumentSyntax argument)
            {
                CheckRenderable(GetType(argument.Expression), argument.Expression);
            }
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
                        CheckBindingNames(outDeclaration.Target);
                        DeclareBinding(outDeclaration.Target, outDeclaration.Type is NamedTypeSyntax { Name: "var" } ? null : Normalize(outDeclaration.Type), scope, true);
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
                if ((PscpIntrinsicCatalog.IntrinsicCallNames.Contains(memberName) || PscpIntrinsicCatalog.CollectionHelperNames.Contains(memberName))
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
            AnalyzeIntrinsicArguments(intrinsicName, receiver, call.Arguments, scope);
            intrinsicType = IntrinsicType(intrinsicName, call.Arguments, receiver is null ? null : GetType(receiver));
            CheckIntrinsicCall(call, intrinsicName, receiver);
            return true;
        }

        // Types the lambda arguments of aggregates and helpers from the source's element type (spec §22, §24).
        private void AnalyzeIntrinsicArguments(string name, Expression? receiver, IReadOnlyList<ArgumentSyntax> arguments, Scope scope)
        {
            Expression? source = receiver;
            int first = 0;
            if (source is null
                && name is not ("min" or "max" or "chmin" or "chmax" or "abs" or "sqrt" or "clamp" or "gcd" or "lcm" or "floor" or "ceil" or "round" or "pow" or "popcount" or "bitLength")
                && arguments.Count > 0
                && arguments[0] is ExpressionArgumentSyntax { Expression: not LambdaExpression } sourceArgument)
            {
                source = sourceArgument.Expression;
                first = 1;
                AnalyzeExpression(source, scope);
            }

            TypeSyntax? element = source is null ? null : IterationElement(GetType(source));
            TypeSyntax? seed = null;
            for (int i = first; i < arguments.Count; i++)
            {
                if (arguments[i] is ExpressionArgumentSyntax { Expression: LambdaExpression lambda })
                {
                    IReadOnlyList<TypeSyntax?> parameterTypes = name switch
                    {
                        "sortWith" => [element, element],
                        "fold" or "scan" or "mapFold" => [seed, element],
                        _ => [element],
                    };
                    _lambdaResultTypes[lambda] = AnalyzeLambdaWithParameterTypes(lambda, scope, parameterTypes);
                    continue;
                }

                AnalyzeArgumentsNormally([arguments[i]], scope);
                if (i == first && name is "fold" or "scan" or "mapFold")
                {
                    seed = ArgType(arguments, i);
                }
            }
        }

        private readonly Dictionary<LambdaExpression, TypeSyntax?> _lambdaResultTypes = new(ReferenceEqualityComparer.Instance);

        private TypeSyntax? LambdaResultType(IReadOnlyList<ArgumentSyntax> arguments)
        {
            foreach (ArgumentSyntax argument in arguments)
            {
                if (argument is ExpressionArgumentSyntax { Expression: LambdaExpression lambda } && _lambdaResultTypes.TryGetValue(lambda, out TypeSyntax? type))
                {
                    return type;
                }
            }

            return null;
        }

        private void CheckIntrinsicCall(CallExpression call, string name, Expression? receiver)
        {
            if (name is "min" or "max" && receiver is null && call.Arguments.Count == 1)
            {
                TypeSyntax? argumentType = ArgType(call.Arguments, 0);
                if (argumentType is NamedTypeSyntax { Name: "int" or "long" or "double" or "decimal" or "char" or "bool" })
                {
                    Error(
                        DiagnosticCodes.ScalarMinMax,
                        $"`{name}` of a single `{DisplayType(argumentType)}` has nothing to compare. Give two or more values (`{name} a b`) or a sequence (`{name} xs`).",
                        _spans.Get(call));
                }
            }

            if (name == "groupCount")
            {
                Warning(DiagnosticCodes.Deprecated, "`groupCount()` is deprecated and will be removed in v0.8. Use `freq()`.", _spans.Get(call.Callee));
            }

            if (name is "sort" or "min" or "max" or "minBy" or "maxBy" or "lowerBound" or "upperBound")
            {
                TypeSyntax? ordered = name is "minBy" or "maxBy"
                    ? LambdaResultType(call.Arguments)
                    : receiver is not null ? IterationElement(GetType(receiver)) : call.Arguments.Count == 1 ? IterationElement(ArgType(call.Arguments, 0)) : ArgType(call.Arguments, 0);
                if (ordered is NamedTypeSyntax { TypeArguments.Count: 0 } orderedType
                    && TryResolveType(orderedType.Name, out TypeInfo? typeInfo)
                    && typeInfo is not null
                    && !typeInfo.HasOrdering)
                {
                    Error(
                        DiagnosticCodes.NoDefaultOrder,
                        $"`{typeInfo.Name}` has no default order. Define `operator<=>` in the type, or use `sortBy` / `minBy` with a key.",
                        _spans.Get(call));
                }
            }
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
            if (UnwrapNullable(receiverType) is NamedTypeSyntax named
                && TryResolveType(named.Name, out TypeInfo? userType)
                && userType is not null
                && userType.Members.ContainsKey(memberName))
            {
                // A real member wins over the intrinsic alias (spec §5.2).
                return false;
            }

            if (memberName is "sum" or "sumBy" or "min" or "max" or "minBy" or "maxBy"
                || PscpIntrinsicCatalog.CollectionHelperNames.Contains(memberName))
            {
                return IterationElement(receiverType) is not null
                    || receiverType is null && memberAccess.Receiver is not IdentifierExpression { Name: PscpBinder.StdinName or PscpBinder.StdoutName };
            }

            return false;
        }

        private TypeSyntax? AnalyzeLambdaWithParameterTypes(LambdaExpression lambda, Scope scope, IReadOnlyList<TypeSyntax?> parameterTypes)
        {
            Scope lambdaScope = new(scope);
            for (int i = 0; i < lambda.Parameters.Count; i++)
            {
                TypeSyntax? parameterType = Normalize(lambda.Parameters[i].Type) ?? (i < parameterTypes.Count ? parameterTypes[i] : null);
                DeclareBinding(lambda.Parameters[i].Target, parameterType, lambdaScope, lambda.Parameters[i].Modifier is ArgumentModifier.Ref or ArgumentModifier.Out);
            }

            return AnalyzeLambdaBody(lambda.Body, lambdaScope);
        }

        private TypeSyntax? AnalyzeLambdaBody(LambdaBody body, Scope lambdaScope)
        {
            switch (body)
            {
                case LambdaExpressionBody expressionBody:
                    if (expressionBody.Expression is AssignmentExpression bodyAssignment)
                    {
                        _statementAssignments.Add(bodyAssignment);
                    }

                    return AnalyzeExpression(expressionBody.Expression, lambdaScope);
                case LambdaBlockBody blockBody:
                    return AnalyzeBlockLike(blockBody.Block, lambdaScope);
                default:
                    return null;
            }
        }

        // Deprecated `stdin` names (spec §17.8) and their replacements.
        internal static readonly IReadOnlyDictionary<string, string> DeprecatedStdinMembers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["int"] = "readInt()", ["long"] = "readLong()", ["double"] = "readDouble()", ["decimal"] = "readDecimal()",
            ["bool"] = "readBool()", ["char"] = "readChar()", ["str"] = "readString()", ["line"] = "readLine()",
            ["lines"] = "readLines(n)", ["words"] = "readWords()", ["chars"] = "readChars()",
            ["array"] = "readArray<T>(n)", ["list"] = "readList<T>(n)", ["linkedList"] = "readLinkedList<T>(n)",
            ["readTuple2"] = "readTuple<A, B>()", ["readTuple3"] = "readTuple<A, B, C>()", ["tuple2"] = "readTuple<A, B>()", ["tuple3"] = "readTuple<A, B, C>()",
            ["readTuples2"] = "readArray<(A, B)>(n)", ["readTuples3"] = "readArray<(A, B, C)>(n)", ["tuples2"] = "readArray<(A, B)>(n)", ["tuples3"] = "readArray<(A, B, C)>(n)",
            ["readGridInt"] = "readGrid<int>(n, m)", ["readGridLong"] = "readGrid<long>(n, m)", ["gridInt"] = "readGrid<int>(n, m)", ["gridLong"] = "readGrid<long>(n, m)",
            ["readNestedArray"] = "readGrid<T>(n, m)", ["nestedArray"] = "readGrid<T>(n, m)",
            ["charGrid"] = "readCharGrid(n)", ["wordGrid"] = "readWordGrid(n)",
        };

        private TypeSyntax? AnalyzeKnownStdinCall(MemberAccessExpression member, IReadOnlyList<ArgumentSyntax> arguments)
        {
            string memberName = member.MemberName;
            string root = PscpIntrinsicCatalog.StripGenericSuffix(memberName);
            if (DeprecatedStdinMembers.TryGetValue(root, out string? replacement))
            {
                Warning(
                    DiagnosticCodes.Deprecated,
                    $"`stdin.{root}` is deprecated and will be removed in v0.8. Use `stdin.{replacement}`.",
                    _spans.GetName(member));
            }

            IReadOnlyList<TypeSyntax>? typeArguments = TryParseGenericTypeArguments(memberName, out IReadOnlyList<TypeSyntax>? parsed) ? parsed : null;
            if (root is "readArray" or "array" or "readList" or "list" or "readLinkedList" or "linkedList" or "readGrid" or "readNestedArray" or "nestedArray"
                && typeArguments is { Count: 1 }
                && !IsTokenReadable(typeArguments[0]))
            {
                Error(
                    DiagnosticCodes.NotTokenReadable,
                    $"`{DisplayType(typeArguments[0])}` cannot be read from input. Use `int`, `long`, `double`, `decimal`, `bool`, `char`, `string`, or a flat tuple of them.",
                    _spans.GetName(member));
            }

            if (root is "readTuple" && (typeArguments is null || typeArguments.Count is < 2 or > 7 || !typeArguments.All(IsTokenReadable)))
            {
                Error(
                    DiagnosticCodes.NotTokenReadable,
                    "`readTuple<...>()` reads 2 to 7 values of `int`, `long`, `double`, `decimal`, `bool`, `char` or `string`.",
                    _spans.GetName(member));
            }

            return root switch
            {
                "int" or "readInt" => TypeName("int"),
                "long" or "readLong" => TypeName("long"),
                "double" or "readDouble" => TypeName("double"),
                "decimal" or "readDecimal" => TypeName("decimal"),
                "bool" or "readBool" => TypeName("bool"),
                "char" or "readChar" => TypeName("char"),
                "str" or "readString" or "line" or "readLine" or "readRestOfLine" => TypeName("string"),
                "hasNext" or "hasNextLine" => TypeName("bool"),
                "lines" or "readLines" => new ArrayTypeSyntax(TypeName("string"), 1),
                "words" or "readWords" => new ArrayTypeSyntax(TypeName("string"), 1),
                "chars" or "readChars" => new ArrayTypeSyntax(TypeName("char"), 1),
                "array" or "readArray" when typeArguments is { Count: 1 } => new ArrayTypeSyntax(typeArguments[0], 1),
                "list" or "readList" when typeArguments is { Count: 1 } => new NamedTypeSyntax("List", Immutable.List(typeArguments[0])),
                "linkedList" or "readLinkedList" when typeArguments is { Count: 1 } => new NamedTypeSyntax("LinkedList", Immutable.List(typeArguments[0])),
                "readTuple" or "tuple2" or "readTuple2" or "tuple3" or "readTuple3" when typeArguments is { Count: >= 2 } => new TupleTypeSyntax(typeArguments),
                "tuples2" or "readTuples2" or "tuples3" or "readTuples3" when typeArguments is { Count: >= 2 } => new ArrayTypeSyntax(new TupleTypeSyntax(typeArguments), 1),
                "readGrid" or "nestedArray" or "readNestedArray" when typeArguments is { Count: 1 } => new ArrayTypeSyntax(typeArguments[0], 2),
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
            TextSpan span = _spans.GetName(member);

            if (memberName is "asc" or "desc")
            {
                if (member.Receiver is TupleExpression tupleTypeExpression && TryGetTupleTypeFromExpression(tupleTypeExpression, scope, out TypeSyntax? tupleType))
                {
                    return new NamedTypeSyntax("IComparer", Immutable.List(tupleType!));
                }

                if (!TryGetTypeLikeReceiverName(member.Receiver, scope, out string? typeLikeName))
                {
                    Error(DiagnosticCodes.SurfaceTypeError, $"`{memberName}` comparator sugar requires a type receiver such as `int.{memberName}` or `MyType.{memberName}`.", span);
                    return new NamedTypeSyntax("IComparer", Immutable.List(TypeName("object")));
                }

                return new NamedTypeSyntax("IComparer", Immutable.List(TypeName(typeLikeName!)));
            }

            if (member.Receiver is IdentifierExpression intrinsicReceiver
                && scope.TryResolveValue(intrinsicReceiver.Name, out Symbol? intrinsicSymbol)
                && intrinsicSymbol is { Kind: SymbolKind.Intrinsic }
                && PscpIntrinsicCatalog.TryGetKnownMembers(DisplayName(intrinsicReceiver.Name), out IReadOnlySet<string>? members)
                && members is not null)
            {
                string receiverDisplay = DisplayName(intrinsicReceiver.Name);
                ExpressionTypes[member.Receiver] = intrinsicSymbol.Type;
                if (!members.Contains(memberName))
                {
                    if (PscpIntrinsicCatalog.StrictIntrinsicReceivers.Contains(receiverDisplay))
                    {
                        Error(DiagnosticCodes.UnknownIoMember, $"`{receiverDisplay}` has no member `{memberName}`.", span);
                    }

                    return null;
                }

                if (receiverDisplay == "Array" && memberName == "zero")
                {
                    Warning(DiagnosticCodes.Deprecated, "`Array.zero(n)` is deprecated and will be removed in v0.8. Use `new[n]`.", span);
                }

                return receiverDisplay switch
                {
                    "stdout" => TypeName("void"),
                    "stdin" when memberName is "int" or "readInt" => TypeName("int"),
                    "stdin" when memberName is "long" or "readLong" => TypeName("long"),
                    "stdin" when memberName is "double" or "readDouble" => TypeName("double"),
                    "stdin" when memberName is "decimal" or "readDecimal" => TypeName("decimal"),
                    "stdin" when memberName is "bool" or "readBool" or "hasNext" or "hasNextLine" => TypeName("bool"),
                    "stdin" when memberName is "char" or "readChar" => TypeName("char"),
                    "stdin" when memberName is "str" or "readString" or "line" or "readLine" or "readRestOfLine" => TypeName("string"),
                    _ => null,
                };
            }

            bool hasTypeLikeReceiver = TryGetTypeLikeReceiverName(member.Receiver, scope, out string? typeLikeReceiverName);
            TypeSyntax? receiverType = hasTypeLikeReceiver ? TypeName(typeLikeReceiverName!) : AnalyzeExpression(member.Receiver, scope);
            TypeSyntax? effectiveReceiverType = UnwrapNullable(receiverType);
            if (receiverType is NullableTypeSyntax && !member.IsNullConditional)
            {
                Warning(DiagnosticCodes.GenericWarning, $"Possible null dereference on nullable receiver when accessing `{memberName}`.", span);
            }

            string receiverName = hasTypeLikeReceiver
                ? typeLikeReceiverName!
                : member.Receiver is IdentifierExpression id ? id.Name : (effectiveReceiverType as NamedTypeSyntax)?.Name ?? string.Empty;

            if (hasTypeLikeReceiver)
            {
                if (TryResolveType(receiverName, out TypeInfo? receiverTypeInfo) && receiverTypeInfo is not null)
                {
                    if (receiverTypeInfo.NestedTypes.TryGetValue(memberName, out TypeInfo? nestedType))
                    {
                        return TypeName(nestedType.Name);
                    }

                    return receiverTypeInfo.Members.TryGetValue(memberName, out Symbol? staticSymbol) ? staticSymbol.Type : null;
                }

                TryInferKnownExternalMemberType(receiverName, effectiveReceiverType, hasTypeLikeReceiver, memberName, out TypeSyntax? staticExternal);
                return staticExternal;
            }

            if (effectiveReceiverType is NamedTypeSyntax receiverNamed && TryResolveType(receiverNamed.Name, out TypeInfo? typeInfo) && typeInfo is not null)
            {
                if (typeInfo.Members.TryGetValue(memberName, out Symbol? symbol))
                {
                    return symbol.Type;
                }

                if (memberName is "ToString" or "Equals" or "GetHashCode" or "GetType" or "CompareTo" or "Deconstruct")
                {
                    return memberName switch
                    {
                        "ToString" => TypeName("string"),
                        "Equals" => TypeName("bool"),
                        "GetHashCode" or "CompareTo" => TypeName("int"),
                        _ => null,
                    };
                }

                return null;
            }

            if (effectiveReceiverType is ArrayTypeSyntax && memberName == "Length")
            {
                return TypeName("int");
            }

            if (effectiveReceiverType is NamedTypeSyntax named
                && memberName == "Count"
                && named.Name is "List" or "LinkedList" or "Queue" or "Stack" or "HashSet" or "Dictionary" or "PriorityQueue" or "SortedSet" or "SortedDictionary")
            {
                return TypeName("int");
            }

            if (effectiveReceiverType is NamedTypeSyntax { TypeArguments.Count: 2 } dictionary
                && dictionary.Name is "Dictionary" or "SortedDictionary"
                && memberName is "Keys" or "Values")
            {
                return new NamedTypeSyntax("IEnumerable", Immutable.List(memberName == "Keys" ? dictionary.TypeArguments[0] : dictionary.TypeArguments[1]));
            }

            if (effectiveReceiverType is NamedTypeSyntax { TypeArguments.Count: 1 } sortedSet
                && sortedSet.Name is "SortedSet"
                && memberName is "Min" or "Max")
            {
                return sortedSet.TypeArguments[0];
            }

            if (TryInferKnownExternalMemberType(receiverName, effectiveReceiverType, hasTypeLikeReceiver, memberName, out TypeSyntax? knownExternalType))
            {
                return knownExternalType;
            }

            return null;
        }

        // `(int, string).asc`: a tuple of type names written as a tuple expression.
        private bool TryGetTupleTypeFromExpression(TupleExpression tuple, Scope scope, out TypeSyntax? type)
        {
            List<TypeSyntax> elements = [];
            foreach (Expression element in tuple.Elements)
            {
                if (element is TupleExpression nested && TryGetTupleTypeFromExpression(nested, scope, out TypeSyntax? nestedType))
                {
                    elements.Add(nestedType!);
                    continue;
                }

                if (!TryGetTypeLikeReceiverName(element, scope, out string? name))
                {
                    type = null;
                    return false;
                }

                elements.Add(TypeName(name!));
            }

            type = new TupleTypeSyntax(elements);
            return true;
        }

        private TypeSyntax? AnalyzeIndex(IndexExpression index, Scope scope)
        {
            TypeSyntax? receiverType = AnalyzeExpression(index.Receiver, scope);
            if (receiverType is NullableTypeSyntax && !index.IsNullConditional)
            {
                Warning(DiagnosticCodes.GenericWarning, "Possible null dereference on nullable receiver when indexing.", _spans.Get(index));
            }

            foreach (Expression argument in index.Arguments) AnalyzeExpression(argument, scope);
            TypeSyntax? effective = UnwrapNullable(receiverType);
            if (index.Arguments.Any(argument => argument is SliceExpression))
            {
                // Spec §15.3: arrays, strings and `List<T>` can be sliced.
                bool supported = effective is ArrayTypeSyntax
                    || effective is NamedTypeSyntax { Name: "string" or "List" or "System.Collections.Generic.List" };
                if (!supported && effective is not null)
                {
                    Error(
                        DiagnosticCodes.UnsupportedSliceTarget,
                        $"A `{DisplayType(effective)}` cannot be sliced. Slices work on arrays, `string` and `List<T>`.",
                        _spans.Get(index));
                }

                return effective;
            }

            return effective switch
            {
                ArrayTypeSyntax { Depth: 1 } array => array.ElementType,
                ArrayTypeSyntax array => new ArrayTypeSyntax(array.ElementType, array.Depth - 1),
                NamedTypeSyntax { Name: "string" } => TypeName("char"),
                NamedTypeSyntax named when named.TypeArguments.Count == 2
                    && named.Name is "Dictionary" or "System.Collections.Generic.Dictionary" or "SortedDictionary" => named.TypeArguments[1],
                NamedTypeSyntax named when named.TypeArguments.Count == 1 => named.TypeArguments[0],
                _ => null,
            };
        }

        private TypeSyntax? AnalyzeWithExpression(WithExpression withExpression, Scope scope)
        {
            TypeSyntax? receiverType = AnalyzeExpression(withExpression.Receiver, scope);
            foreach (WithAssignment assignment in withExpression.Assignments)
            {
                AnalyzeExpression(assignment.Value, scope);
            }

            return receiverType;
        }

        private TypeSyntax? AnalyzeSwitchExpression(SwitchExpression switchExpression, Scope scope)
        {
            AnalyzeExpression(switchExpression.Receiver, scope);
            TypeSyntax? resultType = null;
            TypeSyntax? receiverType = GetType(switchExpression.Receiver);
            foreach (SwitchArm arm in switchExpression.Arms)
            {
                Scope armScope = new(scope);
                DeclarePatternDesignations(arm.Pattern, receiverType, armScope);

                if (arm.Guard is not null)
                {
                    AnalyzeExpression(arm.Guard, armScope);
                }

                resultType = Merge(resultType, AnalyzeExpression(arm.Result, armScope));
            }

            return resultType;
        }

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
            TypeSyntax? receiverType = UnwrapNullable(AnalyzeExpression(projection.Receiver, scope));
            TypeSyntax? elementType = TupleElement(receiverType, projection.Position - 1);
            if (elementType is null && receiverType is not null)
            {
                Error(
                    DiagnosticCodes.SurfaceTypeError,
                    receiverType is TupleTypeSyntax tuple
                        ? $"`.{projection.Position}` is out of range: the tuple has {tuple.Elements.Count} elements."
                        : $"`.{projection.Position}` needs a tuple, but the value is a `{DisplayType(receiverType)}`.",
                    _spans.Get(projection));
            }

            return elementType;
        }

        private TypeSyntax? AnalyzeLambda(LambdaExpression lambda, Scope scope)
        {
            Scope lambdaScope = new(scope);
            foreach (LambdaParameter parameter in lambda.Parameters)
            {
                CheckBindingNames(parameter.Target);
                DeclareBinding(parameter.Target, Normalize(parameter.Type), lambdaScope, parameter.Modifier is ArgumentModifier.Ref or ArgumentModifier.Out);
            }

            return AnalyzeLambdaBody(lambda.Body, lambdaScope);
        }

        private TypeSyntax? AnalyzeNew(NewExpression creation, Scope scope)
        {
            foreach (ArgumentSyntax argument in creation.Arguments)
            {
                AnalyzeArgumentsNormally([argument], scope);
            }

            if (creation.Initializer is not null)
            {
                foreach (WithAssignment assignment in creation.Initializer) AnalyzeExpression(assignment.Value, scope);
            }

            if (creation.Type is not null)
            {
                CheckCultureOrderedConstruction(creation);
            }

            return Normalize(creation.Type);
        }

        // Spec §25.4: `new SortedSet<string>()` without a comparer orders strings by the current culture.
        private void CheckCultureOrderedConstruction(NewExpression creation)
        {
            if (creation.Arguments.Count != 0 || creation.Type is not NamedTypeSyntax named)
            {
                return;
            }

            TypeSyntax? key = named.Name switch
            {
                "SortedSet" or "SortedDictionary" or "SortedList" when named.TypeArguments.Count >= 1 => named.TypeArguments[0],
                "PriorityQueue" when named.TypeArguments.Count == 2 => named.TypeArguments[1],
                _ => null,
            };
            if (ContainsString(key))
            {
                Warning(
                    DiagnosticCodes.CultureOrderedStrings,
                    $"`new {DisplayType(named)}()` orders strings by the current culture (`a A b B`). Pass a comparer: `new(string.asc)` or `new(StringComparer.Ordinal)`.",
                    _spans.Get(creation));
            }
        }

        private TypeSyntax AnalyzeNewArray(NewArrayExpression newArray, Scope scope)
        {
            foreach (Expression dimension in newArray.Dimensions) AnalyzeExpression(dimension, scope);
            return new ArrayTypeSyntax(newArray.ElementType, newArray.Dimensions.Count);
        }

        private TypeSyntax? AnalyzeTargetTypedNewArray(TargetTypedNewArrayExpression targetTypedNewArray, Scope scope)
        {
            foreach (Expression dimension in targetTypedNewArray.Dimensions) AnalyzeExpression(dimension, scope);
            return GetType(targetTypedNewArray);
        }

        private TypeSyntax? AnalyzeCollection(CollectionExpression collection, Scope scope)
        {
            TypeSyntax? elementType = AnalyzeCollectionElements(collection, scope, null);
            return collection.Elements.Count == 0 ? null : new ArrayTypeSyntax(elementType ?? TypeName("object"), 1);
        }

        // Spec §16.1: the element type is the common type of the elements (numeric promotion allowed).
        private TypeSyntax? AnalyzeCollectionElements(CollectionExpression collection, Scope scope, TypeSyntax? elementHint)
        {
            TypeSyntax? elementType = null;
            bool reported = false;
            foreach (CollectionElement element in collection.Elements)
            {
                TypeSyntax? current = element switch
                {
                    ExpressionElement expressionElement => AnalyzeExpression(expressionElement.Expression, scope, elementHint),
                    RangeElement rangeElement => EnumerableElement(AnalyzeRange(rangeElement.Range, scope)),
                    SpreadElement spreadElement => IterationElement(AnalyzeExpression(spreadElement.Expression, scope)),
                    BuilderElement builderElement => AnalyzeBuilder(builderElement, scope, elementHint),
                    _ => null,
                };

                if (current is null)
                {
                    continue;
                }

                if (elementType is null)
                {
                    elementType = current;
                    continue;
                }

                TypeSyntax? merged = Equals(elementType, current) ? elementType : Promote(elementType, current);
                if (merged is null && !reported && elementHint is null && IsSimpleKnownType(elementType) && IsSimpleKnownType(current))
                {
                    reported = true;
                    Error(
                        DiagnosticCodes.NoCommonElementType,
                        $"The elements of this collection have no common type (`{DisplayType(elementType)}` and `{DisplayType(current)}`). Convert them, or declare the target type.",
                        _spans.Get(collection));
                }

                elementType = merged ?? elementType;
            }

            return elementType;
        }

        private static bool IsSimpleKnownType(TypeSyntax? type)
            => type is NamedTypeSyntax { TypeArguments.Count: 0, Name: "int" or "long" or "double" or "decimal" or "bool" or "char" or "string" }
                || type is TupleTypeSyntax;

        private TypeSyntax? AnalyzeBuilder(BuilderElement builder, Scope scope, TypeSyntax? elementHint = null)
        {
            AnalyzeExpression(builder.Source, scope);
            Scope builderScope = new(scope);
            if (builder.IndexTarget is not null)
            {
                CheckBindingNames(builder.IndexTarget);
                DeclareBinding(builder.IndexTarget, TypeName("int"), builderScope, false);
            }

            CheckBindingNames(builder.ItemTarget);
            DeclareBinding(builder.ItemTarget, IterationElement(GetType(builder.Source)), builderScope, false);
            return builder.Body switch
            {
                LambdaExpressionBody expressionBody => AnalyzeExpression(expressionBody.Expression, builderScope, elementHint),
                LambdaBlockBody blockBody => AnalyzeBlockLike(blockBody.Block, builderScope),
                _ => null,
            };
        }

        private TypeSyntax? AnalyzeAggregation(AggregationExpression aggregation, Scope scope)
        {
            AnalyzeExpression(aggregation.Source, scope);
            Scope aggregationScope = new(scope);
            if (aggregation.IndexTarget is not null)
            {
                CheckBindingNames(aggregation.IndexTarget);
                DeclareBinding(aggregation.IndexTarget, TypeName("int"), aggregationScope, false);
            }

            CheckBindingNames(aggregation.ItemTarget);
            DeclareBinding(aggregation.ItemTarget, IterationElement(GetType(aggregation.Source)), aggregationScope, false);
            if (aggregation.WhereExpression is not null) AnalyzeExpression(aggregation.WhereExpression, aggregationScope);
            TypeSyntax? bodyType = AnalyzeExpression(aggregation.Body, aggregationScope);
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
                CheckBindingNames(generator.IndexTarget);
                DeclareBinding(generator.IndexTarget, TypeName("int"), generatorScope, false);
            }

            CheckBindingNames(generator.ItemTarget);
            DeclareBinding(generator.ItemTarget, IterationElement(GetType(generator.Source)), generatorScope, false);
            TypeSyntax? bodyType = generator.Body switch
            {
                LambdaExpressionBody expressionBody => AnalyzeExpression(expressionBody.Expression, generatorScope),
                LambdaBlockBody blockBody => AnalyzeBlockLike(blockBody.Block, generatorScope),
                _ => null,
            };
            return new NamedTypeSyntax("IEnumerable", Immutable.List(bodyType ?? TypeName("object")));
        }

        // A block used as a value: its type is the type of its tail expression or of its `return`.
        private TypeSyntax? AnalyzeBlockLike(BlockStatement block, Scope scope)
        {
            MarkTailStatements(block);
            AnalyzeBlock(block, scope);
            return TailValueType(block.Statements.LastOrDefault());
        }

        private TypeSyntax? TailValueType(Statement? statement)
            => statement switch
            {
                ExpressionStatement tail => GetType(tail.Expression),
                ReturnStatement { Expression: not null } tail => GetType(tail.Expression),
                BlockStatement block => TailValueType(block.Statements.LastOrDefault()),
                IfStatement { ElseBranch: not null } ifStatement => Merge(TailValueType(ifStatement.ThenBranch), TailValueType(ifStatement.ElseBranch)),
                _ => null,
            };

        private void ValidateMethodBody(string name, TypeSyntax returnType, MethodBody body, TextSpan declarationSpan)
        {
            if (IsNamed(returnType, "void"))
            {
                return;
            }

            if (body is ExpressionMethodBody expressionBody)
            {
                TypeSyntax? expressionType = GetType(expressionBody.Expression);
                if (expressionType is not null && !CanImplicitlyConvert(expressionType, returnType) && !IsUnknownLike(expressionType))
                {
                    Error(DiagnosticCodes.MissingReturnValue, $"`{name}` returns `{DisplayType(expressionType)}`, but `{DisplayType(returnType)}` is required.", declarationSpan);
                }

                return;
            }

            if (body is BlockMethodBody blockBody)
            {
                ValidateValueReturningBody(name, returnType, blockBody.Block, declarationSpan);
            }
        }

        // Spec §10.5: every path of a function that returns a value ends with `return`, `throw`, or a tail
        // expression that returns its value.
        private void ValidateValueReturningBody(string name, TypeSyntax? returnType, BlockStatement body, TextSpan declarationSpan)
        {
            if (returnType is null || IsNamed(returnType, "void"))
            {
                return;
            }

            MarkTailStatements(body);

            foreach (ReturnStatement returnStatement in EnumerateReturnStatements(body))
            {
                TextSpan returnSpan = _spans.TryGet(returnStatement, out TextSpan found) ? found : declarationSpan;
                if (returnStatement.Expression is null)
                {
                    Error(DiagnosticCodes.MissingReturnValue, $"`return` in `{name}` needs a value of type `{DisplayType(returnType)}`.", returnSpan);
                    continue;
                }

                TypeSyntax? returnExpressionType = GetType(returnStatement.Expression);
                if (returnExpressionType is not null && !CanImplicitlyConvert(returnExpressionType, returnType) && !IsUnknownLike(returnExpressionType))
                {
                    Error(DiagnosticCodes.MissingReturnValue, $"`return` in `{name}` returns `{DisplayType(returnExpressionType)}`, but `{DisplayType(returnType)}` is required.", returnSpan);
                }
            }

            if (body.Statements.Count == 0)
            {
                Error(DiagnosticCodes.MissingReturnValue, $"`{name}` must return a value of type `{DisplayType(returnType)}`, but its body is empty.", declarationSpan);
                return;
            }

            CheckTailValue(name, returnType, body.Statements[^1], declarationSpan);
        }

        private void CheckTailValue(string name, TypeSyntax returnType, Statement tail, TextSpan declarationSpan)
        {
            TextSpan span = _spans.TryGet(tail, out TextSpan found) ? found : declarationSpan;
            switch (tail)
            {
                case ReturnStatement or ThrowStatement:
                    return;
                case ExpressionStatement { Expression: ThrowExpression }:
                    return;
                case ExpressionStatement expressionStatement:
                {
                    Expression expression = expressionStatement.Expression;
                    if (!_resultReturnEligible(expression))
                    {
                        string hint = expression switch
                        {
                            AssignmentExpression { Operator: AssignmentOperator.Assign } => " A plain `x = e` does not produce a value here; write `x := e` to assign and return the value.",
                            AssignmentExpression => " A compound assignment does not return a value; add the value on the next line.",
                            PrefixExpression or PostfixExpression => " An increment does not return a value; add the value on the next line.",
                            _ => string.Empty,
                        };
                        Error(DiagnosticCodes.MissingReturnValue, $"`{name}` must end with a value of type `{DisplayType(returnType)}`.{hint}", span);
                        return;
                    }

                    TypeSyntax? tailType = GetType(expression);
                    if (IsNamed(tailType, "void"))
                    {
                        Error(DiagnosticCodes.MissingReturnValue, $"The last expression of `{name}` produces no value, but `{DisplayType(returnType)}` is required.", span);
                        return;
                    }

                    if (tailType is not null && !CanImplicitlyConvert(tailType, returnType) && !IsUnknownLike(tailType))
                    {
                        Error(DiagnosticCodes.MissingReturnValue, $"The last expression of `{name}` is `{DisplayType(tailType)}`, but `{DisplayType(returnType)}` is required.", span);
                    }

                    return;
                }
                case BlockStatement block:
                    if (block.Statements.Count == 0)
                    {
                        Error(DiagnosticCodes.MissingReturnValue, $"`{name}` must end with a value of type `{DisplayType(returnType)}`, but this block is empty.", span);
                        return;
                    }

                    CheckTailValue(name, returnType, block.Statements[^1], declarationSpan);
                    return;
                case IfStatement { ElseBranch: not null } ifStatement:
                    CheckTailValue(name, returnType, ifStatement.ThenBranch, declarationSpan);
                    CheckTailValue(name, returnType, ifStatement.ElseBranch, declarationSpan);
                    return;
                case IfStatement:
                    Error(
                        DiagnosticCodes.MissingReturnValue,
                        $"`{name}` must end with a value of type `{DisplayType(returnType)}`, but this `if` has no `else`, so the value is missing when the condition is false.",
                        span);
                    return;
                case WhileStatement { Condition: LiteralExpression { Kind: LiteralKind.True } } loop when !ContainsBreak(loop.Body):
                    // `while true` without `break` never falls through.
                    return;
                case TryStatement:
                    Error(
                        DiagnosticCodes.MissingReturnValue,
                        $"`{name}` must end with a value of type `{DisplayType(returnType)}`. A `try` statement does not return its last value; use `return` inside it.",
                        span);
                    return;
                default:
                    Error(
                        DiagnosticCodes.MissingReturnValue,
                        $"`{name}` must end with a value of type `{DisplayType(returnType)}`. A loop does not produce a value; add `return` or a final expression after it.",
                        span);
                    return;
            }
        }

        private bool _resultReturnEligible(Expression expression)
            => expression switch
            {
                AssignmentExpression assignment => assignment.IsExplicitValueAssignment
                    || (assignment.Operator is AssignmentOperator.AddAssign or AssignmentOperator.SubtractAssign
                        && IsValueReturningSetLike(GetType(assignment.Target))),
                PrefixExpression { Operator: PostfixOperator.Decrement } prefix => IsPoppable(GetType(prefix.Operand)),
                PrefixExpression or PostfixExpression => false,
                _ => true,
            };

        private static bool IsUnknownLike(TypeSyntax type)
            => type is NamedTypeSyntax { Name: "object" or "var" or "IEnumerable" };

        // `break` that leaves this loop: nested loops and lambdas have their own.
        private static bool ContainsBreak(Statement statement)
            => statement switch
            {
                BreakStatement => true,
                BlockStatement block => block.Statements.Any(ContainsBreak),
                IfStatement ifStatement => ContainsBreak(ifStatement.ThenBranch) || (ifStatement.ElseBranch is not null && ContainsBreak(ifStatement.ElseBranch)),
                TryStatement tryStatement => ContainsBreak(tryStatement.Body) || tryStatement.Catches.Any(catchClause => ContainsBreak(catchClause.Body)) || (tryStatement.Finally is not null && ContainsBreak(tryStatement.Finally)),
                _ => false,
            };

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
                case TryStatement tryStatement:
                    foreach (ReturnStatement returnStatement in EnumerateReturnStatements(tryStatement.Body))
                    {
                        yield return returnStatement;
                    }

                    foreach (CatchClause catchClause in tryStatement.Catches)
                    {
                        foreach (ReturnStatement returnStatement in EnumerateReturnStatements(catchClause.Body))
                        {
                            yield return returnStatement;
                        }
                    }

                    yield break;
            }
        }

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

        // Result types of the aggregate, math and helper families (spec §22, §23, §24).
        private TypeSyntax? IntrinsicType(string name, IReadOnlyList<ArgumentSyntax> args, TypeSyntax? receiverType)
        {
            bool memberForm = receiverType is not null;
            TypeSyntax? source = receiverType ?? ArgType(args, 0);
            TypeSyntax? element = IterationElement(source);
            int helperArguments = memberForm ? args.Count : args.Count - 1;
            return name switch
            {
                "min" or "max" when !memberForm && args.Count >= 2 => args.Select((_, index) => ArgType(args, index)).Aggregate((TypeSyntax?)null, (merged, next) => merged is null ? next : Merge(merged, next)),
                "min" or "max" or "sum" => element,
                "sumBy" => LambdaResultType(args) ?? element,
                "count" or "findIndex" or "findLastIndex" or "lowerBound" or "upperBound" => TypeName("int"),
                "any" or "all" or "chmin" or "chmax" => TypeName("bool"),
                "find" when element is not null => helperArguments >= 2 ? element : NullableElement(element),
                "minBy" or "maxBy" => element,
                "sort" or "sortBy" or "sortWith" or "distinct" or "reverse" or "copy" or "filter" when element is not null => new ArrayTypeSyntax(element, 1),
                "map" when LambdaResultType(args) is TypeSyntax mapped => new ArrayTypeSyntax(mapped, 1),
                "fold" => ArgType(args, memberForm ? 0 : 1) is TypeSyntax foldSeed ? Merge(foldSeed, LambdaResultType(args)) ?? foldSeed : LambdaResultType(args),
                "scan" when (ArgType(args, memberForm ? 0 : 1) ?? LambdaResultType(args)) is TypeSyntax scanState => new ArrayTypeSyntax(scanState, 1),
                "mapFold" when LambdaResultType(args) is TupleTypeSyntax { Elements.Count: 2 } mapFoldResult
                    => new TupleTypeSyntax([new ArrayTypeSyntax(mapFoldResult.Elements[0], 1), mapFoldResult.Elements[1]]),
                "groupCount" or "freq" or "index" when element is not null => new NamedTypeSyntax("Dictionary", Immutable.List(element, TypeName("int"))),
                "abs" => ArgType(args, 0),
                "sqrt" => TypeName("double"),
                "pow" when args.Count == 3 => TypeName("long"),
                "pow" => IsIntegral(ArgType(args, 0)) && IsIntegral(ArgType(args, 1))
                    ? (IsNamed(ArgType(args, 0), "long") ? TypeName("long") : TypeName("int"))
                    : TypeName("double"),
                "clamp" => Merge(Merge(ArgType(args, 0), ArgType(args, 1)), ArgType(args, 2)),
                "gcd" or "lcm" => Merge(ArgType(args, 0), ArgType(args, 1)),
                "floor" or "ceil" when args.Count == 2 => Merge(ArgType(args, 0), ArgType(args, 1)),
                "floor" or "ceil" or "round" => InferMathRoundingType(ArgType(args, 0)),
                "popcount" or "bitLength" => TypeName("int"),
                _ => null,
            };
        }

        private static TypeSyntax NullableElement(TypeSyntax element)
            => element is NullableTypeSyntax ? element : new NullableTypeSyntax(element);

        private static bool IsIntegral(TypeSyntax? type)
            => type is NamedTypeSyntax { Name: "int" or "long" or "char" or "short" or "byte" };

        private static TypeSyntax InferMathRoundingType(TypeSyntax? sourceType)
            => sourceType is NamedTypeSyntax { Name: "decimal" } ? TypeName("decimal") : TypeName("double");

        private static bool IsConversionKeyword(string name)
            => name is "int" or "long" or "double" or "decimal" or "bool" or "char" or "string";

        private TypeSyntax? ArgType(IReadOnlyList<ArgumentSyntax> args, int index)
            => index >= 0 && index < args.Count && args[index] is ExpressionArgumentSyntax expressionArgument ? GetType(expressionArgument.Expression) : null;

        private TypeSyntax? GetType(Expression? expression)
            => expression is not null && ExpressionTypes.TryGetValue(expression, out TypeSyntax? type) ? type : null;

        private void CheckBindingNames(BindingTarget target)
        {
            switch (target)
            {
                case NameTarget nameTarget:
                    WarnOnDeclarationName(nameTarget.Name, _spans.Get(nameTarget), "binding");
                    break;
                case TupleTarget tupleTarget:
                    foreach (BindingTarget element in tupleTarget.Elements) CheckBindingNames(element);
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
            LiteralKind.Integer => TypeName(PscpNumericLiterals.GetTypeName(literal.RawText, isFloat: false)),
            LiteralKind.Float => TypeName(PscpNumericLiterals.GetTypeName(literal.RawText, isFloat: true)),
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

        // The value an iteration binds: `Dictionary<K, V>` yields `(K, V)` pairs (spec §16.4).
        private static TypeSyntax? IterationElement(TypeSyntax? type)
            => type is NamedTypeSyntax { TypeArguments.Count: 2 } dictionary && dictionary.Name is "Dictionary" or "SortedDictionary" or "System.Collections.Generic.Dictionary"
                ? new TupleTypeSyntax(dictionary.TypeArguments)
                : EnumerableElement(type);

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
                    or "SortedDictionary" or "System.Collections.Generic.SortedDictionary"
                    or "PriorityQueue" or "System.Collections.Generic.PriorityQueue";
        private static TypeSyntax? Merge(TypeSyntax? left, TypeSyntax? right) => Equals(left, right) ? left : Promote(left, right) ?? left ?? right;
        private static TypeSyntax? Promote(TypeSyntax? left, TypeSyntax? right)
        {
            if (left is not NamedTypeSyntax l || right is not NamedTypeSyntax r) return null;
            int Rank(string name) => name switch { "char" => 0, "int" => 0, "long" => 1, "double" => 2, "decimal" => 3, _ => -1 };
            int lr = Rank(l.Name);
            int rr = Rank(r.Name);
            if (lr < 0 || rr < 0)
            {
                return null;
            }

            // `char` arithmetic produces `int` (spec §8.1).
            TypeSyntax wider = lr >= rr ? left : right;
            return wider is NamedTypeSyntax { Name: "char" } ? new NamedTypeSyntax("int", Immutable.List<TypeSyntax>()) : wider;
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
                Warning(DiagnosticCodes.GenericWarning, $"`{name}` is also a reserved C# keyword. Rename it or expect escaping in generated C#.", span);
            }

            if (PscpIntrinsicCatalog.GlobalValues.Contains(name) || PscpIntrinsicCatalog.IntrinsicCallNames.Contains(name))
            {
                Diagnostics.Add(new Diagnostic($"`{name}` hides the PSCP intrinsic `{name}` in this scope.", span, DiagnosticSeverity.Info, DiagnosticCodes.ShadowsIntrinsic));
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

        private void Error(string message, TextSpan span) => Diagnostics.Add(new Diagnostic(message, span, DiagnosticSeverity.Error, DiagnosticCodes.GenericError));
        private void Error(string code, string message, TextSpan span) => Diagnostics.Add(new Diagnostic(message, span, DiagnosticSeverity.Error, code));
        private void Warning(string message, TextSpan span) => Diagnostics.Add(new Diagnostic(message, span, DiagnosticSeverity.Warning, DiagnosticCodes.GenericWarning));
        private void Warning(string code, string message, TextSpan span) => Diagnostics.Add(new Diagnostic(message, span, DiagnosticSeverity.Warning, code));

        // Spec §18.3: a collection of collections of collections has no automatic rendering.
        private void CheckRenderable(TypeSyntax? type, Expression expression)
        {
            if (CollectionDepth(type) >= 3)
            {
                Error(
                    DiagnosticCodes.DeepCollectionRendering,
                    $"A `{DisplayType(type)}` is nested three levels deep and has no automatic output form. Print it with an explicit loop.",
                    _spans.Get(expression));
            }
        }

        private static int CollectionDepth(TypeSyntax? type)
            => type switch
            {
                ArrayTypeSyntax array => array.Depth + CollectionDepth(array.ElementType),
                NamedTypeSyntax { Name: "string" } => 0,
                NamedTypeSyntax named when named.TypeArguments.Count == 1 && named.Name is "List" or "LinkedList" or "IEnumerable" or "HashSet" or "SortedSet" or "Queue" or "Stack"
                    => 1 + CollectionDepth(named.TypeArguments[0]),
                _ => 0,
            };
    }
}



