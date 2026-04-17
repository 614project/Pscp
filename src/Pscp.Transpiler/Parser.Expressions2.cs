namespace Pscp.Transpiler;

public sealed partial class Parser
{
    private Expression ParseNewExpression()
    {
        Expect(TokenKind.New, "Expected 'new'.");
        bool autoConstructElements = Match(TokenKind.Bang);

        if (Current.Kind == TokenKind.OpenBracket)
        {
            List<Expression> dimensions = ParseArrayDimensionList();
            return new TargetTypedNewArrayExpression(dimensions, autoConstructElements);
        }

        if (Current.Kind == TokenKind.OpenParen)
        {
            Expect(TokenKind.OpenParen, "Expected '(' after new expression.");
            return new NewExpression(null, ParseCallArgumentsTail());
        }

        if (autoConstructElements)
        {
            _diagnostics.Add(new Diagnostic("Expected '[' after 'new!'.", Current.Span));
        }

        TypeSyntax type = ParseTypeSyntax(allowSizedArrays: false);
        if (Current.Kind == TokenKind.OpenBracket)
        {
            List<Expression> dimensions = ParseArrayDimensionList();
            return new NewArrayExpression(type, dimensions);
        }

        Expect(TokenKind.OpenParen, "Expected '(' after new expression.");
        return new NewExpression(type, ParseCallArgumentsTail());
    }

    private CollectionExpression ParseCollectionExpression()
    {
        Expect(TokenKind.OpenBracket, "Expected '[' to start collection expression.");
        List<CollectionElement> elements = [];

        if (!Match(TokenKind.CloseBracket))
        {
            do
            {
                if (Current.Kind == TokenKind.DotDot)
                {
                    Next();
                    elements.Add(new SpreadElement(ParseExpression()));
                    continue;
                }

                Expression expression = ParseExpression();
                if (Match(TokenKind.Arrow))
                {
                    BindingTarget first = ParseBindingTarget();
                    BindingTarget? indexTarget = null;
                    BindingTarget itemTarget = first;

                    if (Match(TokenKind.Comma))
                    {
                        indexTarget = first;
                        itemTarget = ParseBindingTarget();
                    }

                    LambdaBody body;
                    if (Match(TokenKind.Do))
                    {
                        body = Current.Kind == TokenKind.OpenBrace
                            ? new LambdaBlockBody(ParseBlockStatement())
                            : new LambdaExpressionBody(ParseExpression());
                    }
                    else if (Current.Kind == TokenKind.OpenBrace)
                    {
                        body = new LambdaBlockBody(ParseBlockStatement());
                    }
                    else
                    {
                        _diagnostics.Add(new Diagnostic("Expected `do` or block body in collection builder.", Current.Span));
                        body = new LambdaExpressionBody(ParseExpression());
                    }

                    elements.Add(new BuilderElement(expression, indexTarget, itemTarget, body));
                }
                else if (expression is RangeExpression range)
                {
                    elements.Add(new RangeElement(range));
                }
                else
                {
                    elements.Add(new ExpressionElement(expression));
                }
            }
            while (Match(TokenKind.Comma));

            Expect(TokenKind.CloseBracket, "Expected ']' after collection expression.");
        }

        return new CollectionExpression(elements);
    }

    private AggregationExpression ParseAggregationExpression(string name)
    {
        Expect(TokenKind.OpenBrace, "Expected '{' after aggregation name.");
        Expect(TokenKind.For, "Expected 'for' in aggregation expression.");
        BindingTarget first = ParseBindingTarget();
        BindingTarget? indexTarget = null;
        BindingTarget itemTarget = first;

        if (Match(TokenKind.Comma))
        {
            indexTarget = first;
            itemTarget = ParseBindingTarget();
        }

        Expect(TokenKind.In, "Expected 'in' in aggregation expression.");
        Expression source = ParseExpression();
        Expression? whereExpression = null;

        if (Match(TokenKind.Where))
        {
            whereExpression = ParseExpression();
        }

        Expect(TokenKind.Do, "Expected 'do' in aggregation expression.");
        Expression body = ParseExpression();
        Expect(TokenKind.CloseBrace, "Expected '}' after aggregation expression.");

        return new AggregationExpression(name, indexTarget, itemTarget, source, whereExpression, body);
    }

    private IReadOnlyList<ArgumentSyntax> ParseCallArgumentsTail()
    {
        List<ArgumentSyntax> arguments = [];
        if (Match(TokenKind.CloseParen))
        {
            return arguments;
        }

        do
        {
            arguments.Add(ParseCallArgument(allowNamed: true, allowComplexExpression: true));
        }
        while (Match(TokenKind.Comma));

        Expect(TokenKind.CloseParen, "Expected ')' after argument list.");
        return arguments;
    }

    private IReadOnlyList<Expression> ParseIndexArgumentsTail()
    {
        List<Expression> arguments = [];

        do
        {
            arguments.Add(ParseIndexArgument());
        }
        while (Match(TokenKind.Comma));

        Expect(TokenKind.CloseBracket, "Expected ']' after index arguments.");
        return arguments;
    }

    private List<Expression> ParseArrayDimensionList()
    {
        List<Expression> dimensions = [];
        while (Match(TokenKind.OpenBracket))
        {
            Expression dimension = ParseExpression();
            Expect(TokenKind.CloseBracket, "Expected ']' after array dimension.");
            dimensions.Add(dimension);
        }

        return dimensions;
    }

    private Expression ParseIndexArgument()
    {
        if (Match(TokenKind.DotDot))
        {
            Expression? end = Current.Kind is TokenKind.CloseBracket or TokenKind.Comma
                ? null
                : ParseIndexBoundaryExpression();
            return new SliceExpression(null, end);
        }

        Expression start = ParseIndexBoundaryExpression();
        if (!Match(TokenKind.DotDot))
        {
            return start;
        }

        Expression? endExpression = Current.Kind is TokenKind.CloseBracket or TokenKind.Comma
            ? null
            : ParseIndexBoundaryExpression();
        return new SliceExpression(start, endExpression);
    }

    private Expression ParseIndexBoundaryExpression()
    {
        if (Match(TokenKind.Caret))
        {
            return new FromEndExpression(ParseAdditive());
        }

        return ParseAdditive();
    }

    private ArgumentSyntax ParseCallArgument(bool allowNamed, bool allowComplexExpression)
    {
        string? name = null;
        ArgumentModifier modifier = ParseOptionalArgumentModifier();

        if (allowNamed && modifier == ArgumentModifier.None && Current.Kind == TokenKind.Identifier && Peek(1).Kind == TokenKind.Colon)
        {
            name = Next().Text;
            Expect(TokenKind.Colon, "Expected ':' after named argument label.");
        }

        if (modifier == ArgumentModifier.Out)
        {
            int savedPosition = _position;
            int savedDiagnostics = _diagnostics.Count;
            TypeSyntax? outType = TryParseTypeSyntax(allowSizedArrays: false);
            if (outType is not null && TryParseBindingTarget(out BindingTarget? target))
            {
                return new OutDeclarationArgumentSyntax(name, outType, target!);
            }

            Restore(savedPosition, savedDiagnostics);
        }

        Expression expression;
        if (modifier == ArgumentModifier.Out && Current.Kind == TokenKind.Identifier && Current.Text == "_")
        {
            Next();
            expression = new DiscardExpression();
        }
        else
        {
            expression = allowComplexExpression && TryParseBareGeneratorExpression(out Expression? generator)
                ? generator!
                : allowComplexExpression ? ParseExpression() : ParseAtomicArgumentExpression();
        }

        return new ExpressionArgumentSyntax(name, modifier, expression);
    }

    private Expression ParseAtomicArgumentExpression()
    {
        if (Current.Kind is TokenKind.PlusPlus or TokenKind.MinusMinus)
        {
            TokenKind kind = Next().Kind;
            return new PrefixExpression(
                kind == TokenKind.PlusPlus ? PostfixOperator.Increment : PostfixOperator.Decrement,
                ParseAtomicArgumentExpression());
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

            return new UnaryExpression(op, ParseAtomicArgumentExpression());
        }

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

            break;
        }

        return expression;
    }

    private Expression AppendSpaceCallArgument(Expression expression, ArgumentSyntax argument)
    {
        if (expression is CallExpression { IsSpaceSeparated: true } call)
        {
            return new CallExpression(call.Callee, call.Arguments.Concat([argument]).ToArray(), true);
        }

        return new CallExpression(expression, Immutable.List(argument), true);
    }

    private bool CanContinueSpaceCall(Expression expression)
        => expression is IdentifierExpression
            or MemberAccessExpression
            or IndexExpression
            or TupleProjectionExpression
            or CallExpression;

    private static bool CanStartSpaceArgument(TokenKind kind)
        => kind is TokenKind.Identifier
            or TokenKind.IntegerLiteral
            or TokenKind.FloatLiteral
            or TokenKind.StringLiteral
            or TokenKind.CharLiteral
            or TokenKind.True
            or TokenKind.False
            or TokenKind.Null
            or TokenKind.OpenParen
            or TokenKind.OpenBracket
            or TokenKind.If
            or TokenKind.New
            or TokenKind.Ref
            or TokenKind.Out
            or TokenKind.In;
}



