namespace Pscp.Transpiler;

public sealed partial class Parser
{
    private Expression ParseExpression()
        => ParseAssignment();

    private Expression ParseAssignment()
    {
        Expression target = ParseConditional();
        if (Current.Kind is not TokenKind.Equal
            and not TokenKind.PlusEqual
            and not TokenKind.MinusEqual
            and not TokenKind.StarEqual
            and not TokenKind.SlashEqual
            and not TokenKind.PercentEqual
            and not TokenKind.ColonEqual)
        {
            return target;
        }

        TokenKind operatorKind = Next().Kind;
        Expression value = ParseAssignment();
        AssignmentOperator op = operatorKind switch
        {
            TokenKind.PlusEqual => AssignmentOperator.AddAssign,
            TokenKind.MinusEqual => AssignmentOperator.SubtractAssign,
            TokenKind.StarEqual => AssignmentOperator.MultiplyAssign,
            TokenKind.SlashEqual => AssignmentOperator.DivideAssign,
            TokenKind.PercentEqual => AssignmentOperator.ModuloAssign,
            _ => AssignmentOperator.Assign,
        };

        return new AssignmentExpression(target, op, value, operatorKind == TokenKind.ColonEqual);
    }

    private Expression ParseConditional()
    {
        Expression condition = ParsePipe();
        if (!Match(TokenKind.Question))
        {
            return condition;
        }

        Expression whenTrue = ParseExpression();
        Expect(TokenKind.Colon, "Expected ':' in conditional expression.");
        Expression whenFalse = ParseConditional();
        return new ConditionalExpression(condition, whenTrue, whenFalse);
    }

    private Expression ParsePipe()
    {
        Expression expression = ParseOr();

        while (Current.Kind is TokenKind.PipeGreater or TokenKind.LessPipe)
        {
            TokenKind kind = Next().Kind;
            Expression right = ParseOr();
            expression = new BinaryExpression(
                expression,
                kind == TokenKind.PipeGreater ? BinaryOperator.PipeRight : BinaryOperator.PipeLeft,
                right);
        }

        return expression;
    }

    private Expression ParseOr()
    {
        Expression expression = ParseXor();
        while (Current.Kind is TokenKind.PipePipe or TokenKind.Or)
        {
            Next();
            expression = new BinaryExpression(expression, BinaryOperator.LogicalOr, ParseXor());
        }

        return expression;
    }

    private Expression ParseXor()
    {
        Expression expression = ParseAnd();
        while (Current.Kind is TokenKind.Caret or TokenKind.Xor)
        {
            Next();
            expression = new BinaryExpression(expression, BinaryOperator.LogicalXor, ParseAnd());
        }

        return expression;
    }

    private Expression ParseAnd()
    {
        Expression expression = ParseEquality();
        while (Current.Kind is TokenKind.AmpAmp or TokenKind.And)
        {
            Next();
            expression = new BinaryExpression(expression, BinaryOperator.LogicalAnd, ParseEquality());
        }

        return expression;
    }

    private Expression ParseEquality()
    {
        Expression expression = ParseComparison();
        while (Current.Kind is TokenKind.EqualEqual or TokenKind.BangEqual)
        {
            TokenKind kind = Next().Kind;
            expression = new BinaryExpression(
                expression,
                kind == TokenKind.EqualEqual ? BinaryOperator.Equal : BinaryOperator.NotEqual,
                ParseComparison());
        }

        return expression;
    }

    private Expression ParseComparison()
    {
        Expression expression = ParseRange();
        while (true)
        {
            if (Current.Kind is TokenKind.LessThan or TokenKind.LessEqual or TokenKind.GreaterThan or TokenKind.GreaterEqual or TokenKind.Spaceship)
            {
                TokenKind kind = Next().Kind;
                BinaryOperator op = kind switch
                {
                    TokenKind.LessThan => BinaryOperator.LessThan,
                    TokenKind.LessEqual => BinaryOperator.LessThanOrEqual,
                    TokenKind.GreaterThan => BinaryOperator.GreaterThan,
                    TokenKind.GreaterEqual => BinaryOperator.GreaterThanOrEqual,
                    _ => BinaryOperator.Spaceship,
                };

                expression = new BinaryExpression(expression, op, ParseRange());
                continue;
            }

            if (Match(TokenKind.Is))
            {
                bool negated = Match(TokenKind.Not);
                expression = new IsPatternExpression(expression, ParseIsPattern(), negated);
                continue;
            }

            break;
        }

        return expression;
    }

    private IsPatternSyntax ParseIsPattern()
    {
        int savedPosition = _position;
        int savedDiagnostics = _diagnostics.Count;
        TypeSyntax? type = TryParseTypeSyntax(allowSizedArrays: false);
        if (type is not null)
        {
            return new TypePatternSyntax(type);
        }

        Restore(savedPosition, savedDiagnostics);
        return new ConstantPatternSyntax(ParseAdditive());
    }

    private Expression ParseRange()
    {
        Expression start = ParseAdditive();
        if (Current.Kind is not TokenKind.DotDot and not TokenKind.DotDotLess and not TokenKind.DotDotEqual)
        {
            return start;
        }

        TokenKind rangeToken = Next().Kind;
        Expression middle = ParseAdditive();

        if (rangeToken == TokenKind.DotDot && Match(TokenKind.DotDot))
        {
            Expression end = ParseAdditive();
            return new RangeExpression(start, middle, end, RangeKind.Inclusive);
        }

        RangeKind kind = rangeToken switch
        {
            TokenKind.DotDotLess => RangeKind.RightExclusive,
            TokenKind.DotDotEqual => RangeKind.ExplicitInclusive,
            _ => RangeKind.Inclusive,
        };

        return new RangeExpression(start, null, middle, kind);
    }

    private Expression ParseAdditive()
    {
        Expression expression = ParseMultiplicative();
        while (Current.Kind is TokenKind.Plus or TokenKind.Minus)
        {
            TokenKind kind = Next().Kind;
            expression = new BinaryExpression(
                expression,
                kind == TokenKind.Plus ? BinaryOperator.Add : BinaryOperator.Subtract,
                ParseMultiplicative());
        }

        return expression;
    }

    private Expression ParseMultiplicative()
    {
        Expression expression = ParseUnary();
        while (Current.Kind is TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
        {
            TokenKind kind = Next().Kind;
            BinaryOperator op = kind switch
            {
                TokenKind.Star => BinaryOperator.Multiply,
                TokenKind.Slash => BinaryOperator.Divide,
                _ => BinaryOperator.Modulo,
            };

            expression = new BinaryExpression(expression, op, ParseUnary());
        }

        return expression;
    }

    private Expression ParseUnary()
    {
        if (Current.Kind is TokenKind.PlusPlus or TokenKind.MinusMinus)
        {
            TokenKind kind = Next().Kind;
            return new PrefixExpression(
                kind == TokenKind.PlusPlus ? PostfixOperator.Increment : PostfixOperator.Decrement,
                ParseUnary());
        }

        if (Current.Kind is TokenKind.Plus or TokenKind.Minus or TokenKind.Bang or TokenKind.Not or TokenKind.Tilde)
        {
            TokenKind kind = Next().Kind;
            UnaryOperator op = kind switch
            {
                TokenKind.Plus => UnaryOperator.Plus,
                TokenKind.Minus => UnaryOperator.Negate,
                TokenKind.Tilde => UnaryOperator.Peek,
                _ => UnaryOperator.LogicalNot,
            };

            return new UnaryExpression(op, ParseUnary());
        }

        return ParsePostfix();
    }

    private Expression ParsePostfix()
    {
        Expression expression = ParsePrimary();

        while (true)
        {
            if (Match(TokenKind.OpenParen))
            {
                expression = new CallExpression(expression, ParseCallArgumentsTail(), false);
                continue;
            }

            if (Current.Kind == TokenKind.OpenBracket && IsCurrentAdjacentToPreviousToken() && Match(TokenKind.OpenBracket))
            {
                expression = new IndexExpression(expression, ParseIndexArgumentsTail());
                continue;
            }

            if (Current.Kind == TokenKind.Dot && IsCurrentAdjacentToPreviousToken() && Match(TokenKind.Dot))
            {
                if (Current.Kind == TokenKind.IntegerLiteral)
                {
                    int position = int.TryParse(Current.Text.TrimEnd('L', 'l'), out int value) ? value : 1;
                    Next();
                    expression = new TupleProjectionExpression(expression, position);
                }
                else
                {
                    string memberName = ParseIdentifierWithOptionalGenericSuffix();
                    expression = new MemberAccessExpression(expression, memberName);
                }

                continue;
            }

            if (Current.Kind == TokenKind.PlusPlus && IsCurrentAdjacentToPreviousToken() && Match(TokenKind.PlusPlus))
            {
                expression = new PostfixExpression(expression, PostfixOperator.Increment);
                continue;
            }

            if (Current.Kind == TokenKind.MinusMinus && IsCurrentAdjacentToPreviousToken() && Match(TokenKind.MinusMinus))
            {
                expression = new PostfixExpression(expression, PostfixOperator.Decrement);
                continue;
            }

            if (CanContinueSpaceCall(expression) && CanStartSpaceArgument(Current.Kind))
            {
                ArgumentSyntax argument = ParseCallArgument(allowNamed: false, allowComplexExpression: false);
                expression = AppendSpaceCallArgument(expression, argument);
                continue;
            }

            break;
        }

        return expression;
    }

    private Expression ParsePrimary()
    {
        switch (Current.Kind)
        {
            case TokenKind.IntegerLiteral:
                return new LiteralExpression(LiteralKind.Integer, Next().Text);
            case TokenKind.FloatLiteral:
                return new LiteralExpression(LiteralKind.Float, Next().Text);
            case TokenKind.StringLiteral:
                return new LiteralExpression(LiteralKind.String, Next().Text);
            case TokenKind.InterpolatedStringLiteral:
                return ParseInterpolatedStringExpression();
            case TokenKind.CharLiteral:
                return new LiteralExpression(LiteralKind.Char, Next().Text);
            case TokenKind.True:
                return new LiteralExpression(LiteralKind.True, Next().Text);
            case TokenKind.False:
                return new LiteralExpression(LiteralKind.False, Next().Text);
            case TokenKind.Null:
                return new LiteralExpression(LiteralKind.Null, Next().Text);
            case TokenKind.OpenParen:
                if (LooksLikeParenthesizedLambda())
                {
                    return ParseParenthesizedLambda();
                }

                return ParseParenthesizedOrTuple();
            case TokenKind.OpenBrace:
                return new BlockExpression(ParseBlockStatement());
            case TokenKind.OpenBracket:
                return ParseCollectionExpression();
            case TokenKind.If:
                return ParseIfExpression();
            case TokenKind.New:
                return ParseNewExpression();
            case TokenKind.Identifier:
                if (Current.Text == "_" && Peek(1).Kind == TokenKind.FatArrow)
                {
                    return ParseSingleParameterLambda();
                }

                if (Current.Text == "_")
                {
                    Token discard = Next();
                    if (Current.Kind is not TokenKind.Equal
                        and not TokenKind.PlusEqual
                        and not TokenKind.MinusEqual
                        and not TokenKind.StarEqual
                        and not TokenKind.SlashEqual
                        and not TokenKind.PercentEqual
                        and not TokenKind.ColonEqual)
                    {
                        _diagnostics.Add(new Diagnostic("Discard '_' cannot be used as a value.", discard.Span));
                    }

                    return new DiscardExpression();
                }

                if (Peek(1).Kind == TokenKind.FatArrow)
                {
                    return ParseSingleParameterLambda();
                }

                return ParseIdentifierBasedPrimary();
            default:
                _diagnostics.Add(new Diagnostic($"Unexpected token '{Current.Text}' in expression.", Current.Span));
                return new IdentifierExpression(Next().Text);
        }
    }

    private Expression ParseIdentifierBasedPrimary()
    {
        string name = ParseIdentifierWithOptionalGenericSuffix();

        if (Current.Kind == TokenKind.OpenBrace && Peek(1).Kind == TokenKind.For)
        {
            return ParseAggregationExpression(name);
        }

        return new IdentifierExpression(name);
    }

    private Expression ParseSingleParameterLambda()
    {
        BindingTarget parameter = ParseBindingTarget();
        Expect(TokenKind.FatArrow, "Expected '=>' in lambda expression.");
        LambdaBody body = Current.Kind == TokenKind.OpenBrace
            ? new LambdaBlockBody(ParseBlockStatement())
            : new LambdaExpressionBody(ParseExpression());
        return new LambdaExpression(Immutable.List(new LambdaParameter(ArgumentModifier.None, null, parameter)), body);
    }

    private Expression ParseParenthesizedLambda()
    {
        Expect(TokenKind.OpenParen, "Expected '(' to start lambda parameter list.");
        List<LambdaParameter> parameters = [];

        if (!Match(TokenKind.CloseParen))
        {
            do
            {
                parameters.Add(ParseLambdaParameter());
            }
            while (Match(TokenKind.Comma));

            Expect(TokenKind.CloseParen, "Expected ')' after lambda parameter list.");
        }

        Expect(TokenKind.FatArrow, "Expected '=>' in lambda expression.");
        LambdaBody body = Current.Kind == TokenKind.OpenBrace
            ? new LambdaBlockBody(ParseBlockStatement())
            : new LambdaExpressionBody(ParseExpression());

        return new LambdaExpression(parameters, body);
    }

    private LambdaParameter ParseLambdaParameter()
    {
        int savedPosition = _position;
        int savedDiagnostics = _diagnostics.Count;
        ArgumentModifier modifier = ParseOptionalArgumentModifier();
        TypeSyntax? type = TryParseTypeSyntax(allowSizedArrays: false);
        if (type is not null && TryParseBindingTarget(out BindingTarget? typedTarget))
        {
            return new LambdaParameter(modifier, type, typedTarget!);
        }

        Restore(savedPosition, savedDiagnostics);
        return new LambdaParameter(ArgumentModifier.None, null, ParseBindingTarget());
    }

    private Expression ParseParenthesizedOrTuple()
    {
        Expect(TokenKind.OpenParen, "Expected '('.");
        if (Current.Kind == TokenKind.For)
        {
            Expression generator = ParseForGeneratorExpression();
            Expect(TokenKind.CloseParen, "Expected ')' after generator expression.");
            return generator;
        }

        Expression first = ParseExpression();
        if (Match(TokenKind.Arrow))
        {
            Expression generator = ParseArrowGeneratorExpression(first);
            Expect(TokenKind.CloseParen, "Expected ')' after generator expression.");
            return generator;
        }

        if (!Match(TokenKind.Comma))
        {
            Expect(TokenKind.CloseParen, "Expected ')' after parenthesized expression.");
            return first;
        }

        List<Expression> elements = [first];
        do
        {
            elements.Add(ParseExpression());
        }
        while (Match(TokenKind.Comma));

        Expect(TokenKind.CloseParen, "Expected ')' after tuple expression.");
        return new TupleExpression(elements);
    }

    private GeneratorExpression ParseArrowGeneratorExpression(Expression source)
    {
        BindingTarget first = ParseBindingTarget();
        BindingTarget? indexTarget = null;
        BindingTarget itemTarget = first;
        if (Match(TokenKind.Comma))
        {
            indexTarget = first;
            itemTarget = ParseBindingTarget();
        }

        LambdaBody body = ParseGeneratorBody();
        return new GeneratorExpression(indexTarget, itemTarget, source, body);
    }

    private GeneratorExpression ParseForGeneratorExpression()
    {
        Expect(TokenKind.For, "Expected 'for' in generator expression.");
        BindingTarget first = ParseBindingTarget();
        BindingTarget? indexTarget = null;
        BindingTarget itemTarget = first;
        if (Match(TokenKind.Comma))
        {
            indexTarget = first;
            itemTarget = ParseBindingTarget();
        }

        Expect(TokenKind.In, "Expected 'in' in generator expression.");
        Expression source = ParseExpression();
        LambdaBody body = ParseGeneratorBody();
        return new GeneratorExpression(indexTarget, itemTarget, source, body);
    }

    private LambdaBody ParseGeneratorBody()
    {
        if (Match(TokenKind.Do))
        {
            return Current.Kind == TokenKind.OpenBrace
                ? new LambdaBlockBody(ParseBlockStatement())
                : new LambdaExpressionBody(ParseExpression());
        }

        if (Current.Kind == TokenKind.OpenBrace)
        {
            return new LambdaBlockBody(ParseBlockStatement());
        }

        _diagnostics.Add(new Diagnostic("Expected `do` or block body in generator expression.", Current.Span));
        return new LambdaExpressionBody(ParseExpression());
    }

    private bool TryParseBareGeneratorExpression(out Expression? expression)
    {
        int savedPosition = _position;
        int savedDiagnostics = _diagnostics.Count;
        expression = null;

        if (Current.Kind == TokenKind.For)
        {
            expression = ParseForGeneratorExpression();
            return true;
        }

        Expression source = ParseExpression();
        if (Match(TokenKind.Arrow))
        {
            expression = ParseArrowGeneratorExpression(source);
            return true;
        }

        Restore(savedPosition, savedDiagnostics);
        return false;
    }

    private Expression ParseIfExpression()
    {
        Expect(TokenKind.If, "Expected 'if'.");
        Expression condition = ParseExpression();
        Expect(TokenKind.Then, "Expected 'then' in one-line if expression.");
        Expression thenExpression = ParseExpression();
        SkipSeparators();
        Expect(TokenKind.Else, "Expected 'else' in one-line if expression.");
        SkipSeparators();
        Expression elseExpression = ParseExpression();
        return new IfExpression(condition, thenExpression, elseExpression);
    }

    private InterpolatedStringExpression ParseInterpolatedStringExpression()
    {
        Token token = Expect(TokenKind.InterpolatedStringLiteral, "Expected interpolated string literal.");
        return new InterpolatedStringExpression(ParseInterpolatedStringParts(token));
    }

    private IReadOnlyList<InterpolatedStringPart> ParseInterpolatedStringParts(Token token)
    {
        List<InterpolatedStringPart> parts = [];
        System.Text.StringBuilder text = new();
        string raw = token.Text;
        int index = 2;

        while (index < raw.Length)
        {
            if (index == raw.Length - 1 && raw[index] == '"')
            {
                break;
            }

            char ch = raw[index];
            if (ch == '{')
            {
                if (index + 1 < raw.Length && raw[index + 1] == '{')
                {
                    text.Append('{');
                    index += 2;
                    continue;
                }

                FlushTextPart();
                int interpolationStart = index + 1;
                int interpolationDepth = 1;
                index++;

                while (index < raw.Length && interpolationDepth > 0)
                {
                    char inner = raw[index];
                    if (inner == '"' || inner == '\'')
                    {
                        index = SkipStringLike(raw, index, inner);
                        continue;
                    }

                    if (inner == '/' && index + 1 < raw.Length && raw[index + 1] == '/')
                    {
                        index += 2;
                        while (index < raw.Length && raw[index] != '\n')
                        {
                            index++;
                        }

                        continue;
                    }

                    if (inner == '/' && index + 1 < raw.Length && raw[index + 1] == '*')
                    {
                        index += 2;
                        while (index + 1 < raw.Length && !(raw[index] == '*' && raw[index + 1] == '/'))
                        {
                            index++;
                        }

                        index = Math.Min(raw.Length, index + 2);
                        continue;
                    }

                    if (inner == '{')
                    {
                        interpolationDepth++;
                        index++;
                        continue;
                    }

                    if (inner == '}')
                    {
                        interpolationDepth--;
                        index++;
                        continue;
                    }

                    index++;
                }

                int interpolationEnd = Math.Max(interpolationStart, index - 1);
                string expressionText = raw[interpolationStart..interpolationEnd];
                parts.Add(new InterpolatedStringInterpolationPart(ParseInterpolatedHole(expressionText, token.Position + interpolationStart)));
                continue;
            }

            if (ch == '}' && index + 1 < raw.Length && raw[index + 1] == '}')
            {
                text.Append('}');
                index += 2;
                continue;
            }

            if (ch == '\\')
            {
                text.Append(ParseEscapedCharacter(raw, ref index));
                continue;
            }

            text.Append(ch);
            index++;
        }

        FlushTextPart();
        return parts;

        void FlushTextPart()
        {
            if (text.Length == 0)
            {
                return;
            }

            parts.Add(new InterpolatedStringTextPart(text.ToString()));
            text.Clear();
        }
    }

    private Expression ParseInterpolatedHole(string expressionText, int sourceOffset)
    {
        Lexer lexer = new(expressionText);
        IReadOnlyList<Token> tokens = lexer.Lex();
        AddInterpolatedDiagnostics(lexer.Diagnostics, sourceOffset);

        Parser parser = new(tokens);
        Expression expression = parser.ParseExpression();
        parser.SkipSeparators();
        if (parser.Current.Kind != TokenKind.EndOfFile)
        {
            _diagnostics.Add(new Diagnostic(
                "Unexpected trailing tokens in interpolated-string expression.",
                new TextSpan(sourceOffset + parser.Current.Span.Start, parser.Current.Span.Length)));
        }

        AddInterpolatedDiagnostics(parser.Diagnostics, sourceOffset);
        return expression;
    }

    private void AddInterpolatedDiagnostics(IReadOnlyList<Diagnostic> diagnostics, int sourceOffset)
    {
        foreach (Diagnostic diagnostic in diagnostics)
        {
            _diagnostics.Add(new Diagnostic(
                diagnostic.Message,
                new TextSpan(sourceOffset + diagnostic.Span.Start, diagnostic.Span.Length)));
        }
    }

    private static int SkipStringLike(string text, int index, char delimiter)
    {
        index++;
        bool escaped = false;

        while (index < text.Length)
        {
            char current = text[index++];
            if (!escaped && current == '\\')
            {
                escaped = true;
                continue;
            }

            if (!escaped && current == delimiter)
            {
                break;
            }

            escaped = false;
        }

        return index;
    }

    private static char ParseEscapedCharacter(string text, ref int index)
    {
        index++;
        if (index >= text.Length)
        {
            return '\\';
        }

        char escaped = text[index++];
        return escaped switch
        {
            '\\' => '\\',
            '"' => '"',
            '\'' => '\'',
            '0' => '\0',
            'a' => '\a',
            'b' => '\b',
            'f' => '\f',
            'n' => '\n',
            'r' => '\r',
            't' => '\t',
            'v' => '\v',
            'u' when index + 4 <= text.Length && ushort.TryParse(text.AsSpan(index, 4), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out ushort unicode)
                => ConsumeEscapedCodePoint(ref index, 4, unicode),
            'x' => ParseVariableHexEscape(text, ref index),
            _ => escaped,
        };
    }

    private static char ConsumeEscapedCodePoint(ref int index, int length, ushort codePoint)
    {
        index += length;
        return (char)codePoint;
    }

    private static char ParseVariableHexEscape(string text, ref int index)
    {
        int start = index;
        int count = 0;
        while (index < text.Length && count < 4 && Uri.IsHexDigit(text[index]))
        {
            index++;
            count++;
        }

        if (count == 0 || !ushort.TryParse(text.AsSpan(start, count), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out ushort codePoint))
        {
            return 'x';
        }

        return (char)codePoint;
    }
}

