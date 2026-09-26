namespace Pscp.Transpiler;

public sealed partial class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private readonly List<Diagnostic> _diagnostics = [];
    private readonly SyntaxSpans _spans = new();
    // `not x` expressions, told apart from `!x` for the `not a == b` warning (spec §13.7).
    private readonly HashSet<UnaryExpression> _notKeywordExpressions = new(ReferenceEqualityComparer.Instance);
    private int _position;
    // True while parsing the head of `if`/`while`/`for`: a `{` there starts the body, even after a pattern.
    private bool _inStatementHead;
    // Greater than zero directly inside an indexer, where `^k` is allowed.
    private int _indexArgumentDepth;

    public Parser(IReadOnlyList<Token> tokens)
    {
        _tokens = tokens;
    }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public SyntaxSpans Spans => _spans;

    public PscpProgram ParseProgram()
        => ParseProgramCore(stopAtCloseBrace: false);

    // Several declarations written as one statement (`int a = 1, b = 2`). Blocks and the program splice them into
    // their statement list; they never appear in the finished tree.
    private sealed record StatementGroup(IReadOnlyList<Statement> Statements) : Statement;

    private static void AddStatement(List<Statement> statements, Statement statement)
    {
        if (statement is StatementGroup group)
        {
            statements.AddRange(group.Statements);
        }
        else
        {
            statements.Add(statement);
        }
    }

    private PscpProgram ParseProgramCore(bool stopAtCloseBrace)
    {
        List<UsingDirective> usings = [];
        List<TypeDeclaration> types = [];
        List<FunctionDeclaration> functions = [];
        List<Statement> globalStatements = [];
        string? namespaceName = null;

        SkipSeparators();

        while (Current.Kind != TokenKind.EndOfFile && (!stopAtCloseBrace || Current.Kind != TokenKind.CloseBrace))
        {
            int before = _position;
            if (TryParseUsingDirective(out UsingDirective? usingDirective))
            {
                usings.Add(usingDirective!);
            }
            else if (TryParseNamespaceDeclaration(out string? declaredNamespace, out PscpProgram? nestedProgram))
            {
                string effectiveNamespace = declaredNamespace!;
                if (nestedProgram is not null)
                {
                    if (!string.IsNullOrWhiteSpace(nestedProgram.NamespaceName))
                    {
                        effectiveNamespace = effectiveNamespace + "." + nestedProgram.NamespaceName;
                    }

                    usings.AddRange(nestedProgram.Usings);
                    types.AddRange(nestedProgram.Types);
                    functions.AddRange(nestedProgram.Functions);
                    globalStatements.AddRange(nestedProgram.GlobalStatements);
                }

                namespaceName ??= effectiveNamespace;
            }
            else if (TryParseTypeDeclaration(out TypeDeclaration? typeDeclaration))
            {
                types.Add(typeDeclaration!);
            }
            else if (TryParseFunctionDeclaration(out FunctionDeclaration? function))
            {
                functions.Add(function!);
            }
            else
            {
                AddStatement(globalStatements, ParseStatement());
            }

            SkipSeparators();
            if (_position == before && Current.Kind != TokenKind.EndOfFile)
            {
                // Guarantees progress on input no rule accepts.
                AddDiagnostic(DiagnosticCodes.Syntax, $"Unexpected token '{DescribeToken(Current)}'.", Current.Span);
                Next();
            }
        }

        return new PscpProgram(usings, namespaceName, types, functions, globalStatements);
    }

    private bool TryParseUsingDirective(out UsingDirective? usingDirective)
    {
        int savedPosition = _position;
        usingDirective = null;
        if (Current.Kind != TokenKind.Using)
        {
            return false;
        }

        // `using var x = ...` and `using (...)` are statements, not directives (spec §6.3).
        if (Peek(1).Kind is TokenKind.Var or TokenKind.OpenParen)
        {
            return false;
        }

        Next();
        int end = _position;
        while (!IsStatementTerminator(Current.Kind))
        {
            end = ++_position;
        }

        usingDirective = Mark(new UsingDirective("using " + TokensToText(savedPosition + 1, end)), savedPosition);
        TryConsumeSemicolon();
        return true;
    }

    private bool TryParseNamespaceDeclaration(out string? namespaceName, out PscpProgram? nestedProgram)
    {
        int savedPosition = _position;
        int savedDiagnostics = _diagnostics.Count;
        namespaceName = null;
        nestedProgram = null;

        if (!Match(TokenKind.Namespace))
        {
            return false;
        }

        if (!TryParseIdentifier(out string? firstPart))
        {
            Restore(savedPosition, savedDiagnostics);
            return false;
        }

        namespaceName = firstPart;
        while (Match(TokenKind.Dot))
        {
            namespaceName += "." + Expect(TokenKind.Identifier, "Expected identifier after '.'.").Text;
        }

        SkipSeparators();
        if (Match(TokenKind.OpenBrace))
        {
            nestedProgram = ParseProgramCore(stopAtCloseBrace: true);
            Expect(TokenKind.CloseBrace, "Expected '}' after namespace body.");
            return true;
        }

        TryConsumeSemicolon();
        return true;
    }

    private Statement ParseStatement()
    {
        SkipSeparators();
        int start = _position;
        Statement statement = ParseStatementCore();
        return statement is StatementGroup ? statement : Mark(statement, start);
    }

    private Statement ParseStatementCore()
    {
        if (TryParseUnsupportedStatement(out Statement? unsupported))
        {
            return unsupported!;
        }

        if (TryParseFunctionDeclaration(out FunctionDeclaration? localFunction))
        {
            return new LocalFunctionStatement(localFunction!);
        }

        switch (Current.Kind)
        {
            case TokenKind.OpenBrace:
                return ParseBlockStatement();
            case TokenKind.If:
                return ParseIfLeadingStatement();
            case TokenKind.While:
                return ParseWhileStatement();
            case TokenKind.For:
                return ParseForStatement();
            case TokenKind.Return:
                return ParseReturnStatement();
            case TokenKind.Break:
                Next();
                TryConsumeSemicolon();
                return new BreakStatement();
            case TokenKind.Continue:
                Next();
                TryConsumeSemicolon();
                return new ContinueStatement();
            case TokenKind.Equal:
                return ParseOutputStatement(OutputKind.Write);
            case TokenKind.PlusEqual:
                return ParseOutputStatement(OutputKind.WriteLine);
            case TokenKind.Identifier when Current.Text == "try" && NextNonNewLineKind(1) == TokenKind.OpenBrace:
                return ParseTryStatement();
            case TokenKind.Identifier when Current.Text == "throw":
                return ParseThrowStatement();
            default:
                return ParseDeclarationAssignmentOrExpressionStatement();
        }
    }

    // C# statements PSCP does not accept (spec §6.3). Each gets an error naming the PSCP alternative; the
    // statement is skipped so parsing can continue.
    private bool TryParseUnsupportedStatement(out Statement? statement)
    {
        statement = null;
        string? message = null;
        Token token = Current;
        switch (Current.Kind)
        {
            case TokenKind.Do:
                message = "`do { ... } while (c)` is not supported because `do` is a PSCP keyword. Use `while true { ...; if not c then break }`.";
                break;
            case TokenKind.Using when Peek(1).Kind is TokenKind.Var or TokenKind.OpenParen:
                message = "`using` statements are not supported in PSCP programs.";
                break;
            case TokenKind.Using when _statementDepth > 0:
                message = "`using` directives are only allowed at the top of the program.";
                break;
            case TokenKind.Identifier:
                string text = Current.Text;
                TokenKind next = Peek(1).Kind;
                message = text switch
                {
                    "foreach" when next == TokenKind.OpenParen => "`foreach` is not supported. Use `for x in xs { ... }` or `xs -> x { ... }`.",
                    "switch" when next == TokenKind.OpenParen => "The `switch` statement is not supported. Use a switch expression (`x switch { ... }`) or an `if` / `else if` chain.",
                    "goto" => "`goto` is not supported. Split the code into functions or use `break` / `continue`.",
                    "lock" or "fixed" when next == TokenKind.OpenParen => $"`{text}` statements are not supported in PSCP programs.",
                    "unsafe" or "checked" or "unchecked" when next == TokenKind.OpenBrace => $"`{text}` blocks are not supported in PSCP programs.",
                    "yield" when next is TokenKind.Return or TokenKind.Break => "`yield` is not supported. Build the result with a collection expression or a generator `(src -> x do e)`.",
                    "await" or "async" => "`async` / `await` are not supported in PSCP programs.",
                    "case" or "default" when next == TokenKind.Colon => "`case` labels are not supported. Use a switch expression (`x switch { ... }`).",
                    _ when next == TokenKind.Colon && Peek(2).Kind != TokenKind.Colon && !IsSectionLabelStart()
                        => "Statement labels are not supported. Split the code into functions or use `break` / `continue`.",
                    _ => null,
                };
                break;
        }

        if (message is null)
        {
            return false;
        }

        AddDiagnostic(DiagnosticCodes.UnsupportedStatement, message, token.Span);
        SkipUnsupportedStatement();
        statement = new BlockStatement(Immutable.List<Statement>());
        return true;
    }

    private void SkipUnsupportedStatement()
    {
        int depth = 0;
        bool sawBlock = false;
        while (Current.Kind != TokenKind.EndOfFile)
        {
            if (depth == 0 && Current.Kind is TokenKind.NewLine or TokenKind.Semicolon)
            {
                if (!sawBlock || NextNonNewLineKind(0) != TokenKind.While)
                {
                    return;
                }
            }

            if (depth == 0 && Current.Kind == TokenKind.CloseBrace)
            {
                return;
            }

            if (Current.Kind is TokenKind.OpenParen or TokenKind.OpenBracket or TokenKind.OpenBrace)
            {
                depth++;
            }
            else if (Current.Kind is TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.CloseBrace)
            {
                depth--;
                if (depth == 0 && Current.Kind == TokenKind.CloseBrace)
                {
                    sawBlock = true;
                }
            }

            Next();
        }
    }

    private Statement ParseDeclarationAssignmentOrExpressionStatement()
    {
        if (TryParseDeclarationStatements(out List<DeclarationStatement>? declarations))
        {
            return declarations!.Count == 1 ? declarations[0] : new StatementGroup(declarations);
        }

        int start = _position;
        Expression expression = ParseExpression();

        if (Match(TokenKind.Arrow))
        {
            return ParseFastForStatement(expression, start);
        }

        bool hasStatementSemicolon = TryConsumeSemicolon();
        ExpectStatementEnd();
        return new ExpressionStatement(expression, hasStatementSemicolon);
    }

    // A statement ends at a line break, `;`, `}`, `else` (after a `then` branch) or the end of the file. After a
    // consumed `;` the next statement may follow on the same line: `c.hit("a"); c.hit("b")`.
    private void ExpectStatementEnd()
    {
        if (Current.Kind is TokenKind.NewLine or TokenKind.Semicolon or TokenKind.CloseBrace or TokenKind.EndOfFile or TokenKind.Else
            || (_position > 0 && _tokens[_position - 1].Kind == TokenKind.Semicolon))
        {
            return;
        }

        AddDiagnostic(DiagnosticCodes.Syntax, $"Unexpected token '{DescribeToken(Current)}' after the end of the statement.", Current.Span);
        while (Current.Kind is not (TokenKind.NewLine or TokenKind.Semicolon or TokenKind.CloseBrace or TokenKind.EndOfFile))
        {
            if (Current.Kind is TokenKind.OpenParen or TokenKind.OpenBracket)
            {
                int close = FindMatchingClose(_position);
                _position = close < 0 ? _tokens.Count - 1 : close;
            }

            Next();
        }
    }

    private int _statementDepth;

    private BlockStatement ParseBlockStatement()
    {
        int start = _position;
        Expect(TokenKind.OpenBrace, "Expected '{' to start a block.");
        List<Statement> statements = [];
        bool savedHead = _inStatementHead;
        int savedIndexDepth = _indexArgumentDepth;
        _inStatementHead = false;
        _indexArgumentDepth = 0;
        _statementDepth++;
        try
        {
            SkipSeparators();
            while (Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
            {
                int before = _position;
                AddStatement(statements, ParseStatement());
                SkipSeparators();
                if (_position == before)
                {
                    AddDiagnostic(DiagnosticCodes.Syntax, $"Unexpected token '{DescribeToken(Current)}'.", Current.Span);
                    Next();
                }
            }
        }
        finally
        {
            _statementDepth--;
            _inStatementHead = savedHead;
            _indexArgumentDepth = savedIndexDepth;
        }

        Expect(TokenKind.CloseBrace, "Expected '}' to close the block.");
        return Mark(new BlockStatement(statements), start);
    }

    private Expression ParseStatementHeadExpression()
    {
        bool saved = _inStatementHead;
        _inStatementHead = true;
        try
        {
            return ParseExpression();
        }
        finally
        {
            _inStatementHead = saved;
        }
    }

    private Statement ParseIfLeadingStatement()
    {
        Expect(TokenKind.If, "Expected 'if'.");
        Expression condition = ParseStatementHeadExpression();
        SkipNewLinesBefore(TokenKind.Then);

        if (Match(TokenKind.Then))
        {
            if (ShouldParseThenBranchAsStatement())
            {
                Statement statementThenBranch = ParseEmbeddedStatement();
                Statement? statementElseBranch = TryParseElseBranch();
                return new IfStatement(condition, statementThenBranch, statementElseBranch, true);
            }

            int thenStart = _position;
            Expression thenExpression = ParseExpression();
            SkipSeparators();
            Expect(TokenKind.Else, "Expected 'else' in one-line if expression.");
            SkipSeparators();
            if (StartsStatementOnlyForm(Current.Kind))
            {
                // `if c then f(x) else += y`: the else branch can only be a statement, so the whole form is one.
                Statement elseStatement = ParseEmbeddedStatement();
                return new IfStatement(condition, Mark(new ExpressionStatement(thenExpression, false), thenStart), elseStatement, true);
            }

            Expression elseExpression = ParseExpression();
            bool hasSemicolon = TryConsumeSemicolon();
            return new ExpressionStatement(new IfExpression(condition, thenExpression, elseExpression), hasSemicolon);
        }

        Statement thenBranch = ParseEmbeddedStatement();
        Statement? elseBranch = TryParseElseBranch();
        return new IfStatement(condition, thenBranch, elseBranch, false);
    }

    // `else` may start the next line (spec §4.2 rule 3).
    private Statement? TryParseElseBranch()
    {
        int beforeSeparators = _position;
        SkipSeparators();
        if (!Match(TokenKind.Else))
        {
            _position = beforeSeparators;
            return null;
        }

        return ParseEmbeddedStatement();
    }

    private Statement ParseWhileStatement()
    {
        Expect(TokenKind.While, "Expected 'while'.");
        Expression condition = ParseStatementHeadExpression();
        SkipNewLinesBefore(TokenKind.Do);

        if (Match(TokenKind.Do))
        {
            return new WhileStatement(condition, ParseEmbeddedStatement(), true);
        }

        return new WhileStatement(condition, ParseEmbeddedStatement(), false);
    }

    // `for x in src`, `for i, x in src`, `for (a, b) in src` and C-style `for (init; cond; step)`. A `for (` whose
    // parentheses hold only a binding list and are followed by `in` is the destructuring form (spec §12.4).
    private Statement ParseForStatement()
    {
        int start = _position;
        Expect(TokenKind.For, "Expected 'for'.");

        if (Current.Kind == TokenKind.OpenParen && !IsDestructuringForHead())
        {
            Next();
            int headerStart = _position;
            int close = FindMatchingClose(headerStart - 1);
            _position = close < 0 ? _tokens.Count - 1 : close;
            int headerEnd = _position;
            Expect(TokenKind.CloseParen, "Expected ')' after for header.");
            string headerText = TokensToText(headerStart, headerEnd);
            return new CStyleForStatement(headerText, ParseEmbeddedStatement());
        }

        (BindingTarget? indexTarget, BindingTarget itemTarget) = ParseIterationBinding();
        Expect(TokenKind.In, "Expected 'in' in for-in loop.");
        bool saved = _inStatementHead;
        _inStatementHead = true;
        Expression source;
        try
        {
            source = ParseExpression();
        }
        finally
        {
            _inStatementHead = saved;
        }

        SkipNewLinesBefore(TokenKind.Do);
        bool isDoForm = Match(TokenKind.Do);
        Statement body = ParseEmbeddedStatement();
        return indexTarget is null
            ? new ForInStatement(itemTarget, source, body, isDoForm)
            : Mark(new FastForStatement(source, indexTarget, itemTarget, body, isDoForm), start);
    }

    private bool IsDestructuringForHead()
    {
        int close = FindMatchingClose(_position);
        if (close < 0 || _tokens[Math.Min(close + 1, _tokens.Count - 1)].Kind != TokenKind.In)
        {
            return false;
        }

        for (int i = _position + 1; i < close; i++)
        {
            if (_tokens[i].Kind is not (TokenKind.Identifier or TokenKind.Comma or TokenKind.OpenParen or TokenKind.CloseParen or TokenKind.NewLine))
            {
                return false;
            }
        }

        return true;
    }

    private Statement ParseFastForStatement(Expression source, int start)
    {
        (BindingTarget? indexTarget, BindingTarget itemTarget) = ParseIterationBinding();
        bool isDoForm = Match(TokenKind.Do);
        return Mark(new FastForStatement(source, indexTarget, itemTarget, ParseEmbeddedStatement(), isDoForm), start);
    }

    // The body of `then`, `else`, `do` or a statement head: one statement, which may start on the next line.
    private Statement ParseEmbeddedStatement()
    {
        SkipSeparators();
        Statement statement = Current.Kind == TokenKind.OpenBrace ? ParseBlockStatement() : ParseStatement();
        return statement is StatementGroup group ? new BlockStatement(group.Statements) : statement;
    }

    private Statement ParseReturnStatement()
    {
        Expect(TokenKind.Return, "Expected 'return'.");

        if (IsStatementTerminator(Current.Kind) || Current.Kind == TokenKind.Else)
        {
            TryConsumeSemicolon();
            return new ReturnStatement(null);
        }

        Expression expression = ParseExpression();
        TryConsumeSemicolon();
        ExpectStatementEnd();
        return new ReturnStatement(expression);
    }

    private Statement ParseThrowStatement()
    {
        Next();
        if (IsStatementTerminator(Current.Kind) || Current.Kind == TokenKind.Else)
        {
            TryConsumeSemicolon();
            return new ThrowStatement(null);
        }

        Expression expression = ParseExpression();
        TryConsumeSemicolon();
        ExpectStatementEnd();
        return new ThrowStatement(expression);
    }

    private Statement ParseTryStatement()
    {
        Next();
        SkipSeparators();
        BlockStatement body = ParseBlockStatement();
        List<CatchClause> catches = [];
        BlockStatement? finallyBlock = null;
        while (true)
        {
            int beforeSeparators = _position;
            SkipSeparators();
            if (Current.Kind == TokenKind.Identifier && Current.Text == "catch")
            {
                int catchStart = _position;
                Next();
                TypeSyntax? type = null;
                string? name = null;
                if (Match(TokenKind.OpenParen))
                {
                    type = ParseTypeSyntax(allowSizedArrays: false);
                    if (Current.Kind == TokenKind.Identifier)
                    {
                        name = Next().Text;
                    }

                    Expect(TokenKind.CloseParen, "Expected ')' after the catch declaration.");
                }

                Expression? filter = null;
                if (Current.Kind == TokenKind.Identifier && Current.Text == "when")
                {
                    Next();
                    filter = ParseStatementHeadExpression();
                }

                SkipSeparators();
                catches.Add(Mark(new CatchClause(type, name, filter, ParseBlockStatement()), catchStart));
                continue;
            }

            if (Current.Kind == TokenKind.Identifier && Current.Text == "finally")
            {
                Next();
                SkipSeparators();
                finallyBlock = ParseBlockStatement();
                break;
            }

            _position = beforeSeparators;
            break;
        }

        if (catches.Count == 0 && finallyBlock is null)
        {
            AddDiagnostic(DiagnosticCodes.Syntax, "Expected `catch` or `finally` after the `try` block.", Current.Span);
        }

        return new TryStatement(body, catches, finallyBlock);
    }

    private OutputStatement ParseOutputStatement(OutputKind kind)
    {
        _ = kind switch
        {
            OutputKind.Write => Expect(TokenKind.Equal, "Expected '=' to start output statement."),
            _ => Expect(TokenKind.PlusEqual, "Expected '+=' to start line output statement."),
        };

        Expression expression = ParseExpression();
        TryConsumeSemicolon();
        ExpectStatementEnd();
        return new OutputStatement(kind, expression);
    }

    // `[rec] T name[<T>](params) [where ...] { ... }` or `... => expr` (spec §10.1).
    private bool TryParseFunctionDeclaration(out FunctionDeclaration? declaration)
    {
        int savedPosition = _position;
        int savedDiagnostics = _diagnostics.Count;
        declaration = null;

        bool isRecursive = Match(TokenKind.Rec);
        TypeSyntax? returnType = TryParseTypeSyntax(allowSizedArrays: false);
        if (returnType is null || Current.Kind != TokenKind.Identifier)
        {
            Restore(savedPosition, savedDiagnostics);
            return false;
        }

        Token nameToken = Next();
        string? typeParameterText = null;
        if (Current.Kind == TokenKind.LessThan && IsCurrentAdjacentToPreviousToken() && LooksLikeGenericArgumentSuffix())
        {
            typeParameterText = ParseGenericArgumentSuffixText();
        }

        if (Current.Kind != TokenKind.OpenParen)
        {
            Restore(savedPosition, savedDiagnostics);
            return false;
        }

        Next();
        IReadOnlyList<ParameterSyntax> parameters = ParseParameterListTail();
        string? constraintText = null;
        if (Current.Kind == TokenKind.Where)
        {
            int constraintStart = _position;
            while (Current.Kind is not (TokenKind.OpenBrace or TokenKind.FatArrow or TokenKind.NewLine or TokenKind.EndOfFile))
            {
                Next();
            }

            constraintText = TokensToText(constraintStart, _position);
        }

        int beforeBody = _position;
        SkipSeparators();
        BlockStatement body;
        if (Current.Kind == TokenKind.OpenBrace)
        {
            body = ParseBlockStatement();
        }
        else if (Current.Kind == TokenKind.FatArrow && _position == beforeBody)
        {
            int expressionStart = ++_position;
            SkipExpressionNewLines();
            Expression expression = WithNestedContext(ParseExpression);
            TryConsumeSemicolon();
            ExpressionStatement statement = Mark(new ExpressionStatement(expression, false), expressionStart);
            body = Mark(new BlockStatement(Immutable.List<Statement>(statement)), expressionStart);
        }
        else
        {
            Restore(savedPosition, savedDiagnostics);
            return false;
        }

        declaration = Mark(new FunctionDeclaration(isRecursive, returnType, nameToken.Text, parameters, body, typeParameterText, constraintText), savedPosition);
        _spans.SetName(declaration, nameToken.Span);
        return true;
    }

    private IReadOnlyList<ParameterSyntax> ParseParameterListTail()
    {
        List<ParameterSyntax> parameters = [];
        SkipSeparators();

        if (Match(TokenKind.CloseParen))
        {
            return parameters;
        }

        do
        {
            SkipSeparators();
            int start = _position;
            ArgumentModifier modifier = ParseOptionalArgumentModifier();
            if (Current.Kind == TokenKind.Identifier && Current.Text == "params")
            {
                Next();
            }

            TypeSyntax? parameterType = TryParseTypeSyntax(allowSizedArrays: false);
            if (parameterType is null)
            {
                AddDiagnostic(DiagnosticCodes.Syntax, "Expected parameter type.", Current.Span);
                parameterType = new NamedTypeSyntax("object", Immutable.List<TypeSyntax>());
            }

            BindingTarget target = ParseBindingTarget();
            if (Current.Kind == TokenKind.Equal)
            {
                // Default values are C# pass-through; the parameter keeps its declared type.
                Next();
                _ = ParseExpression();
            }

            parameters.Add(Mark(new ParameterSyntax(modifier, parameterType, target), start));
            SkipSeparators();
        }
        while (Match(TokenKind.Comma));

        Expect(TokenKind.CloseParen, "Expected ')' after function parameter list.");
        return parameters;
    }

    // Declarations (spec §9): `let x = e`, `var x = e`, `[mut] T x [= e]`, the input shorthand `[mut] T a, b =`,
    // destructuring `T a, b = e`, per-name initializers `T a = 1, b = 2` (one declaration each), and
    // `(T1 a, T2 b) = e`. `A b` (two names) is kept as a declaration; the binder turns it into a space-call
    // when `A` is callable (spec §11.5).
    private bool TryParseDeclarationStatements(out List<DeclarationStatement>? declarations)
    {
        int savedPosition = _position;
        int savedDiagnostics = _diagnostics.Count;
        declarations = null;

        MutabilityKind mutability;
        TypeSyntax? explicitType = null;
        List<BindingTarget>? preparsedTargets = null;
        Token? keyword = null;

        if (Current.Kind is TokenKind.Let or TokenKind.Var)
        {
            keyword = Next();
            mutability = keyword.Kind == TokenKind.Var ? MutabilityKind.Mutable : MutabilityKind.Immutable;
        }
        else
        {
            bool hadMut = Match(TokenKind.Mut);
            if (TryParseTypedTupleDeclarationHead(out TypeSyntax? tupleType, out List<BindingTarget>? tupleTargets))
            {
                explicitType = tupleType;
                preparsedTargets = tupleTargets;
            }
            else
            {
                explicitType = TryParseTypeSyntax(allowSizedArrays: true);
                if (explicitType is null)
                {
                    Restore(savedPosition, savedDiagnostics);
                    return false;
                }
            }

            mutability = hadMut ? MutabilityKind.Mutable : MutabilityKind.Immutable;
        }

        if (preparsedTargets is null && explicitType is not null && Current.Kind == TokenKind.OpenParen)
        {
            Restore(savedPosition, savedDiagnostics);
            return false;
        }

        List<BindingTarget> targets = preparsedTargets ?? [];
        if (preparsedTargets is null && !TryParseBindingTargetList(targets))
        {
            Restore(savedPosition, savedDiagnostics);
            return false;
        }

        if (Current.Kind == TokenKind.Equal)
        {
            Token equalToken = Next();
            if (IsStatementTerminator(Current.Kind))
            {
                if (explicitType is null)
                {
                    AddDiagnostic(
                        DiagnosticCodes.UntypedInput,
                        $"An input declaration needs a type: write `int {DescribeTargets(targets)} =` instead of `{keyword?.Text ?? "let"} {DescribeTargets(targets)} =`.",
                        equalToken.Span);
                }

                declarations = [Mark(new DeclarationStatement(mutability, explicitType, targets, null, true), savedPosition)];
                TryConsumeSemicolon();
                return true;
            }

            SkipExpressionNewLines();
            int initializerStart = _position;
            Expression initializer = ParseExpression();
            if (explicitType is not null && targets.Count == 1 && preparsedTargets is null && StartsPerNameDeclarator())
            {
                declarations = [Mark(new DeclarationStatement(mutability, explicitType, targets, initializer, false), savedPosition)];
                ParsePerNameDeclarators(mutability, explicitType, declarations);
                TryConsumeSemicolon();
                ExpectStatementEnd();
                return true;
            }

            if (Current.Kind == TokenKind.Comma)
            {
                initializer = Mark(ParseTupleContinuation(initializer), initializerStart);
            }

            declarations = [Mark(new DeclarationStatement(mutability, explicitType, targets, initializer, false), savedPosition)];
            TryConsumeSemicolon();
            ExpectStatementEnd();
            return true;
        }

        if (!IsStatementTerminator(Current.Kind))
        {
            Restore(savedPosition, savedDiagnostics);
            return false;
        }

        if (explicitType is NamedTypeSyntax named && named.Name.Contains('.', StringComparison.Ordinal) && named.TypeArguments.Count == 0)
        {
            Restore(savedPosition, savedDiagnostics);
            return false;
        }

        if (explicitType is null)
        {
            Restore(savedPosition, savedDiagnostics);
            return false;
        }

        declarations = [Mark(new DeclarationStatement(mutability, explicitType, targets, null, false), savedPosition)];
        TryConsumeSemicolon();
        return true;
    }

    private static string DescribeTargets(IReadOnlyList<BindingTarget> targets)
        => string.Join(", ", targets.Select(target => target switch
        {
            NameTarget name => name.Name,
            DiscardTarget => "_",
            _ => "x",
        }));

    // `, name =` or `, name` followed by `,` or the end of the statement.
    private bool StartsPerNameDeclarator()
        => Current.Kind == TokenKind.Comma
            && Peek(1).Kind == TokenKind.Identifier
            && (Peek(2).Kind is TokenKind.Equal or TokenKind.Comma || IsStatementTerminator(Peek(2).Kind));

    private void ParsePerNameDeclarators(MutabilityKind mutability, TypeSyntax explicitType, List<DeclarationStatement> declarations)
    {
        while (Match(TokenKind.Comma))
        {
            SkipExpressionNewLines();
            int start = _position;
            BindingTarget target = ParseBindingTarget();
            Expression? initializer = null;
            if (Match(TokenKind.Equal))
            {
                SkipExpressionNewLines();
                initializer = ParseExpression();
            }

            declarations.Add(Mark(new DeclarationStatement(mutability, explicitType, [target], initializer, false), start));
        }
    }

    // `(int x, int y) = e`. Mixing declared and existing names (`(int x, y) = e`) is an error (spec §9.5).
    private bool TryParseTypedTupleDeclarationHead(out TypeSyntax? explicitType, out List<BindingTarget>? targets)
    {
        int savedPosition = _position;
        int savedDiagnostics = _diagnostics.Count;
        explicitType = null;
        targets = null;

        if (!Match(TokenKind.OpenParen))
        {
            return false;
        }

        List<TypeSyntax> elementTypes = [];
        List<BindingTarget> elementTargets = [];
        bool sawUntyped = false;
        SkipSeparators();

        if (Match(TokenKind.CloseParen))
        {
            Restore(savedPosition, savedDiagnostics);
            return false;
        }

        do
        {
            SkipSeparators();
            int elementStart = _position;
            int elementDiagnostics = _diagnostics.Count;
            TypeSyntax? type = TryParseTypeSyntax(allowSizedArrays: false);
            if (type is not null && TryParseBindingTarget(out BindingTarget? target))
            {
                elementTypes.Add(type);
                elementTargets.Add(target!);
            }
            else
            {
                Restore(elementStart, elementDiagnostics);
                if (Current.Kind != TokenKind.Identifier || Peek(1).Kind is not (TokenKind.Comma or TokenKind.CloseParen))
                {
                    Restore(savedPosition, savedDiagnostics);
                    return false;
                }

                sawUntyped = true;
                Next();
            }

            SkipSeparators();
        }
        while (Match(TokenKind.Comma));

        if (!Match(TokenKind.CloseParen) || Current.Kind != TokenKind.Equal)
        {
            Restore(savedPosition, savedDiagnostics);
            return false;
        }

        if (sawUntyped)
        {
            if (elementTypes.Count == 0)
            {
                Restore(savedPosition, savedDiagnostics);
                return false;
            }

            AddDiagnostic(
                DiagnosticCodes.MixedDestructuring,
                "A destructuring cannot both declare new names and assign existing ones. Declare every name (`(int x, int y) = e`) or none (`(x, y) = e`).",
                new TextSpan(_tokens[savedPosition].Position, Math.Max(1, _tokens[_position - 1].Position + 1 - _tokens[savedPosition].Position)));
        }

        explicitType = new TupleTypeSyntax(elementTypes);
        targets = [new TupleTarget(elementTargets)];
        return true;
    }

    private Expression ParseTupleContinuation(Expression first)
    {
        if (!Match(TokenKind.Comma))
        {
            return first;
        }

        List<Expression> elements = [first];
        do
        {
            SkipSeparators();
            elements.Add(ParseExpression());
        }
        while (Match(TokenKind.Comma));

        return new TupleExpression(elements);
    }

    private bool TryParseBindingTargetList(List<BindingTarget> targets)
    {
        int savedPosition = _position;

        if (!TryParseBindingTarget(out BindingTarget? first))
        {
            _position = savedPosition;
            return false;
        }

        targets.Add(first!);

        while (Current.Kind == TokenKind.Comma && Peek(1).Kind is TokenKind.Identifier or TokenKind.OpenParen)
        {
            int beforeComma = _position;
            Next();
            if (!TryParseBindingTarget(out BindingTarget? next))
            {
                _position = beforeComma;
                break;
            }

            targets.Add(next!);
        }

        return true;
    }

    private bool TryParseTypeDeclaration(out TypeDeclaration? declaration)
    {
        int savedPosition = _position;
        int savedDiagnostics = _diagnostics.Count;
        declaration = null;

        _ = ParseModifiers();
        bool rawBody = false;

        if (Match(TokenKind.Class) || Match(TokenKind.Struct))
        {
        }
        else if (Match(TokenKind.Record))
        {
            _ = Match(TokenKind.Class) || Match(TokenKind.Struct);
        }
        else if (Current.Kind == TokenKind.Identifier && Current.Text is "interface" or "enum")
        {
            Next();
            rawBody = true;
        }
        else
        {
            Restore(savedPosition, savedDiagnostics);
            return false;
        }

        if (Current.Kind != TokenKind.Identifier)
        {
            Restore(savedPosition, savedDiagnostics);
            return false;
        }

        Token nameToken = Next();
        string name = nameToken.Text;
        int headerStart = savedPosition;

        while (Current.Kind != TokenKind.EndOfFile && Current.Kind != TokenKind.OpenBrace && !IsStatementTerminator(Current.Kind))
        {
            if (Current.Kind is TokenKind.OpenParen or TokenKind.OpenBracket)
            {
                int close = FindMatchingClose(_position);
                _position = close < 0 ? _tokens.Count - 1 : close;
            }

            Next();
        }

        int headerEnd = _position;
        string headerText = TokensToText(headerStart, headerEnd);

        SkipSeparators();
        if (Current.Kind == TokenKind.OpenBrace)
        {
            IReadOnlyList<TypeMember> members;
            if (rawBody)
            {
                int bodyStart = _position + 1;
                int close = FindMatchingClose(_position);
                _position = close < 0 ? _tokens.Count - 1 : close;
                members = Immutable.List<TypeMember>(new RawTypeMember(TokensToTextWithLineBreaks(bodyStart, _position)));
            }
            else
            {
                Next();
                members = ParseTypeMembers(name);
            }

            Expect(TokenKind.CloseBrace, "Expected '}' after type declaration.");
            declaration = Mark(new TypeDeclaration(headerText, name, members, HasBody: true), savedPosition);
            _spans.SetName(declaration, nameToken.Span);
            return true;
        }

        TryConsumeSemicolon();
        declaration = Mark(new TypeDeclaration(headerText, name, Immutable.List<TypeMember>(), HasBody: false), savedPosition);
        _spans.SetName(declaration, nameToken.Span);
        return true;
    }

    private IReadOnlyList<TypeMember> ParseTypeMembers(string declaringTypeName)
    {
        List<TypeMember> members = [];
        SkipSeparators();

        _statementDepth++;
        try
        {
            while (Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
            {
                int before = _position;
                if (TryParseTypeSectionLabel())
                {
                }
                else if (TryParseTypeDeclaration(out TypeDeclaration? nestedType))
                {
                    members.Add(new NestedTypeMember(nestedType!));
                }
                else if (TryParseTypeMember(declaringTypeName, members))
                {
                }
                else
                {
                    AddDiagnostic(DiagnosticCodes.Syntax, "Expected a type member declaration.", Current.Span);
                    Next();
                }

                SkipSeparators();
                if (_position == before)
                {
                    Next();
                }
            }
        }
        finally
        {
            _statementDepth--;
        }

        return members;
    }

    // `public:` section labels were removed in v0.6 and are an error in v0.7 (PSCP1105).
    private bool TryParseTypeSectionLabel()
    {
        if (!IsSectionLabelStart())
        {
            return false;
        }

        Token label = Next();
        AddDiagnostic(
            DiagnosticCodes.RemovedSectionLabel,
            $"Access section labels (`{label.Text}:`) are not supported. Write `{label.Text}` on each member instead; members are public by default.",
            new TextSpan(label.Position, label.Text.Length + 1));
        Expect(TokenKind.Colon, "Expected ':' after access section label.");
        return true;
    }

    private bool TryParseTypeMember(string declaringTypeName, List<TypeMember> members)
    {
        int savedPosition = _position;
        int savedDiagnostics = _diagnostics.Count;

        if (Current.Kind == TokenKind.Rec)
        {
            Token recToken = Next();
            AddDiagnostic(
                DiagnosticCodes.UnnecessaryRec,
                "`rec` has no effect on a type member: methods may call themselves without it.",
                recToken.Span,
                DiagnosticSeverity.Warning);
        }

        IReadOnlyList<string> modifiers = ParseModifiers();

        if (TryParseOrderingShorthand(out OrderingShorthandMember? ordering))
        {
            members.Add(Mark(ordering!, savedPosition));
            return true;
        }

        if (Current.Kind == TokenKind.Identifier && Current.Text == declaringTypeName && Peek(1).Kind == TokenKind.OpenParen)
        {
            Token constructorName = Next();
            Expect(TokenKind.OpenParen, "Expected '(' after constructor name.");
            IReadOnlyList<ParameterSyntax> parameters = ParseParameterListTail();
            SkipSeparators();
            string? initializerText = ParseOptionalConstructorInitializerText();
            SkipSeparators();
            MethodBody body = ParseMethodBody();
            MethodMember constructor = Mark(new MethodMember(modifiers, null, constructorName.Text, parameters, body, IsConstructor: true, initializerText), savedPosition);
            _spans.SetName(constructor, constructorName.Span);
            members.Add(constructor);
            return true;
        }

        int afterModifiers = _position;
        int afterModifiersDiagnostics = _diagnostics.Count;
        TypeSyntax? returnType = TryParseTypeSyntax(allowSizedArrays: false);
        if (returnType is not null
            && Current.Kind == TokenKind.Identifier
            && Current.Text == "operator")
        {
            Next();
            if (TryReadOperatorTokenText(out string? operatorTokenText))
            {
                Expect(TokenKind.OpenParen, "Expected '(' after operator token.");
                IReadOnlyList<ParameterSyntax> parameters = ParseOperatorParameterList(declaringTypeName);
                SkipSeparators();
                MethodBody body = ParseMethodBody();
                members.Add(Mark(new OperatorMember(modifiers, returnType, operatorTokenText!, parameters, body), savedPosition));
                return true;
            }
        }

        if (returnType is not null && Current.Kind == TokenKind.Identifier)
        {
            Token nameToken = Next();
            string? typeParameterText = null;
            if (Current.Kind == TokenKind.LessThan && IsCurrentAdjacentToPreviousToken() && LooksLikeGenericArgumentSuffix())
            {
                typeParameterText = ParseGenericArgumentSuffixText();
            }

            if (Match(TokenKind.OpenParen))
            {
                IReadOnlyList<ParameterSyntax> parameters = ParseParameterListTail();
                string? constraintText = null;
                if (Current.Kind == TokenKind.Where)
                {
                    int constraintStart = _position;
                    while (Current.Kind is not (TokenKind.OpenBrace or TokenKind.FatArrow or TokenKind.NewLine or TokenKind.EndOfFile))
                    {
                        Next();
                    }

                    constraintText = TokensToText(constraintStart, _position);
                }

                SkipSeparators();
                MethodBody body = ParseMethodBody();
                string methodName = typeParameterText is null ? nameToken.Text : nameToken.Text + typeParameterText;
                MethodMember method = Mark(new MethodMember(modifiers, returnType, methodName, parameters, body, IsConstructor: false, ConstraintText: constraintText), savedPosition);
                _spans.SetName(method, nameToken.Span);
                members.Add(method);
                return true;
            }

            if (typeParameterText is null)
            {
                int beforeBody = _position;
                SkipSeparators();
                if (Current.Kind is TokenKind.FatArrow || (Current.Kind == TokenKind.OpenBrace && LooksLikeAccessorBlock()))
                {
                    MethodBody body = ParseMethodBody();
                    PropertyMember property = Mark(new PropertyMember(modifiers, returnType, nameToken.Text, body), savedPosition);
                    _spans.SetName(property, nameToken.Span);
                    members.Add(property);
                    return true;
                }

                _position = beforeBody;
            }
        }

        Restore(afterModifiers, afterModifiersDiagnostics);
        if (TryParseDeclarationStatements(out List<DeclarationStatement>? declarations))
        {
            foreach (DeclarationStatement declaration in declarations!)
            {
                FieldMember field = new(modifiers, declaration);
                _spans.Copy(declaration, field);
                members.Add(field);
            }

            return true;
        }

        Restore(savedPosition, savedDiagnostics);
        return false;
    }

    // `{ get; set; }` or a getter body `{ ... }`: any block directly after a member name.
    private bool LooksLikeAccessorBlock()
        => Current.Kind == TokenKind.OpenBrace;

    private MethodBody ParseMethodBody()
    {
        SkipSeparators();
        if (Current.Kind == TokenKind.OpenBrace)
        {
            return new BlockMethodBody(ParseBlockStatement());
        }

        if (Match(TokenKind.FatArrow))
        {
            SkipSeparators();
            Expression expression = WithNestedContext(ParseExpression);
            TryConsumeSemicolon();
            return new ExpressionMethodBody(expression);
        }

        AddDiagnostic(DiagnosticCodes.Syntax, "Expected method body.", Current.Span);
        return new BlockMethodBody(new BlockStatement(Immutable.List<Statement>()));
    }

    private string? ParseOptionalConstructorInitializerText()
    {
        if (!Match(TokenKind.Colon))
        {
            return null;
        }

        int start = _position - 1;
        while (Current.Kind is not (TokenKind.EndOfFile or TokenKind.OpenBrace or TokenKind.NewLine or TokenKind.Semicolon or TokenKind.FatArrow))
        {
            if (Current.Kind is TokenKind.OpenParen or TokenKind.OpenBracket)
            {
                int close = FindMatchingClose(_position);
                _position = close < 0 ? _tokens.Count - 1 : close;
            }

            Next();
        }

        return TokensToText(start, _position);
    }

    private IReadOnlyList<ParameterSyntax> ParseOperatorParameterList(string declaringTypeName)
    {
        List<ParameterSyntax> parameters = [];
        SkipSeparators();
        if (Match(TokenKind.CloseParen))
        {
            return parameters;
        }

        do
        {
            SkipSeparators();
            int start = _position;
            ArgumentModifier modifier = ParseOptionalArgumentModifier();
            int savedPosition = _position;
            int savedDiagnostics = _diagnostics.Count;
            TypeSyntax? parameterType = TryParseTypeSyntax(allowSizedArrays: false);
            if (parameterType is not null && TryParseBindingTarget(out BindingTarget? typedTarget))
            {
                parameters.Add(Mark(new ParameterSyntax(modifier, parameterType, typedTarget!), start));
            }
            else
            {
                Restore(savedPosition, savedDiagnostics);
                BindingTarget target = ParseBindingTarget();
                parameters.Add(Mark(new ParameterSyntax(modifier, new NamedTypeSyntax(declaringTypeName, Immutable.List<TypeSyntax>()), target), start));
            }

            SkipSeparators();
        }
        while (Match(TokenKind.Comma));

        Expect(TokenKind.CloseParen, "Expected ')' after operator parameter list.");
        return parameters;
    }

    private bool TryReadOperatorTokenText(out string? operatorTokenText)
    {
        operatorTokenText = Current.Kind switch
        {
            TokenKind.Plus => "+",
            TokenKind.Minus => "-",
            TokenKind.Star => "*",
            TokenKind.Slash => "/",
            TokenKind.Percent => "%",
            TokenKind.EqualEqual => "==",
            TokenKind.BangEqual => "!=",
            TokenKind.LessThan => "<",
            TokenKind.LessEqual => "<=",
            TokenKind.GreaterThan => ">",
            TokenKind.GreaterEqual => ">=",
            TokenKind.Spaceship => "<=>",
            TokenKind.Bang => "!",
            TokenKind.Tilde => "~",
            TokenKind.PlusPlus => "++",
            TokenKind.MinusMinus => "--",
            TokenKind.Amp => "&",
            TokenKind.Pipe => "|",
            TokenKind.Caret => "^",
            TokenKind.True => "true",
            TokenKind.False => "false",
            _ => null,
        };

        if (operatorTokenText is null)
        {
            return false;
        }

        Next();
        return true;
    }

    // `operator<=>(other) => expr` or with a block body (spec §25.3).
    private bool TryParseOrderingShorthand(out OrderingShorthandMember? member)
    {
        member = null;

        if (Current.Kind != TokenKind.Identifier || Current.Text != "operator" || Peek(1).Kind != TokenKind.Spaceship)
        {
            return false;
        }

        Next();
        Expect(TokenKind.Spaceship, "Expected '<=>' after 'operator'.");
        Expect(TokenKind.OpenParen, "Expected '(' after 'operator<=>'.");
        List<string> parameterNames = [];
        SkipSeparators();
        if (!Match(TokenKind.CloseParen))
        {
            do
            {
                SkipSeparators();
                // `operator<=>(Job other)` may state the parameter type; it is always the declaring type.
                if (Current.Kind == TokenKind.Identifier && Peek(1).Kind == TokenKind.Identifier)
                {
                    Next();
                }

                parameterNames.Add(Expect(TokenKind.Identifier, "Expected parameter name in ordering shorthand.").Text);
                SkipSeparators();
            }
            while (Match(TokenKind.Comma));

            Expect(TokenKind.CloseParen, "Expected ')' after ordering shorthand parameter list.");
        }

        MethodBody body = ParseMethodBody();
        member = new OrderingShorthandMember(parameterNames, body);
        return true;
    }
}
