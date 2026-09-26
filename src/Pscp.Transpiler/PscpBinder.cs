namespace Pscp.Transpiler;

// Name resolution that changes the tree, run between the parser and the semantic analyzer.
//
// - References to the intrinsic `stdin` / `stdout` become the reserved `__pscp_stdin` / `__pscp_stdout`, so a
//   user declaration named `stdin` keeps its name in the generated C# (spec §5.4).
// - A pipe target whose head names a collection helper that no user declaration shadows gets receiver
//   insertion: `xs |> filter(p)` is `xs.filter(p)` (spec §13.3).
// - `A b` statements become space-calls when `A` is callable rather than a type (spec §11.5), and `(f)x`
//   becomes an application when `f` is a value.
//
// It also reports the name rules that need scopes: reserved `__pscp` names, a variable and a function sharing a
// name, `break`/`continue` outside a loop, type declarations referring to non-constant program-scope names
// (spec §7.4), missing and unnecessary `rec` from the call graph (spec §10.3), and top-level calls to functions
// that read top-level variables before their declaration has run (spec §7.3).
internal sealed class PscpBinder : SyntaxRewriter
{
    public const string StdinName = "__pscp_stdin";
    public const string StdoutName = "__pscp_stdout";

    private enum SymbolKind
    {
        Variable,
        Constant,
        Function,
        Type,
    }

    private enum ScopeKind
    {
        Program,
        Type,
        Function,
        Local,
    }

    private sealed record Symbol(SymbolKind Kind, object? Declaration);

    private sealed class Scope
    {
        public Scope(Scope? parent, ScopeKind kind)
        {
            Parent = parent;
            Kind = kind;
        }

        public Scope? Parent { get; }
        public ScopeKind Kind { get; }
        public Dictionary<string, Symbol> Symbols { get; } = new(StringComparer.Ordinal);
    }

    private readonly List<Diagnostic> _diagnostics = [];
    private readonly Scope _programScope = new(null, ScopeKind.Program);
    private readonly Dictionary<FunctionDeclaration, List<FunctionDeclaration>> _calls = new(ReferenceEqualityComparer.Instance);
    private readonly List<FunctionDeclaration> _statementFunctions = [];
    private readonly Dictionary<FunctionDeclaration, HashSet<string>> _globalReads = new(ReferenceEqualityComparer.Instance);
    private readonly List<(int StatementIndex, FunctionDeclaration Function, Expression Call)> _topLevelCalls = [];
    private readonly Dictionary<string, int> _globalDeclarationIndex = new(StringComparer.Ordinal);
    private readonly HashSet<string> _constantsUsedByTypes = new(StringComparer.Ordinal);
    private readonly HashSet<DeclarationStatement> _callLikeTopLevelDeclarations = new(ReferenceEqualityComparer.Instance);
    private Scope _scope;
    private FunctionDeclaration? _currentFunction;
    private FunctionDeclaration? _currentTopLevelFunction;
    private int _lambdaDepth;
    private int _loopDepth;
    private int _typeDepth;
    private int _topLevelStatementIndex = -1;

    private PscpBinder(SyntaxSpans spans)
        : base(spans)
    {
        _scope = _programScope;
    }

    public static BindingResult Bind(PscpProgram program, SyntaxSpans spans)
    {
        PscpBinder binder = new(spans);
        PscpProgram bound = binder.VisitProgram(program);
        binder.ReportRecursion();
        binder.ReportReadsBeforeInitialization();
        return new BindingResult(bound, binder._diagnostics, binder._constantsUsedByTypes);
    }

    public override PscpProgram VisitProgram(PscpProgram program)
    {
        foreach (TypeDeclaration type in program.Types)
        {
            Declare(_programScope, type.Name, new Symbol(SymbolKind.Type, type), type);
        }

        foreach (FunctionDeclaration function in program.Functions)
        {
            Declare(_programScope, function.Name, new Symbol(SymbolKind.Function, function), function);
            _statementFunctions.Add(function);
        }

        for (int i = 0; i < program.GlobalStatements.Count; i++)
        {
            if (program.GlobalStatements[i] is not DeclarationStatement declaration)
            {
                continue;
            }

            if (IsAmbiguousCallLikeDeclaration(declaration, out (string Callee, string Argument)? callLike)
                && IsCallableName(callLike!.Value.Callee))
            {
                _callLikeTopLevelDeclarations.Add(declaration);
                continue;
            }

            bool constant = IsConstantDeclaration(declaration);
            foreach (NameTarget target in EnumerateNameTargets(declaration.Targets))
            {
                Declare(_programScope, target.Name, new Symbol(constant ? SymbolKind.Constant : SymbolKind.Variable, declaration), target);
                _globalDeclarationIndex.TryAdd(target.Name, i);
            }
        }

        foreach (Statement statement in program.GlobalStatements)
        {
            if (statement is LocalFunctionStatement localFunction)
            {
                // Top-level `LocalFunctionStatement`s do not occur (the parser lifts functions), but a nested
                // namespace body could produce them.
                Declare(_programScope, localFunction.Function.Name, new Symbol(SymbolKind.Function, localFunction.Function), localFunction.Function);
            }
        }

        IReadOnlyList<TypeDeclaration> types = VisitList(program.Types, VisitTypeDeclaration);
        IReadOnlyList<FunctionDeclaration> functions = VisitList(program.Functions, VisitTopLevelFunction);

        Statement[]? statements = null;
        for (int i = 0; i < program.GlobalStatements.Count; i++)
        {
            _topLevelStatementIndex = i;
            Statement original = program.GlobalStatements[i];
            Statement rewritten = VisitTopLevelStatement(original);
            if (statements is null && !ReferenceEquals(rewritten, original))
            {
                statements = new Statement[program.GlobalStatements.Count];
                for (int j = 0; j < i; j++)
                {
                    statements[j] = program.GlobalStatements[j];
                }
            }

            if (statements is not null)
            {
                statements[i] = rewritten;
            }
        }

        _topLevelStatementIndex = -1;
        IReadOnlyList<Statement> globalStatements = statements is null ? program.GlobalStatements : Array.AsReadOnly(statements);
        return Same(types, program.Types) && Same(functions, program.Functions) && Same(globalStatements, program.GlobalStatements)
            ? program
            : Replace(program, program with { Types = types, Functions = functions, GlobalStatements = globalStatements });
    }

    // Top-level declarations are already in the program scope; only their initializers are visited here.
    private Statement VisitTopLevelStatement(Statement statement)
    {
        if (statement is DeclarationStatement callLikeDeclaration
            && _callLikeTopLevelDeclarations.Contains(callLikeDeclaration)
            && IsAmbiguousCallLikeDeclaration(callLikeDeclaration, out (string Callee, string Argument)? callLike))
        {
            return TryBindCallLikeDeclaration(callLikeDeclaration, callLike!.Value) ?? statement;
        }

        if (statement is DeclarationStatement declaration)
        {
            TypeSyntax? type = declaration.ExplicitType is null ? null : VisitType(declaration.ExplicitType);
            Expression? initializer = declaration.Initializer is null ? null : VisitExpression(declaration.Initializer);
            return Same(type, declaration.ExplicitType) && Same(initializer, declaration.Initializer)
                ? declaration
                : Replace(declaration, declaration with { ExplicitType = type, Initializer = initializer });
        }

        return VisitStatement(statement);
    }

    private FunctionDeclaration VisitTopLevelFunction(FunctionDeclaration function)
    {
        FunctionDeclaration? savedTopLevel = _currentTopLevelFunction;
        _currentTopLevelFunction = function;
        try
        {
            return VisitFunction(function);
        }
        finally
        {
            _currentTopLevelFunction = savedTopLevel;
        }
    }

    public override TypeDeclaration VisitTypeDeclaration(TypeDeclaration declaration)
    {
        Scope typeScope = new(_scope, ScopeKind.Type);
        foreach (string member in EnumerateTypeMemberNames(declaration))
        {
            typeScope.Symbols.TryAdd(member, new Symbol(SymbolKind.Variable, null));
        }

        typeScope.Symbols.TryAdd("this", new Symbol(SymbolKind.Variable, null));
        typeScope.Symbols.TryAdd("base", new Symbol(SymbolKind.Variable, null));
        foreach (TypeMember member in declaration.Members)
        {
            if (member is NestedTypeMember nested)
            {
                typeScope.Symbols.TryAdd(nested.Declaration.Name, new Symbol(SymbolKind.Type, nested.Declaration));
            }
        }

        Scope saved = _scope;
        FunctionDeclaration? savedFunction = _currentFunction;
        FunctionDeclaration? savedTopLevel = _currentTopLevelFunction;
        int savedLoopDepth = _loopDepth;
        _scope = typeScope;
        _currentFunction = null;
        _currentTopLevelFunction = null;
        _loopDepth = 0;
        _typeDepth++;
        try
        {
            return base.VisitTypeDeclaration(declaration);
        }
        finally
        {
            _typeDepth--;
            _scope = saved;
            _currentFunction = savedFunction;
            _currentTopLevelFunction = savedTopLevel;
            _loopDepth = savedLoopDepth;
        }
    }

    public override TypeMember VisitTypeMember(TypeMember member)
    {
        switch (member)
        {
            case MethodMember method:
                return WithScope(ScopeKind.Function, () =>
                {
                    DeclareParameters(method.Parameters);
                    return base.VisitTypeMember(member);
                });
            case OperatorMember @operator:
                return WithScope(ScopeKind.Function, () =>
                {
                    DeclareParameters(@operator.Parameters);
                    return base.VisitTypeMember(member);
                });
            case OrderingShorthandMember ordering:
                return WithScope(ScopeKind.Function, () =>
                {
                    foreach (string parameter in ordering.ParameterNames)
                    {
                        _scope.Symbols[parameter] = new Symbol(SymbolKind.Variable, null);
                    }

                    return base.VisitTypeMember(member);
                });
            case PropertyMember:
                return WithScope(ScopeKind.Function, () => base.VisitTypeMember(member));
            default:
                return base.VisitTypeMember(member);
        }
    }

    public override FunctionDeclaration VisitFunction(FunctionDeclaration function)
    {
        CheckReservedName(function.Name, function);
        FunctionDeclaration? savedFunction = _currentFunction;
        int savedLambdaDepth = _lambdaDepth;
        int savedLoopDepth = _loopDepth;
        _currentFunction = function;
        _lambdaDepth = 0;
        _loopDepth = 0;
        _calls.TryAdd(function, []);
        try
        {
            return WithScope(ScopeKind.Function, () =>
            {
                DeclareParameters(function.Parameters);
                return base.VisitFunction(function);
            });
        }
        finally
        {
            _currentFunction = savedFunction;
            _lambdaDepth = savedLambdaDepth;
            _loopDepth = savedLoopDepth;
        }
    }

    public override BlockStatement VisitBlock(BlockStatement block)
        => WithScope(ScopeKind.Local, () =>
        {
            foreach (Statement statement in block.Statements)
            {
                if (statement is LocalFunctionStatement localFunction)
                {
                    Declare(_scope, localFunction.Function.Name, new Symbol(SymbolKind.Function, localFunction.Function), localFunction.Function);
                    _statementFunctions.Add(localFunction.Function);
                }
            }

            return base.VisitBlock(block);
        });

    public override Statement VisitEmbeddedStatement(Statement statement)
        => statement is BlockStatement
            ? VisitStatement(statement)
            : WithScope(ScopeKind.Local, () => VisitStatement(statement));

    public override Statement VisitDeclarationStatement(DeclarationStatement declaration)
    {
        if (IsAmbiguousCallLikeDeclaration(declaration, out (string Callee, string Argument)? callLike))
        {
            Statement? call = TryBindCallLikeDeclaration(declaration, callLike!.Value);
            if (call is not null)
            {
                return call;
            }
        }

        Statement rewritten = base.VisitDeclarationStatement(declaration);
        foreach (NameTarget target in EnumerateNameTargets(declaration.Targets))
        {
            Declare(_scope, target.Name, new Symbol(SymbolKind.Variable, declaration), target);
        }

        return rewritten;
    }

    // `A b` with no initializer: a declaration when `A` is a type, a space-call when `A` is callable (spec §11.5).
    private static bool IsAmbiguousCallLikeDeclaration(DeclarationStatement declaration, out (string Callee, string Argument)? callLike)
    {
        callLike = null;
        if (declaration.IsInputShorthand
            || declaration.Initializer is not null
            || declaration.Mutability != MutabilityKind.Immutable
            || declaration.Targets.Count != 1
            || declaration.Targets[0] is not NameTarget target
            || declaration.ExplicitType is not NamedTypeSyntax { TypeArguments.Count: 0 } named
            || named.Name.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        callLike = (named.Name, target.Name);
        return true;
    }

    private bool IsCallableName(string name)
    {
        Symbol? symbol = Lookup(name, out _);
        return symbol?.Kind is SymbolKind.Function or SymbolKind.Variable or SymbolKind.Constant
            || (symbol is null && PscpIntrinsicCatalog.IntrinsicCallNames.Contains(name));
    }

    private Statement? TryBindCallLikeDeclaration(DeclarationStatement declaration, (string Callee, string Argument) callLike)
    {
        Symbol? symbol = Lookup(callLike.Callee, out _);
        if (!IsCallableName(callLike.Callee))
        {
            if (symbol is null
                && !PscpIntrinsicCatalog.BuiltinTypes.Contains(callLike.Callee)
                && callLike.Callee.Length > 0
                && char.IsLower(callLike.Callee[0]))
            {
                Report(
                    DiagnosticCodes.NotTypeNorCallable,
                    $"`{callLike.Callee}` is neither a type nor something callable, so `{callLike.Callee} {callLike.Argument}` is neither a declaration nor a call.",
                    Spans.Get(declaration));
            }

            return null;
        }

        TextSpan span = Spans.Get(declaration);
        IdentifierExpression callee = new(callLike.Callee);
        IdentifierExpression argument = new(callLike.Argument);
        if (declaration.ExplicitType is not null && Spans.TryGet(declaration.ExplicitType, out TextSpan calleeSpan))
        {
            Spans.Set(callee, calleeSpan);
        }

        if (Spans.TryGet(declaration.Targets[0], out TextSpan argumentSpan))
        {
            Spans.Set(argument, argumentSpan);
        }

        CallExpression call = new(callee, [new ExpressionArgumentSyntax(null, ArgumentModifier.None, argument)], true);
        Spans.Set(call, span);
        ExpressionStatement statement = new(call, false);
        Spans.Set(statement, span);
        return VisitStatement(statement);
    }

    public override Statement VisitForInStatement(ForInStatement statement)
    {
        Expression source = VisitExpression(statement.Source);
        return WithScope(ScopeKind.Local, () =>
        {
            DeclareBinding(statement.Iterator);
            Statement body = VisitLoopBody(statement.Body);
            return Same(source, statement.Source) && Same(body, statement.Body)
                ? statement
                : Replace(statement, statement with { Source = source, Body = body });
        });
    }

    public override Statement VisitFastForStatement(FastForStatement statement)
    {
        Expression source = VisitExpression(statement.Source);
        return WithScope(ScopeKind.Local, () =>
        {
            if (statement.IndexTarget is not null)
            {
                DeclareBinding(statement.IndexTarget);
            }

            DeclareBinding(statement.ItemTarget);
            Statement body = VisitLoopBody(statement.Body);
            return Same(source, statement.Source) && Same(body, statement.Body)
                ? statement
                : Replace(statement, statement with { Source = source, Body = body });
        });
    }

    public override Statement VisitCStyleForStatement(CStyleForStatement statement)
        => WithScope(ScopeKind.Local, () =>
        {
            foreach (string name in PscpSyntaxFacts.GetCStyleForHeaderBindings(statement.HeaderText))
            {
                _scope.Symbols[name] = new Symbol(SymbolKind.Variable, null);
            }

            Statement body = VisitLoopBody(statement.Body);
            return Same(body, statement.Body) ? statement : Replace(statement, statement with { Body = body });
        });

    public override Statement VisitWhileStatement(WhileStatement statement)
    {
        Expression condition = VisitExpression(statement.Condition);
        Statement body = VisitLoopBody(statement.Body);
        return Same(condition, statement.Condition) && Same(body, statement.Body)
            ? statement
            : Replace(statement, statement with { Condition = condition, Body = body });
    }

    private Statement VisitLoopBody(Statement body)
    {
        _loopDepth++;
        try
        {
            return VisitEmbeddedStatement(body);
        }
        finally
        {
            _loopDepth--;
        }
    }

    public override Statement VisitStatement(Statement statement)
    {
        if (statement is BreakStatement or ContinueStatement && _loopDepth == 0)
        {
            Report(
                DiagnosticCodes.JumpOutsideLoop,
                $"`{(statement is BreakStatement ? "break" : "continue")}` must be inside a loop (`while`, `for`, or `->`).",
                Spans.Get(statement));
        }

        return base.VisitStatement(statement);
    }

    public override CatchClause VisitCatchClause(CatchClause clause)
        => WithScope(ScopeKind.Local, () =>
        {
            if (clause.Name is not null)
            {
                _scope.Symbols[clause.Name] = new Symbol(SymbolKind.Variable, clause);
            }

            return base.VisitCatchClause(clause);
        });

    public override Expression VisitIdentifier(IdentifierExpression identifier)
    {
        Symbol? symbol = Lookup(identifier.Name, out Scope? owner);
        if (symbol is null)
        {
            string? reserved = identifier.Name switch
            {
                "stdin" => StdinName,
                "stdout" => StdoutName,
                _ => null,
            };

            return reserved is null ? identifier : Replace(identifier, new IdentifierExpression(reserved));
        }

        if (ReferenceEquals(owner, _programScope))
        {
            NoteProgramScopeReference(identifier, symbol);
        }

        return identifier;
    }

    private void NoteProgramScopeReference(IdentifierExpression identifier, Symbol symbol)
    {
        if (_typeDepth > 0)
        {
            switch (symbol.Kind)
            {
                case SymbolKind.Constant:
                    _constantsUsedByTypes.Add(identifier.Name);
                    break;
                case SymbolKind.Variable or SymbolKind.Function:
                    Report(
                        DiagnosticCodes.TypeReferencesProgramScope,
                        symbol.Kind == SymbolKind.Function
                            ? $"Code inside a type cannot call the top-level function `{identifier.Name}`. Move it into the type as a method, or pass the result in."
                            : $"Code inside a type can only use top-level `let` constants; `{identifier.Name}` is not a compile-time constant. Pass it in through a constructor or parameter.",
                        Spans.Get(identifier));
                    break;
            }

            return;
        }

        if (symbol.Kind is SymbolKind.Variable or SymbolKind.Constant && _currentTopLevelFunction is not null)
        {
            if (!_globalReads.TryGetValue(_currentTopLevelFunction, out HashSet<string>? reads))
            {
                reads = new HashSet<string>(StringComparer.Ordinal);
                _globalReads[_currentTopLevelFunction] = reads;
            }

            reads.Add(identifier.Name);
        }
    }

    public override Expression VisitMemberAccess(MemberAccessExpression member)
        => base.VisitMemberAccess(member);

    public override Expression VisitCall(CallExpression call)
    {
        if (call.Pipe == PipeKind.Forward
            && call.Callee is IdentifierExpression { Name: var helperName }
            && PscpIntrinsicCatalog.CollectionHelperNames.Contains(helperName)
            && Lookup(helperName, out _) is null
            && call.Arguments.Count > 0
            && call.Arguments[0] is ExpressionArgumentSyntax { Modifier: ArgumentModifier.None, Name: null } receiverArgument)
        {
            // `xs |> filter(p)` is `xs.filter(p)`: the piped value becomes the receiver.
            MemberAccessExpression member = new(receiverArgument.Expression, helperName);
            Spans.Copy(call.Callee, member);
            CallExpression memberCall = new(member, call.Arguments.Skip(1).ToArray(), false);
            Spans.Copy(call, memberCall);
            return VisitCall(memberCall);
        }

        if (call.Callee is IdentifierExpression calleeIdentifier
            && Lookup(calleeIdentifier.Name, out _) is { Kind: SymbolKind.Function, Declaration: FunctionDeclaration target })
        {
            if (_currentFunction is not null && _lambdaDepth == 0 && _calls.TryGetValue(_currentFunction, out List<FunctionDeclaration>? edges))
            {
                edges.Add(target);
            }

            if (_currentFunction is null && _typeDepth == 0 && _topLevelStatementIndex >= 0 && IsTopLevelFunction(target))
            {
                _topLevelCalls.Add((_topLevelStatementIndex, target, call));
            }
        }

        return base.VisitCall(call);
    }

    private bool IsTopLevelFunction(FunctionDeclaration function)
        => _programScope.Symbols.TryGetValue(function.Name, out Symbol? symbol) && ReferenceEquals(symbol.Declaration, function);

    public override ArgumentSyntax VisitArgument(ArgumentSyntax argument)
    {
        if (argument is OutDeclarationArgumentSyntax declaration)
        {
            DeclareBinding(declaration.Target);
            return argument;
        }

        return base.VisitArgument(argument);
    }

    public override Expression VisitCast(CastExpression cast)
    {
        if (cast.Type is NamedTypeSyntax { TypeArguments.Count: 0 } named
            && !named.Name.Contains('.', StringComparison.Ordinal)
            && Lookup(named.Name, out _) is { Kind: SymbolKind.Variable or SymbolKind.Constant or SymbolKind.Function })
        {
            // `(f)x` where `f` is a value is an application with a parenthesized head.
            IdentifierExpression callee = new(named.Name);
            Spans.Copy(cast.Type, callee);
            CallExpression call = new(callee, [new ExpressionArgumentSyntax(null, ArgumentModifier.None, cast.Operand)], true);
            Spans.Copy(cast, call);
            return VisitCall(call);
        }

        return base.VisitCast(cast);
    }

    public override Expression VisitIsPattern(IsPatternExpression expression)
    {
        Expression rewritten = base.VisitIsPattern(expression);
        DeclarePatternDesignations(expression.Pattern);
        return rewritten;
    }

    public override Expression VisitSwitch(SwitchExpression @switch)
    {
        Expression receiver = VisitExpression(@switch.Receiver);
        IReadOnlyList<SwitchArm> arms = VisitList(@switch.Arms, arm => WithScope(ScopeKind.Local, () =>
        {
            DeclarePatternDesignations(arm.Pattern);
            return VisitSwitchArm(arm);
        }));
        return Same(receiver, @switch.Receiver) && Same(arms, @switch.Arms)
            ? @switch
            : Replace(@switch, @switch with { Receiver = receiver, Arms = arms });
    }

    public override Expression VisitLambda(LambdaExpression lambda)
    {
        int savedLoopDepth = _loopDepth;
        _lambdaDepth++;
        _loopDepth = 0;
        try
        {
            return WithScope(ScopeKind.Function, () =>
            {
                foreach (LambdaParameter parameter in lambda.Parameters)
                {
                    DeclareBinding(parameter.Target);
                }

                return base.VisitLambda(lambda);
            });
        }
        finally
        {
            _lambdaDepth--;
            _loopDepth = savedLoopDepth;
        }
    }

    public override CollectionElement VisitCollectionElement(CollectionElement element)
    {
        if (element is not BuilderElement builder)
        {
            return base.VisitCollectionElement(element);
        }

        Expression source = VisitExpression(builder.Source);
        LambdaBody body = WithIterationScope(builder.IndexTarget, builder.ItemTarget, () => VisitLambdaBody(builder.Body));
        return Same(source, builder.Source) && Same(body, builder.Body)
            ? element
            : Replace(element, builder with { Source = source, Body = body });
    }

    public override Expression VisitGenerator(GeneratorExpression generator)
    {
        Expression source = VisitExpression(generator.Source);
        LambdaBody body = WithIterationScope(generator.IndexTarget, generator.ItemTarget, () => VisitLambdaBody(generator.Body));
        return Same(source, generator.Source) && Same(body, generator.Body)
            ? generator
            : Replace(generator, generator with { Source = source, Body = body });
    }

    public override Expression VisitAggregation(AggregationExpression aggregation)
    {
        Expression source = VisitExpression(aggregation.Source);
        (Expression? where, Expression body) = WithIterationScope(aggregation.IndexTarget, aggregation.ItemTarget, () =>
            (aggregation.WhereExpression is null ? null : VisitExpression(aggregation.WhereExpression), VisitExpression(aggregation.Body)));
        return Same(source, aggregation.Source) && Same(where, aggregation.WhereExpression) && Same(body, aggregation.Body)
            ? aggregation
            : Replace(aggregation, aggregation with { Source = source, WhereExpression = where, Body = body });
    }

    private T WithIterationScope<T>(BindingTarget? indexTarget, BindingTarget itemTarget, Func<T> visit)
        => WithScope(ScopeKind.Local, () =>
        {
            if (indexTarget is not null)
            {
                DeclareBinding(indexTarget);
            }

            DeclareBinding(itemTarget);
            return visit();
        });

    private T WithScope<T>(ScopeKind kind, Func<T> visit)
    {
        Scope saved = _scope;
        _scope = new Scope(saved, kind);
        try
        {
            return visit();
        }
        finally
        {
            _scope = saved;
        }
    }

    // Unqualified lookup (spec §5.1). From inside a type the program scope offers only types and constants; a
    // hit on anything else is still returned so the caller can report it.
    private Symbol? Lookup(string name, out Scope? owner)
    {
        for (Scope? scope = _scope; scope is not null; scope = scope.Parent)
        {
            if (scope.Symbols.TryGetValue(name, out Symbol? symbol))
            {
                owner = scope;
                return symbol;
            }
        }

        owner = null;
        return null;
    }

    private void Declare(Scope scope, string name, Symbol symbol, object declarationNode)
    {
        CheckReservedName(name, declarationNode);
        if (scope.Symbols.TryGetValue(name, out Symbol? existing))
        {
            bool conflict = (existing.Kind == SymbolKind.Function) != (symbol.Kind == SymbolKind.Function)
                && existing.Kind != SymbolKind.Type
                && symbol.Kind != SymbolKind.Type;
            if (conflict)
            {
                Report(
                    DiagnosticCodes.VariableFunctionConflict,
                    $"`{name}` is declared both as a variable and as a function in the same scope. Rename one of them.",
                    Spans.GetName(declarationNode));
            }

            if (existing.Kind == SymbolKind.Function && symbol.Kind != SymbolKind.Function)
            {
                return;
            }
        }

        scope.Symbols[name] = symbol;
    }

    private void DeclareParameters(IReadOnlyList<ParameterSyntax> parameters)
    {
        foreach (ParameterSyntax parameter in parameters)
        {
            DeclareBinding(parameter.Target);
        }
    }

    private void DeclareBinding(BindingTarget target)
    {
        foreach (NameTarget name in EnumerateNameTargets([target]))
        {
            Declare(_scope, name.Name, new Symbol(SymbolKind.Variable, name), name);
        }
    }

    private void DeclarePatternDesignations(PatternSyntax pattern)
    {
        foreach (PatternDesignation designation in pattern.Designations)
        {
            CheckReservedName(designation.Name, pattern);
            _scope.Symbols[designation.Name] = new Symbol(SymbolKind.Variable, pattern);
        }
    }

    private void CheckReservedName(string name, object declarationNode)
    {
        if (name.StartsWith("__pscp", StringComparison.Ordinal) || name.StartsWith("__Pscp", StringComparison.Ordinal))
        {
            Report(
                DiagnosticCodes.ReservedIdentifier,
                $"`{name}` is reserved: names starting with `__pscp` or `__Pscp` belong to generated code.",
                Spans.GetName(declarationNode));
        }
    }

    // Functions in a cycle of the call graph need `rec`; `rec` on any other function is unnecessary (spec §10.3).
    private void ReportRecursion()
    {
        Dictionary<FunctionDeclaration, int> index = new(ReferenceEqualityComparer.Instance);
        Dictionary<FunctionDeclaration, int> lowLink = new(ReferenceEqualityComparer.Instance);
        HashSet<FunctionDeclaration> onStack = new(ReferenceEqualityComparer.Instance);
        Stack<FunctionDeclaration> stack = new();
        int next = 0;
        List<List<FunctionDeclaration>> components = [];

        void Connect(FunctionDeclaration function)
        {
            index[function] = next;
            lowLink[function] = next;
            next++;
            stack.Push(function);
            onStack.Add(function);
            foreach (FunctionDeclaration callee in _calls.TryGetValue(function, out List<FunctionDeclaration>? edges) ? edges : [])
            {
                if (!index.ContainsKey(callee))
                {
                    Connect(callee);
                    lowLink[function] = Math.Min(lowLink[function], lowLink[callee]);
                }
                else if (onStack.Contains(callee))
                {
                    lowLink[function] = Math.Min(lowLink[function], index[callee]);
                }
            }

            if (lowLink[function] == index[function])
            {
                List<FunctionDeclaration> component = [];
                FunctionDeclaration member;
                do
                {
                    member = stack.Pop();
                    onStack.Remove(member);
                    component.Add(member);
                }
                while (!ReferenceEquals(member, function));

                components.Add(component);
            }
        }

        foreach (FunctionDeclaration function in _statementFunctions)
        {
            if (!index.ContainsKey(function))
            {
                Connect(function);
            }
        }

        foreach (List<FunctionDeclaration> component in components)
        {
            bool recursive = component.Count > 1
                || (_calls.TryGetValue(component[0], out List<FunctionDeclaration>? selfEdges) && selfEdges.Any(edge => ReferenceEquals(edge, component[0])));
            foreach (FunctionDeclaration function in component)
            {
                if (recursive && !function.IsRecursive)
                {
                    string through = component.Count > 1
                        ? $" through {string.Join(", ", component.Where(other => !ReferenceEquals(other, function)).Select(other => $"`{other.Name}`"))}"
                        : string.Empty;
                    Report(
                        DiagnosticCodes.MissingRec,
                        $"`{function.Name}` calls itself{through}, so it must be declared `rec`: `rec {DisplayReturnType(function)} {function.Name}(...)`.",
                        Spans.GetName(function));
                }
                else if (!recursive && function.IsRecursive)
                {
                    Report(
                        DiagnosticCodes.UnnecessaryRec,
                        $"`{function.Name}` is marked `rec` but never calls itself. Remove `rec`.",
                        Spans.GetName(function),
                        DiagnosticSeverity.Warning);
                }
            }
        }
    }

    private static string DisplayReturnType(FunctionDeclaration function)
        => function.ReturnType is NamedTypeSyntax named ? named.Name : "T";

    private void ReportReadsBeforeInitialization()
    {
        foreach ((int statementIndex, FunctionDeclaration function, Expression call) in _topLevelCalls)
        {
            if (!_globalReads.TryGetValue(function, out HashSet<string>? reads))
            {
                continue;
            }

            foreach (string name in reads.Order(StringComparer.Ordinal))
            {
                if (_globalDeclarationIndex.TryGetValue(name, out int declarationIndex) && declarationIndex >= statementIndex)
                {
                    Report(
                        DiagnosticCodes.ReadBeforeInitialization,
                        $"`{function.Name}` reads `{name}`, whose declaration has not run yet at this call; it still has its default value.",
                        Spans.Get(call),
                        DiagnosticSeverity.Warning);
                }
            }
        }
    }

    // A top-level `let` (or immutable explicitly typed declaration) whose initializer is a compile-time constant.
    private static bool IsConstantDeclaration(DeclarationStatement declaration)
        => declaration.Mutability == MutabilityKind.Immutable
            && !declaration.IsInputShorthand
            && declaration.Targets.Count == 1
            && declaration.Targets[0] is NameTarget
            && declaration.Initializer is not null
            && (declaration.ExplicitType is null || declaration.ExplicitType is NamedTypeSyntax { TypeArguments.Count: 0, Name: "int" or "long" or "double" or "decimal" or "bool" or "char" or "string" })
            && PscpSyntaxFacts.IsConstantExpression(declaration.Initializer);

    private static IEnumerable<string> EnumerateTypeMemberNames(TypeDeclaration declaration)
    {
        foreach (string name in PscpSyntaxFacts.GetPrimaryConstructorParameterNames(declaration.HeaderText))
        {
            yield return name;
        }

        foreach (TypeMember member in declaration.Members)
        {
            switch (member)
            {
                case FieldMember field:
                    foreach (NameTarget target in EnumerateNameTargets(field.Declaration.Targets))
                    {
                        yield return target.Name;
                    }

                    break;
                case PropertyMember property:
                    yield return property.Name;
                    break;
                case MethodMember { IsConstructor: false } method:
                    yield return PscpIntrinsicCatalog.StripGenericSuffix(method.Name);
                    break;
                case OrderingShorthandMember:
                    yield return "CompareTo";
                    break;
            }
        }
    }

    private static IEnumerable<NameTarget> EnumerateNameTargets(IEnumerable<BindingTarget> targets)
    {
        foreach (BindingTarget target in targets)
        {
            switch (target)
            {
                case NameTarget name:
                    yield return name;
                    break;
                case TupleTarget tuple:
                    foreach (NameTarget nested in EnumerateNameTargets(tuple.Elements))
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }

    private void Report(string code, string message, TextSpan span, DiagnosticSeverity severity = DiagnosticSeverity.Error)
        => _diagnostics.Add(new Diagnostic(message, span, severity, code));
}

internal sealed record BindingResult(
    PscpProgram Program,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlySet<string> ConstantsUsedByTypes);
