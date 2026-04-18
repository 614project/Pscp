namespace Pscp.Transpiler;

public sealed partial class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private readonly List<Diagnostic> _diagnostics = [];
    private int _position;

    public Parser(IReadOnlyList<Token> tokens)
    {
        _tokens = tokens;
    }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public PscpProgram ParseProgram()
        => ParseProgramCore(stopAtCloseBrace: false);

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
                globalStatements.Add(ParseStatement());
            }

            SkipSeparators();
        }

        return new PscpProgram(usings, namespaceName, types, functions, globalStatements);
    }

    private bool TryParseUsingDirective(out UsingDirective? usingDirective)
    {
        int savedPosition = _position;
        usingDirective = null;
        if (!Match(TokenKind.Using))
        {
            return false;
        }

        int end = _position;
        while (!IsStatementTerminator(Current.Kind))
        {
            end = ++_position;
        }

        usingDirective = new UsingDirective("using " + TokensToText(savedPosition + 1, end));
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

        if (TryParseFunctionDeclaration(out FunctionDeclaration? localFunction))
        {
            return new LocalFunctionStatement(localFunction!);
        }

        return Current.Kind switch
        {
            TokenKind.OpenBrace => ParseBlockStatement(),
            TokenKind.If => ParseIfLeadingStatement(),
            TokenKind.While => ParseWhileStatement(),
            TokenKind.For => ParseForStatement(),
            TokenKind.Return => ParseReturnStatement(),
            TokenKind.Break => ParseBreakStatement(),
            TokenKind.Continue => ParseContinueStatement(),
            TokenKind.Equal => ParseOutputStatement(OutputKind.Write),
            TokenKind.PlusEqual => ParseOutputStatement(OutputKind.WriteLine),
            _ => ParseDeclarationAssignmentOrExpressionStatement(),
        };
    }

    private Statement ParseDeclarationAssignmentOrExpressionStatement()
    {
        if (TryParseDeclarationStatement(out DeclarationStatement? declaration))
        {
            return declaration!;
        }

        Expression expression = ParseExpression();

        if (Match(TokenKind.Arrow))
        {
            return ParseFastForStatement(expression);
        }

        bool hasStatementSemicolon = TryConsumeSemicolon();
        return new ExpressionStatement(expression, hasStatementSemicolon);
    }

    private BlockStatement ParseBlockStatement()
    {
        Expect(TokenKind.OpenBrace, "Expected '{' to start a block.");
        List<Statement> statements = [];

        SkipSeparators();

        while (Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
        {
            statements.Add(ParseStatement());
            SkipSeparators();
        }

        Expect(TokenKind.CloseBrace, "Expected '}' to close the block.");
        return new BlockStatement(statements);
    }

    private Statement ParseIfLeadingStatement()
    {
        Expect(TokenKind.If, "Expected 'if'.");
        Expression condition = ParseExpression();

        if (Match(TokenKind.Then))
        {
            if (ShouldParseThenBranchAsStatement())
            {
                Statement statementThenBranch = ParseEmbeddedStatement();
                SkipSeparators();
                Statement? statementElseBranch = null;
                if (Match(TokenKind.Else))
                {
                    statementElseBranch = ParseEmbeddedStatement();
                }

                return new IfStatement(condition, statementThenBranch, statementElseBranch, true);
            }

            Expression thenExpression = ParseExpression();
            SkipSeparators();
            Expect(TokenKind.Else, "Expected 'else' in one-line if expression.");
            SkipSeparators();
            Expression elseExpression = ParseExpression();
            bool hasSemicolon = TryConsumeSemicolon();
            return new ExpressionStatement(
                new IfExpression(condition, thenExpression, elseExpression),
                hasSemicolon);
        }

        Statement thenBranch = ParseEmbeddedStatement();
        SkipSeparators();
        Statement? elseBranch = null;
        if (Match(TokenKind.Else))
        {
            elseBranch = ParseEmbeddedStatement();
        }

        return new IfStatement(condition, thenBranch, elseBranch, false);
    }

    private Statement ParseWhileStatement()
    {
        Expect(TokenKind.While, "Expected 'while'.");
        Expression condition = ParseExpression();

        if (Match(TokenKind.Do))
        {
            return new WhileStatement(condition, ParseStatement(), true);
        }

        return new WhileStatement(condition, ParseEmbeddedStatement(), false);
    }

    private Statement ParseForStatement()
    {
        Expect(TokenKind.For, "Expected 'for'.");

        if (Current.Kind == TokenKind.OpenParen)
        {
            Expect(TokenKind.OpenParen, "Expected '(' after for.");
            int start = _position;
            int depth = 1;
            while (depth > 0 && Current.Kind != TokenKind.EndOfFile)
            {
                if (Current.Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (Current.Kind == TokenKind.CloseParen)
                {
                    depth--;
                    if (depth == 0)
                    {
                        break;
                    }
                }

                Next();
            }

            int end = _position;
            Expect(TokenKind.CloseParen, "Expected ')' after for header.");
            string headerText = TokensToText(start, end);
            return new CStyleForStatement(headerText, ParseEmbeddedStatement());
        }

        BindingTarget iterator = ParseBindingTarget();
        Expect(TokenKind.In, "Expected 'in' in for-in loop.");
        Expression source = ParseExpression();

        if (Match(TokenKind.Do))
        {
            return new ForInStatement(iterator, source, ParseStatement(), true);
        }

        return new ForInStatement(iterator, source, ParseEmbeddedStatement(), false);
    }

    private Statement ParseFastForStatement(Expression source)
    {
        BindingTarget first = ParseBindingTarget();
        BindingTarget? indexTarget = null;
        BindingTarget itemTarget = first;

        if (Match(TokenKind.Comma))
        {
            indexTarget = first;
            itemTarget = ParseBindingTarget();
        }

        if (Match(TokenKind.Do))
        {
            return new FastForStatement(source, indexTarget, itemTarget, ParseStatement(), true);
        }

        return new FastForStatement(source, indexTarget, itemTarget, ParseEmbeddedStatement(), false);
    }

    private Statement ParseEmbeddedStatement()
    {
        SkipSeparators();
        return Current.Kind == TokenKind.OpenBrace ? ParseBlockStatement() : ParseStatement();
    }

    private Statement ParseReturnStatement()
    {
        Expect(TokenKind.Return, "Expected 'return'.");

        if (IsStatementTerminator(Current.Kind))
        {
            TryConsumeSemicolon();
            return new ReturnStatement(null);
        }

        Expression expression = ParseExpression();
        TryConsumeSemicolon();
        return new ReturnStatement(expression);
    }

    private Statement ParseBreakStatement()
    {
        Expect(TokenKind.Break, "Expected 'break'.");
        TryConsumeSemicolon();
        return new BreakStatement();
    }

    private Statement ParseContinueStatement()
    {
        Expect(TokenKind.Continue, "Expected 'continue'.");
        TryConsumeSemicolon();
        return new ContinueStatement();
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
        return new OutputStatement(kind, expression);
    }

    private bool TryParseFunctionDeclaration(out FunctionDeclaration? declaration)
    {
        int savedPosition = _position;
        int savedDiagnostics = _diagnostics.Count;
        declaration = null;

        bool isRecursive = Match(TokenKind.Rec);
        TypeSyntax? returnType = TryParseTypeSyntax(allowSizedArrays: false);
        if (returnType is null)
        {
            Restore(savedPosition, savedDiagnostics);
            return false;
        }

        if (!TryParseIdentifier(out string? name))
        {
            Restore(savedPosition, savedDiagnostics);
            return false;
        }

        if (!Match(TokenKind.OpenParen))
        {
            Restore(savedPosition, savedDiagnostics);
            return false;
        }

        IReadOnlyList<ParameterSyntax> parameters = ParseParameterListTail();
        SkipSeparators();
        if (Current.Kind != TokenKind.OpenBrace)
        {
            Restore(savedPosition, savedDiagnostics);
            return false;
        }

        BlockStatement body = ParseBlockStatement();
        declaration = new FunctionDeclaration(isRecursive, returnType, name!, parameters, body);
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
            ArgumentModifier modifier = ParseOptionalArgumentModifier();
            TypeSyntax? parameterType = TryParseTypeSyntax(allowSizedArrays: false);
            if (parameterType is null)
            {
                _diagnostics.Add(new Diagnostic("Expected parameter type.", Current.Span));
                parameterType = new NamedTypeSyntax("object", Immutable.List<TypeSyntax>());
            }

            BindingTarget target = ParseBindingTarget();
            parameters.Add(new ParameterSyntax(modifier, parameterType, target));
            SkipSeparators();
        }
        while (Match(TokenKind.Comma));

        Expect(TokenKind.CloseParen, "Expected ')' after function parameter list.");
        return parameters;
    }

    private bool TryParseDeclarationStatement(out DeclarationStatement? declaration)
    {
        int savedPosition = _position;
        int savedDiagnostics = _diagnostics.Count;
        declaration = null;

        MutabilityKind mutability = MutabilityKind.Immutable;
        TypeSyntax? explicitType = null;
        List<BindingTarget>? preparsedTargets = null;

        if (Match(TokenKind.Let))
        {
            explicitType = null;
            mutability = MutabilityKind.Immutable;
        }
        else if (Match(TokenKind.Var))
        {
            explicitType = null;
            mutability = MutabilityKind.Mutable;
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

        if (Match(TokenKind.Equal))
        {
            bool isInputShorthand = IsStatementTerminator(Current.Kind);
            if (isInputShorthand)
            {
                declaration = new DeclarationStatement(mutability, explicitType, targets, null, true);
                TryConsumeSemicolon();
                return true;
            }

            Expression initializer = TryParseMultiDeclaratorInitializer(explicitType, targets, out Expression? multiInitializer)
                ? multiInitializer!
                : ParseTupleAwareInitializer();
            declaration = new DeclarationStatement(mutability, explicitType, targets, initializer, false);
            TryConsumeSemicolon();
            return true;
        }

        if (!IsStatementTerminator(Current.Kind))
        {
            Restore(savedPosition, savedDiagnostics);
            return false;
        }

        if (declaration is null
            && explicitType is NamedTypeSyntax named
            && named.Name.Contains('.', StringComparison.Ordinal))
        {
            Restore(savedPosition, savedDiagnostics);
            return false;
        }

        declaration = new DeclarationStatement(mutability, explicitType, targets, null, false);
        TryConsumeSemicolon();
        return true;
    }

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
        SkipSeparators();

        if (Match(TokenKind.CloseParen))
        {
            Restore(savedPosition, savedDiagnostics);
            return false;
        }

        do
        {
            SkipSeparators();
            TypeSyntax? type = TryParseTypeSyntax(allowSizedArrays: false);
            if (type is null || !TryParseBindingTarget(out BindingTarget? target))
            {
                Restore(savedPosition, savedDiagnostics);
                return false;
            }

            elementTypes.Add(type);
            elementTargets.Add(target!);
            SkipSeparators();
        }
        while (Match(TokenKind.Comma));

        if (!Match(TokenKind.CloseParen))
        {
            Restore(savedPosition, savedDiagnostics);
            return false;
        }

        explicitType = new TupleTypeSyntax(elementTypes);
        targets = [new TupleTarget(elementTargets)];
        return true;
    }

    private bool TryParseMultiDeclaratorInitializer(TypeSyntax? explicitType, List<BindingTarget> targets, out Expression? initializer)
    {
        initializer = null;
        if (explicitType is null || targets.Count != 1)
        {
            return false;
        }

        Expression firstValue = ParseExpression();
        if (Current.Kind != TokenKind.Comma)
        {
            initializer = firstValue;
            return true;
        }

        int repeatSavedPosition = _position;
        int repeatSavedDiagnostics = _diagnostics.Count;
        List<BindingTarget> extraTargets = [];
        List<Expression> values = [firstValue];

        while (Match(TokenKind.Comma))
        {
            SkipSeparators();
            if (!TryParseBindingTarget(out BindingTarget? target) || !Match(TokenKind.Equal))
            {
                Restore(repeatSavedPosition, repeatSavedDiagnostics);
                initializer = ParseTupleContinuation(firstValue);
                return true;
            }

            SkipSeparators();
            extraTargets.Add(target!);
            values.Add(ParseExpression());
            SkipSeparators();
        }

        if (extraTargets.Count == 0)
        {
            Restore(repeatSavedPosition, repeatSavedDiagnostics);
            initializer = ParseTupleContinuation(firstValue);
            return true;
        }

        targets.AddRange(extraTargets);
        initializer = new TupleExpression(values);
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
            SkipSeparators();
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

        while (Match(TokenKind.Comma))
        {
            if (!TryParseBindingTarget(out BindingTarget? next))
            {
                _position = savedPosition;
                targets.Clear();
                return false;
            }

            targets.Add(next!);
        }

        return true;
    }

    private Expression ParseTupleAwareInitializer()
    {
        Expression first = ParseExpression();
        return ParseTupleContinuation(first);
    }

    private bool TryParseTypeDeclaration(out TypeDeclaration? declaration)
    {
        int savedPosition = _position;
        int savedDiagnostics = _diagnostics.Count;
        declaration = null;

        IReadOnlyList<string> modifiers = ParseModifiers();
        Token kindToken;
        bool recordStruct = false;

        if (Match(TokenKind.Class))
        {
            kindToken = _tokens[_position - 1];
        }
        else if (Match(TokenKind.Struct))
        {
            kindToken = _tokens[_position - 1];
        }
        else if (Match(TokenKind.Record))
        {
            kindToken = _tokens[_position - 1];
            recordStruct = Match(TokenKind.Struct);
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

        string name = Next().Text;
        int headerStart = savedPosition;

        while (Current.Kind != TokenKind.EndOfFile && Current.Kind != TokenKind.OpenBrace && !IsStatementTerminator(Current.Kind))
        {
            Next();
        }

        int headerEnd = _position;
        string headerText = TokensToText(headerStart, headerEnd);

        SkipSeparators();
        if (Match(TokenKind.OpenBrace))
        {
            IReadOnlyList<TypeMember> members = ParseTypeMembers(name);
            Expect(TokenKind.CloseBrace, "Expected '}' after type declaration.");
            declaration = new TypeDeclaration(headerText, name, members, HasBody: true);
            return true;
        }

        TryConsumeSemicolon();
        declaration = new TypeDeclaration(headerText, name, Immutable.List<TypeMember>(), HasBody: false);
        return true;
    }

    private IReadOnlyList<TypeMember> ParseTypeMembers(string declaringTypeName)
    {
        List<TypeMember> members = [];
        string? sectionAccess = null;
        SkipSeparators();

        while (Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
        {
            if (TryParseTypeSectionLabel(out string? nextSectionAccess))
            {
                sectionAccess = nextSectionAccess;
            }
            else if (TryParseTypeDeclaration(out TypeDeclaration? nestedType))
            {
                members.Add(new NestedTypeMember(nestedType!));
            }
            else if (TryParseTypeMember(declaringTypeName, sectionAccess, out TypeMember? member))
            {
                members.Add(member!);
            }
            else
            {
                _diagnostics.Add(new Diagnostic("Expected a type member declaration.", Current.Span));
                Next();
            }

            SkipSeparators();
        }

        return members;
    }

    private bool TryParseTypeSectionLabel(out string? accessModifier)
    {
        accessModifier = null;
        if (!IsSectionLabelStart())
        {
            return false;
        }

        accessModifier = Next().Text;
        _diagnostics.Add(new Diagnostic(
            $"Access section label syntax `{accessModifier}:` is removed in v0.5; prefer ordinary modifiers such as `{accessModifier}` on each member.",
            Current.Span,
            DiagnosticSeverity.Warning));
        Expect(TokenKind.Colon, "Expected ':' after access section label.");
        return true;
    }

    private bool TryParseTypeMember(string declaringTypeName, string? sectionAccess, out TypeMember? member)
    {
        int savedPosition = _position;
        int savedDiagnostics = _diagnostics.Count;
        member = null;

        _ = Match(TokenKind.Rec);
        IReadOnlyList<string> modifiers = ParseModifiers();
        IReadOnlyList<string> effectiveModifiers = ApplySectionAccess(modifiers, sectionAccess);

        if (TryParseOrderingShorthand(out OrderingShorthandMember? ordering))
        {
            member = ordering!;
            return true;
        }

        if (Current.Kind == TokenKind.Identifier && Current.Text == declaringTypeName && Peek(1).Kind == TokenKind.OpenParen)
        {
            string name = Next().Text;
            Expect(TokenKind.OpenParen, "Expected '(' after constructor name.");
            IReadOnlyList<ParameterSyntax> parameters = ParseParameterListTail();
            SkipSeparators();
            string? initializerText = ParseOptionalConstructorInitializerText();
            SkipSeparators();
            MethodBody body = ParseMethodBody();
            member = new MethodMember(effectiveModifiers, null, name, parameters, body, IsConstructor: true, initializerText);
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
                member = new OperatorMember(effectiveModifiers, returnType, operatorTokenText!, parameters, body);
                return true;
            }
        }

        if (returnType is not null && TryParseIdentifier(out string? nameToken))
        {
            if (Match(TokenKind.OpenParen))
            {
                IReadOnlyList<ParameterSyntax> parameters = ParseParameterListTail();
                SkipSeparators();
                MethodBody body = ParseMethodBody();
                member = new MethodMember(effectiveModifiers, returnType, nameToken!, parameters, body, IsConstructor: false);
                return true;
            }

            SkipSeparators();
            if (Current.Kind is TokenKind.FatArrow or TokenKind.OpenBrace)
            {
                MethodBody body = ParseMethodBody();
                member = new PropertyMember(effectiveModifiers, returnType, nameToken!, body);
                return true;
            }
        }

        Restore(afterModifiers, afterModifiersDiagnostics);
        if (TryParseDeclarationStatement(out DeclarationStatement? declaration))
        {
            member = new FieldMember(effectiveModifiers, declaration!);
            return true;
        }

        Restore(savedPosition, savedDiagnostics);
        return false;
    }

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
            Expression expression = ParseExpression();
            TryConsumeSemicolon();
            return new ExpressionMethodBody(expression);
        }

        _diagnostics.Add(new Diagnostic("Expected method body.", Current.Span));
        return new BlockMethodBody(new BlockStatement(Immutable.List<Statement>()));
    }

    private string? ParseOptionalConstructorInitializerText()
    {
        if (!Match(TokenKind.Colon))
        {
            return null;
        }

        int start = _position - 1;
        int parenDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;
        while (Current.Kind != TokenKind.EndOfFile)
        {
            if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0
                && Current.Kind is TokenKind.OpenBrace or TokenKind.NewLine or TokenKind.Semicolon)
            {
                break;
            }

            switch (Current.Kind)
            {
                case TokenKind.OpenParen:
                    parenDepth++;
                    break;
                case TokenKind.CloseParen:
                    if (parenDepth > 0)
                    {
                        parenDepth--;
                    }
                    break;
                case TokenKind.OpenBracket:
                    bracketDepth++;
                    break;
                case TokenKind.CloseBracket:
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }
                    break;
                case TokenKind.OpenBrace:
                    braceDepth++;
                    break;
                case TokenKind.CloseBrace:
                    if (braceDepth > 0)
                    {
                        braceDepth--;
                    }
                    break;
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
            ArgumentModifier modifier = ParseOptionalArgumentModifier();
            int savedPosition = _position;
            int savedDiagnostics = _diagnostics.Count;
            TypeSyntax? parameterType = TryParseTypeSyntax(allowSizedArrays: false);
            if (parameterType is not null && TryParseBindingTarget(out BindingTarget? typedTarget))
            {
                parameters.Add(new ParameterSyntax(modifier, parameterType, typedTarget!));
            }
            else
            {
                Restore(savedPosition, savedDiagnostics);
                BindingTarget target = ParseBindingTarget();
                parameters.Add(new ParameterSyntax(
                    modifier,
                    new NamedTypeSyntax(declaringTypeName, Immutable.List<TypeSyntax>()),
                    target));
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
            _ => null,
        };

        if (operatorTokenText is null)
        {
            return false;
        }

        Next();
        return true;
    }

    private bool TryParseOrderingShorthand(out OrderingShorthandMember? member)
    {
        int savedPosition = _position;
        int savedDiagnostics = _diagnostics.Count;
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

    private static IReadOnlyList<string> ApplySectionAccess(IReadOnlyList<string> modifiers, string? sectionAccess)
    {
        if (string.IsNullOrWhiteSpace(sectionAccess)
            || modifiers.Any(modifier => modifier is "public" or "private" or "protected" or "internal"))
        {
            return modifiers;
        }

        return [sectionAccess, .. modifiers];
    }
}


