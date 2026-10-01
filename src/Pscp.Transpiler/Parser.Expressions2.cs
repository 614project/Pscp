namespace Pscp.Transpiler;

public sealed partial class Parser
{
    private Expression ParseNewExpression()
    {
        Expect(TokenKind.New, "Expected 'new'.");
        bool autoConstructElements = Current.Kind == TokenKind.Bang && IsCurrentAdjacentToPreviousToken() && Match(TokenKind.Bang);

        if (Current.Kind == TokenKind.OpenBracket)
        {
            List<Expression> dimensions = ParseArrayDimensionList();
            return new TargetTypedNewArrayExpression(dimensions, autoConstructElements);
        }

        if (autoConstructElements)
        {
            AddDiagnostic(DiagnosticCodes.Syntax, "Expected '[' after 'new!'.", Current.Span);
        }

        if (Current.Kind == TokenKind.OpenParen)
        {
            Next();
            IReadOnlyList<ArgumentSyntax> targetTypedArguments = ParseCallArgumentsTail();
            return new NewExpression(null, targetTypedArguments, ParseOptionalObjectInitializer());
        }

        int typeStart = _position;
        int typeDiagnostics = _diagnostics.Count;
        TypeSyntax? type = TryParseTypeSyntax(allowSizedArrays: false);
        if (type is not null && Current.Kind == TokenKind.OpenBracket)
        {
            List<Expression> dimensions = ParseArrayDimensionList();
            return new NewArrayExpression(type, dimensions);
        }

        if (type is not null && Current.Kind == TokenKind.OpenParen)
        {
            Next();
            IReadOnlyList<ArgumentSyntax> arguments = ParseCallArgumentsTail();
            return new NewExpression(type, arguments, ParseOptionalObjectInitializer());
        }

        if (type is not null && Current.Kind == TokenKind.OpenBrace)
        {
            return new NewExpression(type, Immutable.List<ArgumentSyntax>(), ParseMemberInitializerList("new"));
        }

        if (type is not null && CanStartApplicationGroup() && LooksLikeExplicitTypeForSpaceSeparatedNew(type))
        {
            return new NewExpression(type, ParseSpaceSeparatedArguments());
        }

        Restore(typeStart, typeDiagnostics);
        if (CanStartApplicationGroup())
        {
            return new NewExpression(null, ParseSpaceSeparatedArguments());
        }

        TypeSyntax fallbackType = ParseTypeSyntax(allowSizedArrays: false);
        Expect(TokenKind.OpenParen, "Expected '(' after new expression.");
        return new NewExpression(fallbackType, ParseCallArgumentsTail());
    }

    private IReadOnlyList<WithAssignment>? ParseOptionalObjectInitializer()
        => Current.Kind == TokenKind.OpenBrace && !_inStatementHead
            ? ParseMemberInitializerList("new")
            : null;

    private IReadOnlyList<ArgumentSyntax> ParseSpaceSeparatedArguments()
    {
        List<ArgumentSyntax> arguments = [];
        while (CanStartApplicationGroup())
        {
            ParseApplicationGroup(arguments);
        }

        return arguments;
    }

    private static bool LooksLikeExplicitTypeForSpaceSeparatedNew(TypeSyntax type)
        => type switch
        {
            NamedTypeSyntax named when named.Name.Length > 0
                => char.IsUpper(named.Name[0]) || named.Name.Contains('.', StringComparison.Ordinal) || named.TypeArguments.Count > 0,
            ArrayTypeSyntax => true,
            NullableTypeSyntax nullable => LooksLikeExplicitTypeForSpaceSeparatedNew(nullable.InnerType),
            _ => false
        };

    // `[e1, ..spread, a..<b]` and the builder `[src -> x do e]` (spec §16.1, §16.2). A range written directly as
    // an element expands; `..<b` without a start cannot be told apart from a spread and is an error.
    private CollectionExpression ParseCollectionExpression()
    {
        Expect(TokenKind.OpenBracket, "Expected '[' to start collection expression.");
        List<CollectionElement> elements = WithNestedContext(() =>
        {
            List<CollectionElement> parsed = [];
            SkipSeparators();
            if (Current.Kind == TokenKind.CloseBracket)
            {
                return parsed;
            }

            do
            {
                SkipSeparators();
                int elementStart = _position;
                if (Current.Kind == TokenKind.DotDot)
                {
                    Next();
                    parsed.Add(Mark(new SpreadElement(ParseExpression()), elementStart));
                    SkipSeparators();
                    continue;
                }

                if (Current.Kind is TokenKind.DotDotLess or TokenKind.DotDotEqual)
                {
                    AddDiagnostic(
                        DiagnosticCodes.ExclusiveRangeInCollection,
                        $"`{Current.Text}b` inside `[]` has no start. Write the start (`0{Current.Text}b`), or `..xs` to spread a sequence.",
                        Current.Span);
                    Next();
                    parsed.Add(Mark(new SpreadElement(ParseExpression()), elementStart));
                    SkipSeparators();
                    continue;
                }

                Expression expression = ParseExpression();
                if (Match(TokenKind.Arrow))
                {
                    (BindingTarget? indexTarget, BindingTarget itemTarget) = ParseIterationBinding();
                    LambdaBody body;
                    if (Match(TokenKind.Do))
                    {
                        body = ParseLambdaBody();
                    }
                    else if (Current.Kind == TokenKind.OpenBrace)
                    {
                        body = new LambdaBlockBody(WithNestedContext(ParseBlockStatement));
                    }
                    else
                    {
                        AddDiagnostic(DiagnosticCodes.Syntax, "Expected `do` or a block body in the collection builder.", Current.Span);
                        body = new LambdaExpressionBody(ParseExpression());
                    }

                    parsed.Add(Mark(new BuilderElement(expression, indexTarget, itemTarget, body), elementStart));
                }
                else if (expression is RangeExpression range)
                {
                    parsed.Add(Mark(new RangeElement(range), elementStart));
                }
                else
                {
                    parsed.Add(Mark(new ExpressionElement(expression), elementStart));
                }

                SkipSeparators();
            }
            while (Match(TokenKind.Comma));

            SkipSeparators();
            return parsed;
        });

        Expect(TokenKind.CloseBracket, "Expected ']' after collection expression.");
        return new CollectionExpression(elements);
    }

    private AggregationExpression ParseAggregationExpression(string name)
    {
        Expect(TokenKind.OpenBrace, "Expected '{' after aggregation name.");
        Expect(TokenKind.For, "Expected 'for' in aggregation expression.");
        (BindingTarget? indexTarget, BindingTarget itemTarget) = ParseIterationBinding();
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
        List<ArgumentSyntax> arguments = WithNestedContext(() =>
        {
            List<ArgumentSyntax> parsed = [];
            SkipSeparators();
            if (Current.Kind == TokenKind.CloseParen)
            {
                return parsed;
            }

            do
            {
                SkipSeparators();
                parsed.Add(ParseCallArgument(allowNamed: true, allowComplexExpression: true));
                SkipSeparators();
            }
            while (Match(TokenKind.Comma));

            return parsed;
        });

        Expect(TokenKind.CloseParen, "Expected ')' after argument list.");
        return arguments;
    }

    private IReadOnlyList<Expression> ParseIndexArgumentsTail()
    {
        bool savedHead = _inStatementHead;
        int savedIndexDepth = _indexArgumentDepth;
        _inStatementHead = false;
        _indexArgumentDepth = 1;
        List<Expression> arguments = [];
        try
        {
            do
            {
                SkipSeparators();
                arguments.Add(ParseIndexArgument());
                SkipSeparators();
            }
            while (Match(TokenKind.Comma));
        }
        finally
        {
            _inStatementHead = savedHead;
            _indexArgumentDepth = savedIndexDepth;
        }

        Expect(TokenKind.CloseBracket, "Expected ']' after index arguments.");
        return arguments;
    }

    private List<Expression> ParseArrayDimensionList()
    {
        List<Expression> dimensions = [];
        while (Match(TokenKind.OpenBracket))
        {
            Expression dimension = WithNestedContext(ParseExpression);
            Expect(TokenKind.CloseBracket, "Expected ']' after array dimension.");
            dimensions.Add(dimension);
        }

        return dimensions;
    }

    // An index or a slice (spec §15). Inside an indexer the end of a slice must be stated: `a..<b`, `a..=b`,
    // `a..`, `..<b`, `..=b` or `..`. `a..b` and `..b` are ambiguous (PSCP2303); a stepped range is not a slice.
    private Expression ParseIndexArgument()
    {
        int start = _position;
        switch (Current.Kind)
        {
            case TokenKind.DotDot:
            {
                Token dots = Next();
                if (Current.Kind is TokenKind.CloseBracket or TokenKind.Comma)
                {
                    return Mark(new SliceExpression(null, null, SliceKind.Open), start);
                }

                Expression end = ParseSliceBound();
                ReportAmbiguousSlice(start, dots, null, end);
                return Mark(new SliceExpression(null, end, SliceKind.Ambiguous), start);
            }
            case TokenKind.DotDotLess:
                Next();
                return Mark(new SliceExpression(null, ParseSliceBound(), SliceKind.Exclusive), start);
            case TokenKind.DotDotEqual:
                Next();
                return Mark(new SliceExpression(null, ParseSliceBound(), SliceKind.Inclusive), start);
        }

        Expression expression = ParseExpression();
        if (Current.Kind == TokenKind.DotDot && Peek(1).Kind is TokenKind.CloseBracket or TokenKind.Comma)
        {
            Next();
            return Mark(new SliceExpression(expression, null, SliceKind.Open), start);
        }

        if (expression is not RangeExpression range)
        {
            return expression;
        }

        if (range.Step is not null)
        {
            AddDiagnostic(DiagnosticCodes.SteppedSlice, "A stepped range cannot be used as a slice. Use a loop or `filter` to pick every k-th element.", _spans.Get(range));
            return Mark(new SliceExpression(range.Start, range.End, SliceKind.Exclusive), start);
        }

        switch (range.Kind)
        {
            case RangeKind.RightExclusive:
                return Mark(new SliceExpression(range.Start, range.End, SliceKind.Exclusive), start);
            case RangeKind.ExplicitInclusive:
                return Mark(new SliceExpression(range.Start, range.End, SliceKind.Inclusive), start);
            default:
                ReportAmbiguousSlice(start, null, range.Start, range.End);
                return Mark(new SliceExpression(range.Start, range.End, SliceKind.Ambiguous), start);
        }
    }

    private Expression ParseSliceBound()
        => ParseShift();

    private void ReportAmbiguousSlice(int startToken, Token? leadingDots, Expression? start, Expression end)
    {
        string startText = start is null ? string.Empty : SourceTextOf(start);
        string endText = SourceTextOf(end);
        TextSpan span = new(_tokens[startToken].Position, Math.Max(1, _spans.Get(end).End - _tokens[startToken].Position));
        AddDiagnostic(
            DiagnosticCodes.AmbiguousSliceBound,
            $"Inside an indexer `..` does not say whether the end is included. Write `[{startText}..<{endText}]` to exclude it or `[{startText}..={endText}]` to include it.",
            span);
    }

    private string SourceTextOf(Expression expression)
    {
        TextSpan span = _spans.Get(expression);
        if (span.Length == 0)
        {
            return "b";
        }

        int first = -1;
        int last = -1;
        for (int i = 0; i < _tokens.Count; i++)
        {
            if (_tokens[i].Position >= span.Start && _tokens[i].Position < span.End && _tokens[i].Kind != TokenKind.NewLine)
            {
                if (first < 0)
                {
                    first = i;
                }

                last = i;
            }
        }

        return first < 0 ? "b" : TokensToText(first, last + 1);
    }

    private ArgumentSyntax ParseCallArgument(bool allowNamed, bool allowComplexExpression)
    {
        int start = _position;
        string? name = null;
        if (allowNamed && Current.Kind == TokenKind.Identifier && Peek(1).Kind == TokenKind.Colon)
        {
            name = Next().Text;
            Expect(TokenKind.Colon, "Expected ':' after named argument label.");
        }

        ArgumentModifier modifier = ParseOptionalArgumentModifier();
        if (modifier == ArgumentModifier.Out)
        {
            if (TryParseOutDeclaration(name, out ArgumentSyntax? declaration))
            {
                return Mark(declaration!, start);
            }

            if (Current.Kind == TokenKind.Identifier && Current.Text == "_")
            {
                Next();
                return Mark(new ExpressionArgumentSyntax(name, modifier, new DiscardExpression()), start);
            }
        }

        Expression expression = allowComplexExpression && TryParseBareGeneratorExpression(out Expression? generator)
            ? generator!
            : allowComplexExpression ? ParseExpression() : ParsePostfix(allowLineLeadingMember: false);

        return Mark(new ExpressionArgumentSyntax(name, modifier, expression), start);
    }
}
