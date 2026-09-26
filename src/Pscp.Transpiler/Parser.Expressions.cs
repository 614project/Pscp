namespace Pscp.Transpiler;

// Expression grammar (spec §13.1 and appendix A), weakest to strongest:
// assignment → conditional → `??` → `or` → `and` → `|` → `^` → `&` → `==` → relational/`is`/`as` → pipe → range
// → shift → additive → multiplicative → `switch`/`with` → unary/cast → application (space-call) → postfix → primary.
public sealed partial class Parser
{
    private Expression ParseExpression()
        => ParseAssignment();

    private static bool IsAssignmentOperator(TokenKind kind)
        => kind is TokenKind.Equal
            or TokenKind.PlusEqual
            or TokenKind.MinusEqual
            or TokenKind.StarEqual
            or TokenKind.SlashEqual
            or TokenKind.PercentEqual
            or TokenKind.AmpEqual
            or TokenKind.PipeEqual
            or TokenKind.CaretEqual
            or TokenKind.LessLessEqual
            or TokenKind.GreaterGreaterEqual
            or TokenKind.QuestionQuestionEqual
            or TokenKind.ColonEqual;

    private Expression ParseAssignment()
    {
        int start = _position;
        Expression target = ParseConditional();
        if (!IsAssignmentOperator(Current.Kind))
        {
            return target;
        }

        Token operatorToken = Next();
        if (operatorToken.Kind == TokenKind.Equal && Current.Kind is TokenKind.NewLine or TokenKind.EndOfFile)
        {
            // Spec §4.3: a line ends with `=` only in an input declaration (`int n =`).
            AddDiagnostic(
                DiagnosticCodes.TrailingAssignment,
                "The value of `=` is missing. Only an input declaration such as `int n =` may end a line with `=`; wrap a long value in parentheses to continue it on the next line.",
                operatorToken.Span);
            return Mark(new AssignmentExpression(target, AssignmentOperator.Assign, new IdentifierExpression("default"), false), start);
        }

        // Any other assignment operator at the end of a line continues the statement (spec §4.2 rule 2).
        SkipExpressionNewLines();
        Expression value = ParseAssignment();
        return Mark(
            new AssignmentExpression(target, ToAssignmentOperator(operatorToken.Kind), value, operatorToken.Kind == TokenKind.ColonEqual),
            start);
    }

    private Expression ParseConditional()
    {
        int start = _position;
        Expression condition = ParseCoalesce();
        if (Current.Kind != TokenKind.Question)
        {
            return condition;
        }

        Next();
        SkipExpressionNewLines();
        Expression whenTrue = ParseExpression();
        Expect(TokenKind.Colon, "Expected ':' in conditional expression.");
        SkipExpressionNewLines();
        Expression whenFalse = ParseConditional();
        return Mark(new ConditionalExpression(condition, whenTrue, whenFalse), start);
    }

    private Expression ParseCoalesce()
    {
        int start = _position;
        Expression left = ParseOr();
        if (Current.Kind != TokenKind.QuestionQuestion)
        {
            return left;
        }

        Next();
        SkipExpressionNewLines();
        Expression right = ParseCoalesce();
        return Mark(new BinaryExpression(left, BinaryOperator.Coalesce, right), start);
    }

    private Expression ParseOr()
    {
        int start = _position;
        Expression expression = ParseAnd();
        while (true)
        {
            // `||` / `or` at the start of the next line continue the expression (spec §4.2 rule 3).
            SkipExpressionNewLinesBefore(TokenKind.PipePipe, TokenKind.Or);
            if (Current.Kind is not TokenKind.PipePipe and not TokenKind.Or)
            {
                return expression;
            }

            Next();
            SkipExpressionNewLines();
            expression = Mark(new BinaryExpression(expression, BinaryOperator.LogicalOr, ParseAnd()), start);
        }
    }

    private Expression ParseAnd()
    {
        int start = _position;
        Expression expression = ParseBitwiseOr();
        while (true)
        {
            SkipExpressionNewLinesBefore(TokenKind.AmpAmp, TokenKind.And);
            if (Current.Kind is not TokenKind.AmpAmp and not TokenKind.And)
            {
                return expression;
            }

            Next();
            SkipExpressionNewLines();
            expression = Mark(new BinaryExpression(expression, BinaryOperator.LogicalAnd, ParseBitwiseOr()), start);
        }
    }

    private Expression ParseBitwiseOr()
    {
        int start = _position;
        Expression expression = ParseXor();
        while (Current.Kind == TokenKind.Pipe)
        {
            Next();
            SkipExpressionNewLines();
            expression = Mark(new BinaryExpression(expression, BinaryOperator.BitwiseOr, ParseXor()), start);
        }

        return expression;
    }

    private Expression ParseXor()
    {
        int start = _position;
        Expression expression = ParseBitwiseAnd();
        while (Current.Kind is TokenKind.Caret or TokenKind.Xor)
        {
            Next();
            SkipExpressionNewLines();
            expression = Mark(new BinaryExpression(expression, BinaryOperator.BitwiseXor, ParseBitwiseAnd()), start);
        }

        return expression;
    }

    private Expression ParseBitwiseAnd()
    {
        int start = _position;
        Expression expression = ParseEquality();
        while (Current.Kind == TokenKind.Amp)
        {
            Next();
            SkipExpressionNewLines();
            expression = Mark(new BinaryExpression(expression, BinaryOperator.BitwiseAnd, ParseEquality()), start);
        }

        return expression;
    }

    private Expression ParseEquality()
    {
        int start = _position;
        Expression expression = ParseRelational();
        while (Current.Kind is TokenKind.EqualEqual or TokenKind.BangEqual)
        {
            Token operatorToken = Next();
            WarnIfNotOnComparisonLeft(expression, operatorToken);
            SkipExpressionNewLines();
            expression = Mark(
                new BinaryExpression(
                    expression,
                    operatorToken.Kind == TokenKind.EqualEqual ? BinaryOperator.Equal : BinaryOperator.NotEqual,
                    ParseRelational()),
                start);
        }

        return expression;
    }

    private Expression ParseRelational()
    {
        int start = _position;
        Expression expression = ParsePipe();
        while (true)
        {
            BinaryOperator? relational = Current.Kind switch
            {
                TokenKind.LessThan => BinaryOperator.LessThan,
                TokenKind.LessEqual => BinaryOperator.LessThanOrEqual,
                TokenKind.GreaterThan => BinaryOperator.GreaterThan,
                TokenKind.GreaterEqual => BinaryOperator.GreaterThanOrEqual,
                TokenKind.Spaceship => BinaryOperator.Spaceship,
                _ => null,
            };

            if (relational is not null)
            {
                Token operatorToken = Next();
                WarnIfNotOnComparisonLeft(expression, operatorToken);
                SkipExpressionNewLines();
                expression = Mark(new BinaryExpression(expression, relational.Value, ParsePipe()), start);
                continue;
            }

            if (Current.Kind == TokenKind.Is)
            {
                Token isToken = Next();
                WarnIfNotOnComparisonLeft(expression, isToken);
                PatternSyntax pattern = ParsePattern(isToken, inSwitchArm: false);
                expression = Mark(new IsPatternExpression(expression, pattern), start);
                continue;
            }

            if (Current.Kind == TokenKind.Identifier && Current.Text == "as")
            {
                Next();
                TypeSyntax type = ParseTypeSyntax(allowSizedArrays: false);
                expression = Mark(new AsExpression(expression, type), start);
                continue;
            }

            return expression;
        }
    }

    // Spec §13.7: `not` is a prefix operator, so `not a == b` is `(not a) == b`.
    private void WarnIfNotOnComparisonLeft(Expression left, Token operatorToken)
    {
        if (left is UnaryExpression unary && _notKeywordExpressions.Contains(unary))
        {
            AddDiagnostic(
                DiagnosticCodes.NotOnComparisonLeft,
                $"`not` applies only to its operand, so this compares `not ...` with `{operatorToken.Text}`. Write `not (a {operatorToken.Text} b)` or use the opposite operator.",
                _spans.Get(unary),
                DiagnosticSeverity.Warning);
        }
    }

    // Pipes sit between range and relational operators (spec §13.3). `lhs |> target` inserts `lhs` as the first
    // argument and `target <| rhs` inserts `rhs` as the last; mixing both directions in one chain is an error.
    private Expression ParsePipe()
    {
        int start = _position;
        Expression expression = ParseRange();
        bool sawForward = false;
        while (true)
        {
            SkipExpressionNewLinesBefore(TokenKind.PipeGreater, TokenKind.LessPipe);
            if (Current.Kind == TokenKind.PipeGreater)
            {
                Token pipeToken = Next();
                SkipExpressionNewLines();
                sawForward = true;
                expression = Mark(ParseForwardPipeTarget(expression), start);
                continue;
            }

            if (Current.Kind == TokenKind.LessPipe)
            {
                Token pipeToken = Next();
                if (sawForward)
                {
                    ReportMixedPipes(pipeToken);
                }

                SkipExpressionNewLines();
                Expression value = ParseBackwardPipeChain();
                expression = Mark(MakeBackwardPipe(expression, value), start);
                continue;
            }

            return expression;
        }
    }

    // `target <| (value chain)`: `<|` is right associative, so `f <| g <| x` is `f (g x)`.
    private Expression ParseBackwardPipeChain()
    {
        int start = _position;
        Expression target = ParseRange();
        SkipExpressionNewLinesBefore(TokenKind.PipeGreater, TokenKind.LessPipe);
        if (Current.Kind == TokenKind.LessPipe)
        {
            Next();
            SkipExpressionNewLines();
            Expression value = ParseBackwardPipeChain();
            return Mark(MakeBackwardPipe(target, value), start);
        }

        if (Current.Kind == TokenKind.PipeGreater)
        {
            ReportMixedPipes(Current);
        }

        return target;
    }

    private void ReportMixedPipes(Token token)
        => AddDiagnostic(
            DiagnosticCodes.MixedPipeDirections,
            "`|>` and `<|` cannot be mixed in one chain. Add parentheses to say which pipe applies first.",
            token.Span);

    private Expression ParseForwardPipeTarget(Expression piped)
    {
        int targetStart = _position;
        int parenthesizedEnd = Current.Kind == TokenKind.OpenParen ? FindMatchingClose(_position) : -1;
        Expression target = ParseUnaryOperand();
        ExpressionArgumentSyntax argument = new(null, ArgumentModifier.None, piped);

        // `lhs |> (e)`: the parenthesized expression is invoked as a delegate.
        if (parenthesizedEnd >= 0 && _position == parenthesizedEnd + 1)
        {
            return Mark(new CallExpression(target, [argument], false, PipeKind.Delegate), targetStart);
        }

        return target switch
        {
            CallExpression call => new CallExpression(call.Callee, [argument, .. call.Arguments], call.IsSpaceSeparated, PipeKind.Forward),
            _ => new CallExpression(target, [argument], false, PipeKind.Forward),
        };
    }

    private static Expression MakeBackwardPipe(Expression target, Expression value)
    {
        ExpressionArgumentSyntax argument = new(null, ArgumentModifier.None, value);
        return target switch
        {
            CallExpression call => new CallExpression(call.Callee, [.. call.Arguments, argument], call.IsSpaceSeparated, PipeKind.Backward),
            _ => new CallExpression(target, [argument], false, PipeKind.Backward),
        };
    }

    // Ranges are weaker than arithmetic (`0..<n-1` is `0..<(n-1)`) and have no associativity. `a..s..b`,
    // `a..s..<b` and `a..s..=b` are stepped ranges. Inside an indexer `a..` has no end and is left to the slice
    // parser.
    private Expression ParseRange()
    {
        int start = _position;
        Expression first = ParseShift();
        if (!IsRangeOperator(Current.Kind) || Peek(1).Kind is TokenKind.CloseBracket or TokenKind.Comma)
        {
            return first;
        }

        TokenKind firstOperator = Next().Kind;
        SkipExpressionNewLines();
        Expression second = ParseShift();
        if (firstOperator == TokenKind.DotDot && IsRangeOperator(Current.Kind))
        {
            TokenKind secondOperator = Next().Kind;
            SkipExpressionNewLines();
            Expression end = ParseShift();
            return Mark(new RangeExpression(first, second, end, ToRangeKind(secondOperator)), start);
        }

        return Mark(new RangeExpression(first, null, second, ToRangeKind(firstOperator)), start);
    }

    private static bool IsRangeOperator(TokenKind kind)
        => kind is TokenKind.DotDot or TokenKind.DotDotLess or TokenKind.DotDotEqual;

    private static RangeKind ToRangeKind(TokenKind kind)
        => kind switch
        {
            TokenKind.DotDotLess => RangeKind.RightExclusive,
            TokenKind.DotDotEqual => RangeKind.ExplicitInclusive,
            _ => RangeKind.Inclusive,
        };

    private Expression ParseShift()
    {
        int start = _position;
        Expression expression = ParseAdditive();
        while (TryReadShiftOperator(out BinaryOperator shiftOperator))
        {
            SkipExpressionNewLines();
            expression = Mark(new BinaryExpression(expression, shiftOperator, ParseAdditive()), start);
        }

        return expression;
    }

    // A `+` or `-` at the start of a line begins a new statement (spec §4.3), so no line breaks are skipped here.
    private Expression ParseAdditive()
    {
        int start = _position;
        Expression expression = ParseMultiplicative();
        while (Current.Kind is TokenKind.Plus or TokenKind.Minus)
        {
            TokenKind kind = Next().Kind;
            SkipExpressionNewLines();
            expression = Mark(
                new BinaryExpression(expression, kind == TokenKind.Plus ? BinaryOperator.Add : BinaryOperator.Subtract, ParseMultiplicative()),
                start);
        }

        return expression;
    }

    private Expression ParseMultiplicative()
    {
        int start = _position;
        Expression expression = ParseSwitchOrWith();
        while (Current.Kind is TokenKind.Star or TokenKind.Slash or TokenKind.Percent)
        {
            TokenKind kind = Next().Kind;
            SkipExpressionNewLines();
            BinaryOperator op = kind switch
            {
                TokenKind.Star => BinaryOperator.Multiply,
                TokenKind.Slash => BinaryOperator.Divide,
                _ => BinaryOperator.Modulo,
            };

            expression = Mark(new BinaryExpression(expression, op, ParseSwitchOrWith()), start);
        }

        return expression;
    }

    private Expression ParseSwitchOrWith()
    {
        int start = _position;
        Expression expression = ParseUnary();
        while (Current.Kind == TokenKind.Identifier
            && Current.Text is "switch" or "with"
            && NextNonNewLineKind(1) == TokenKind.OpenBrace)
        {
            expression = Current.Text == "switch"
                ? Mark(ParseSwitchExpression(expression), start)
                : Mark(ParseWithExpression(expression), start);
        }

        return expression;
    }

    private Expression ParseUnary()
    {
        int start = _position;
        switch (Current.Kind)
        {
            case TokenKind.PlusPlus:
            case TokenKind.MinusMinus:
            {
                TokenKind kind = Next().Kind;
                PostfixOperator op = kind == TokenKind.PlusPlus ? PostfixOperator.Increment : PostfixOperator.Decrement;
                return Mark(new PrefixExpression(op, ParseUnary()), start);
            }
            case TokenKind.Plus:
            case TokenKind.Minus:
            case TokenKind.Bang:
            case TokenKind.Not:
            case TokenKind.Tilde:
            {
                Token token = Next();
                UnaryOperator op = token.Kind switch
                {
                    TokenKind.Plus => UnaryOperator.Plus,
                    TokenKind.Minus => UnaryOperator.Negate,
                    TokenKind.Tilde => UnaryOperator.Peek,
                    _ => UnaryOperator.LogicalNot,
                };

                UnaryExpression unary = Mark(new UnaryExpression(op, ParseUnary()), start);
                if (token.Kind == TokenKind.Not)
                {
                    _notKeywordExpressions.Add(unary);
                }

                return unary;
            }
            case TokenKind.Caret:
            {
                Token caret = Next();
                if (_indexArgumentDepth == 0)
                {
                    AddDiagnostic(DiagnosticCodes.Syntax, "Index from end `^k` can only be used inside an indexer, as in `a[^1]`.", caret.Span);
                }

                return Mark(new FromEndExpression(ParseUnary()), start);
            }
            case TokenKind.OpenParen when TryParseCast(out Expression? cast):
                return cast!;
            default:
                return ParseUnaryOperand();
        }
    }

    // The operand level below the prefix operators: an application or a postfix expression. Pipe targets are
    // parsed at this level too.
    private Expression ParseUnaryOperand()
        => ParseApplication();

    private static readonly HashSet<string> PredefinedTypeNames =
    [
        "int", "long", "double", "decimal", "bool", "char", "string", "object",
        "byte", "sbyte", "short", "ushort", "uint", "ulong", "float", "nint", "nuint",
    ];

    // `(T)x` (spec §13.1 level 3). A parenthesized name followed by an operand is a cast when the type is
    // unmistakably a type (predefined, generic, array, nullable, tuple, or qualified) or when `)` touches the
    // operand as in `(Node)obj`. Otherwise `(f) x` stays an application with a parenthesized head.
    private bool TryParseCast(out Expression? cast)
    {
        cast = null;
        int start = _position;
        int savedDiagnostics = _diagnostics.Count;
        Next();
        TypeSyntax? type = TryParseTypeSyntax(allowSizedArrays: false);
        if (type is null || Current.Kind != TokenKind.CloseParen)
        {
            Restore(start, savedDiagnostics);
            return false;
        }

        Next();
        bool predefined = type is NamedTypeSyntax { TypeArguments.Count: 0 } simple && PredefinedTypeNames.Contains(simple.Name);
        bool unmistakable = predefined
            || type is ArrayTypeSyntax or NullableTypeSyntax or TupleTypeSyntax
            || type is NamedTypeSyntax named && (named.TypeArguments.Count > 0 || named.Name.Contains('.', StringComparison.Ordinal));
        bool adjacent = IsCurrentAdjacentToPreviousToken();
        bool operandStart = Current.Kind is TokenKind.IntegerLiteral
                or TokenKind.FloatLiteral
                or TokenKind.StringLiteral
                or TokenKind.InterpolatedStringLiteral
                or TokenKind.CharLiteral
                or TokenKind.True
                or TokenKind.False
                or TokenKind.Null
                or TokenKind.OpenParen
                or TokenKind.New
                or TokenKind.Bang
                or TokenKind.Tilde
                or TokenKind.Not
            || (Current.Kind == TokenKind.Identifier && !IsContextualOperatorWord(Current.Text));
        bool signStart = Current.Kind is TokenKind.Minus or TokenKind.Plus or TokenKind.PlusPlus or TokenKind.MinusMinus;
        if ((operandStart && (unmistakable || adjacent)) || (signStart && predefined))
        {
            cast = Mark(new CastExpression(type, ParseUnary()), start);
            return true;
        }

        Restore(start, savedDiagnostics);
        return false;
    }

    // Identifiers that continue an expression instead of starting an operand.
    private static bool IsContextualOperatorWord(string text)
        => text is "switch" or "with" or "as" or "when";

    // Space-call application (spec §11.2): `head group1 group2 ...`. A group is an atom (a primary with the
    // postfix operations written directly after it) or a parenthesized group whose elements are appended to the
    // argument list, so `min (a, b)` is `min(a, b)` and `f (a + b) x` is `f(a + b, x)`. Atoms never start with
    // a sign or prefix operator: `f -1` is a subtraction (spec §11.3).
    private Expression ParseApplication()
    {
        int start = _position;
        Expression head = ParsePostfix(allowLineLeadingMember: true);
        if (!IsApplicationHead(head))
        {
            return head;
        }

        List<ArgumentSyntax>? arguments = null;
        while (CanStartApplicationGroup())
        {
            arguments ??= [];
            ParseApplicationGroup(arguments);
        }

        if (arguments is null)
        {
            return head;
        }

        Expression call = Mark(new CallExpression(head, arguments, true), start);
        return ParsePostfixTail(call, start, allowLineLeadingMember: true, allowAdjacentOperations: false);
    }

    private static bool IsApplicationHead(Expression expression)
        => expression switch
        {
            IdentifierExpression identifier => identifier.Name is not "this" and not "base" and not "default",
            MemberAccessExpression { IsNullConditional: false } => true,
            _ => false,
        };

    private bool CanStartApplicationGroup()
        => Current.Kind switch
        {
            TokenKind.Identifier => !IsContextualOperatorWord(Current.Text) && Current.Text != "_",
            TokenKind.IntegerLiteral
                or TokenKind.FloatLiteral
                or TokenKind.StringLiteral
                or TokenKind.InterpolatedStringLiteral
                or TokenKind.CharLiteral
                or TokenKind.True
                or TokenKind.False
                or TokenKind.Null
                or TokenKind.OpenParen
                or TokenKind.OpenBracket
                or TokenKind.New
                or TokenKind.Ref
                or TokenKind.Out
                or TokenKind.In => true,
            _ => false,
        };

    private void ParseApplicationGroup(List<ArgumentSyntax> arguments)
    {
        int start = _position;
        if (Current.Kind == TokenKind.OpenParen && !LooksLikeParenthesizedLambda())
        {
            Next();
            List<ArgumentSyntax> elements = WithNestedContext(() =>
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
            Expect(TokenKind.CloseParen, "Expected ')' to close the argument group.");

            // `f (a + b).Length`: postfix operations written directly after the group make it one atom.
            if (elements.Count == 1
                && elements[0] is ExpressionArgumentSyntax { Modifier: ArgumentModifier.None, Name: null } single
                && IsCurrentAdjacentToPreviousToken()
                && Current.Kind is TokenKind.Dot or TokenKind.QuestionDot or TokenKind.OpenBracket)
            {
                Expression atom = ParsePostfixTail(single.Expression, start, allowLineLeadingMember: false, allowAdjacentOperations: true);
                arguments.Add(new ExpressionArgumentSyntax(null, ArgumentModifier.None, atom));
                return;
            }

            arguments.AddRange(elements);
            return;
        }

        arguments.Add(ParseAtomArgument());
    }

    private ArgumentSyntax ParseAtomArgument()
    {
        ArgumentModifier modifier = ParseOptionalArgumentModifier();
        if (modifier == ArgumentModifier.Out)
        {
            if (TryParseOutDeclaration(null, out ArgumentSyntax? declaration))
            {
                return declaration!;
            }

            if (Current.Kind == TokenKind.Identifier && Current.Text == "_")
            {
                Next();
                return new ExpressionArgumentSyntax(null, modifier, new DiscardExpression());
            }
        }

        return new ExpressionArgumentSyntax(null, modifier, ParsePostfix(allowLineLeadingMember: false));
    }

    private bool TryParseOutDeclaration(string? name, out ArgumentSyntax? declaration)
    {
        declaration = null;
        int savedPosition = _position;
        int savedDiagnostics = _diagnostics.Count;
        TypeSyntax? outType = Current.Kind == TokenKind.Var
            ? ParseVarKeywordAsType()
            : TryParseTypeSyntax(allowSizedArrays: false);
        if (outType is not null && Current.Kind is TokenKind.Identifier or TokenKind.OpenParen && TryParseBindingTarget(out BindingTarget? target))
        {
            declaration = new OutDeclarationArgumentSyntax(name, outType, target!);
            return true;
        }

        Restore(savedPosition, savedDiagnostics);
        return false;
    }

    private TypeSyntax ParseVarKeywordAsType()
    {
        Next();
        return new NamedTypeSyntax("var", Immutable.List<TypeSyntax>());
    }

    private Expression ParsePostfix(bool allowLineLeadingMember)
    {
        int start = _position;
        Expression expression = ParsePrimary();
        return ParsePostfixTail(expression, start, allowLineLeadingMember, allowAdjacentOperations: true);
    }

    // Postfix operations attach to the preceding primary only when written directly after it (`f(x)`, `a[i]`,
    // `p.1`). A line that starts with `.` or `?.` continues the expression (spec §4.2 rule 3).
    private Expression ParsePostfixTail(Expression expression, int start, bool allowLineLeadingMember, bool allowAdjacentOperations)
    {
        while (true)
        {
            bool lineContinuation = false;
            if (allowLineLeadingMember
                && Current.Kind == TokenKind.NewLine
                && NextNonNewLineKind(0) is TokenKind.Dot or TokenKind.QuestionDot)
            {
                SkipExpressionNewLines();
                lineContinuation = true;
            }

            bool adjacent = lineContinuation || (allowAdjacentOperations && IsCurrentAdjacentToPreviousToken());
            if (!adjacent)
            {
                return expression;
            }

            switch (Current.Kind)
            {
                case TokenKind.OpenParen when !lineContinuation:
                    Next();
                    expression = Mark(new CallExpression(expression, ParseCallArgumentsTail(), false), start);
                    continue;
                case TokenKind.OpenBracket when !lineContinuation:
                    Next();
                    expression = Mark(new IndexExpression(expression, ParseIndexArgumentsTail()), start);
                    continue;
                case TokenKind.Question when !lineContinuation
                    && Peek(1).Kind == TokenKind.OpenBracket
                    && Peek(1).Position == Current.Position + 1:
                    Next();
                    Next();
                    expression = Mark(new IndexExpression(expression, ParseIndexArgumentsTail(), IsNullConditional: true), start);
                    continue;
                case TokenKind.Dot:
                    Next();
                    expression = Mark(ParseMemberOrProjection(expression, nullConditional: false), start);
                    continue;
                case TokenKind.QuestionDot:
                    Next();
                    expression = Mark(ParseMemberOrProjection(expression, nullConditional: true), start);
                    continue;
                case TokenKind.PlusPlus when !lineContinuation:
                    Next();
                    expression = Mark(new PostfixExpression(expression, PostfixOperator.Increment), start);
                    continue;
                case TokenKind.MinusMinus when !lineContinuation:
                    Next();
                    expression = Mark(new PostfixExpression(expression, PostfixOperator.Decrement), start);
                    continue;
                case TokenKind.Bang when !lineContinuation:
                    Next();
                    expression = Mark(new NullForgivingExpression(expression), start);
                    continue;
                default:
                    return expression;
            }
        }
    }

    private Expression ParseMemberOrProjection(Expression receiver, bool nullConditional)
    {
        if (!nullConditional && Current.Kind == TokenKind.IntegerLiteral)
        {
            if (!int.TryParse(Current.Text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int position) || position < 1)
            {
                AddDiagnostic(DiagnosticCodes.Syntax, "Tuple projection index must be a positive integer (`.1`, `.2`, ...).", Current.Span);
                position = 1;
            }

            Next();
            return new TupleProjectionExpression(receiver, position);
        }

        Token nameToken = Current;
        string memberName = ParseIdentifierWithOptionalGenericSuffix();
        MemberAccessExpression member = new(receiver, memberName, nullConditional);
        _spans.SetName(member, nameToken.Span);
        return member;
    }

    // `receiver with { Member = value, ... }`: member names are C#, values are ordinary PSCP expressions.
    private Expression ParseWithExpression(Expression receiver)
    {
        Expect(TokenKind.Identifier, "Expected 'with'.");
        SkipSeparators();
        IReadOnlyList<WithAssignment> assignments = ParseMemberInitializerList("with");
        return new WithExpression(receiver, assignments);
    }

    // `{ Member = value, ... }` after `with` or `new T(...)`.
    private IReadOnlyList<WithAssignment> ParseMemberInitializerList(string owner)
    {
        Expect(TokenKind.OpenBrace, $"Expected '{{' after '{owner}'.");
        List<WithAssignment> assignments = WithNestedContext(() =>
        {
            List<WithAssignment> parsed = [];
            SkipSeparators();
            while (Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
            {
                string memberName = Expect(TokenKind.Identifier, $"Expected a member name in the '{owner}' initializer.").Text;
                Expect(TokenKind.Equal, "Expected '=' after the member name.");
                SkipSeparators();
                parsed.Add(new WithAssignment(memberName, ParseExpression()));
                SkipSeparators();
                if (!Match(TokenKind.Comma))
                {
                    break;
                }

                SkipSeparators();
            }

            return parsed;
        });
        Expect(TokenKind.CloseBrace, $"Expected '}}' to close the '{owner}' initializer.");
        return assignments;
    }

    // `receiver switch { pattern [when guard] => result, ... }`. Arms are separated by `,` or a line break.
    // Patterns are C# pass-through (spec §13.9); guards and results are PSCP expressions.
    private Expression ParseSwitchExpression(Expression receiver)
    {
        Expect(TokenKind.Identifier, "Expected 'switch'.");
        SkipSeparators();
        Expect(TokenKind.OpenBrace, "Expected '{' after 'switch'.");
        List<SwitchArm> arms = WithNestedContext(() =>
        {
            List<SwitchArm> parsed = [];
            SkipSeparators();
            while (Current.Kind is not TokenKind.CloseBrace and not TokenKind.EndOfFile)
            {
                Token first = Current;
                PatternSyntax pattern = ParsePattern(first, inSwitchArm: true);
                Expression? guard = null;
                if (Current.Kind == TokenKind.Identifier && Current.Text == "when")
                {
                    Next();
                    SkipSeparators();
                    guard = ParseExpression();
                    SkipSeparators();
                }

                Expect(TokenKind.FatArrow, "Expected '=>' in switch expression arm.");
                SkipSeparators();
                Expression result = ParseExpression();
                parsed.Add(new SwitchArm(pattern, guard, result));
                bool lineBreak = Current.Kind == TokenKind.NewLine;
                SkipSeparators();
                if (!Match(TokenKind.Comma) && !lineBreak)
                {
                    break;
                }

                SkipSeparators();
            }

            return parsed;
        });
        Expect(TokenKind.CloseBrace, "Expected '}' to close the switch expression.");
        return new SwitchExpression(receiver, arms);
    }

    // Pattern context (spec §13.9): the C# 10 pattern text runs until a token that cannot continue a pattern.
    // `and`, `or` and `not` are pattern combinators here. In the head of `if`/`while` a `{` starts the body.
    private PatternSyntax ParsePattern(Token introducer, bool inSwitchArm)
    {
        int start = _position;
        int depth = 0;
        while (Current.Kind != TokenKind.EndOfFile)
        {
            TokenKind kind = Current.Kind;
            if (depth == 0)
            {
                bool ends = kind is TokenKind.NewLine
                        or TokenKind.Semicolon
                        or TokenKind.AmpAmp
                        or TokenKind.PipePipe
                        or TokenKind.Question
                        or TokenKind.QuestionQuestion
                        or TokenKind.Colon
                        or TokenKind.FatArrow
                        or TokenKind.Then
                        or TokenKind.Do
                        or TokenKind.Else
                        or TokenKind.Comma
                        or TokenKind.CloseParen
                        or TokenKind.CloseBracket
                        or TokenKind.CloseBrace
                        or TokenKind.Equal
                        or TokenKind.EqualEqual
                        or TokenKind.BangEqual
                        or TokenKind.PipeGreater
                        or TokenKind.LessPipe
                        or TokenKind.Arrow
                    || (kind == TokenKind.Identifier && Current.Text == "when")
                    || (kind == TokenKind.OpenBrace && _inStatementHead && !inSwitchArm);
                if (ends)
                {
                    break;
                }
            }

            if (kind is TokenKind.OpenParen or TokenKind.OpenBracket or TokenKind.OpenBrace)
            {
                depth++;
            }
            else if (kind is TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.CloseBrace)
            {
                depth--;
            }

            Next();
        }

        int end = _position;
        if (end == start)
        {
            AddDiagnostic(DiagnosticCodes.Syntax, inSwitchArm ? "Expected a pattern in the switch arm." : "Expected a pattern after `is`.", Current.Span);
            return new PatternSyntax("_", Immutable.List<PatternDesignation>());
        }

        PatternSyntax pattern = new(TokensToText(start, end), CollectPatternDesignations(start, end))
        {
            SimpleType = TryGetSimplePatternType(start, end),
        };
        Mark(pattern, start);
        return pattern;
    }

    // The pattern is exactly a type, optionally followed by a designation: `int`, `int v`, `List<int> xs`.
    private TypeSyntax? TryGetSimplePatternType(int start, int end)
    {
        TypeSyntax? whole = TryParseTypeFromTokens(start, end);
        if (whole is not null)
        {
            return whole;
        }

        if (end - start >= 2 && _tokens[end - 1].Kind == TokenKind.Identifier)
        {
            return TryParseTypeFromTokens(start, end - 1);
        }

        return null;
    }

    // Names declared by a pattern: an identifier that directly follows a type (or `var`, or a positional or
    // property pattern) and ends its sub-pattern: `int v`, `var (a, b)`, `(int, int) t`, `{ Length: > 0 } s`.
    private IReadOnlyList<PatternDesignation> CollectPatternDesignations(int start, int end)
    {
        List<PatternDesignation> designations = [];
        for (int i = start; i < end; i++)
        {
            Token token = _tokens[i];
            if (token.Kind != TokenKind.Identifier || token.Text == "_" || i == start)
            {
                continue;
            }

            TokenKind next = i + 1 < end ? _tokens[i + 1].Kind : TokenKind.EndOfFile;
            bool endsSubpattern = next is TokenKind.EndOfFile or TokenKind.Comma or TokenKind.CloseParen or TokenKind.CloseBrace
                or TokenKind.CloseBracket or TokenKind.And or TokenKind.Or;
            if (!endsSubpattern)
            {
                continue;
            }

            if (IsInsideVarDesignation(start, i))
            {
                designations.Add(new PatternDesignation(token.Text, null));
                continue;
            }

            Token previous = _tokens[i - 1];
            switch (previous.Kind)
            {
                case TokenKind.Var:
                    designations.Add(new PatternDesignation(token.Text, null));
                    break;
                case TokenKind.CloseParen:
                case TokenKind.CloseBrace:
                    designations.Add(new PatternDesignation(token.Text, TryParseDesignationType(start, i)));
                    break;
                case TokenKind.Identifier:
                case TokenKind.CloseBracket:
                case TokenKind.Question:
                    designations.Add(new PatternDesignation(token.Text, TryParseDesignationType(start, i)));
                    break;
                case TokenKind.GreaterThan when i - 2 >= start && _tokens[i - 2].Kind is TokenKind.Identifier or TokenKind.GreaterThan or TokenKind.CloseBracket or TokenKind.CloseParen or TokenKind.Question:
                    // `List<int> xs`: the `>` closes a type argument list rather than starting a relational pattern.
                    designations.Add(new PatternDesignation(token.Text, TryParseDesignationType(start, i)));
                    break;
            }
        }

        return designations;
    }

    // The longest token run before the designation that parses as a type and starts at a sub-pattern boundary.
    private TypeSyntax? TryParseDesignationType(int patternStart, int designationIndex)
    {
        for (int typeStart = patternStart; typeStart < designationIndex; typeStart++)
        {
            if (typeStart > patternStart
                && _tokens[typeStart - 1].Kind is not (TokenKind.OpenParen or TokenKind.Comma or TokenKind.And or TokenKind.Or or TokenKind.Not or TokenKind.Colon or TokenKind.OpenBrace))
            {
                continue;
            }

            TypeSyntax? type = TryParseTypeFromTokens(typeStart, designationIndex);
            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }

    private bool IsInsideVarDesignation(int start, int index)
    {
        int depth = 0;
        for (int i = index - 1; i >= start; i--)
        {
            TokenKind kind = _tokens[i].Kind;
            if (kind == TokenKind.CloseParen)
            {
                depth++;
            }
            else if (kind == TokenKind.OpenParen)
            {
                if (depth == 0)
                {
                    return i > start && _tokens[i - 1].Kind == TokenKind.Var || IsInsideVarDesignation(start, i);
                }

                depth--;
            }
        }

        return false;
    }

    private TypeSyntax? TryParseTypeFromTokens(int start, int end)
    {
        if (end <= start)
        {
            return null;
        }

        List<Token> slice = [];
        for (int i = start; i < end; i++)
        {
            if (_tokens[i].Kind != TokenKind.NewLine)
            {
                slice.Add(_tokens[i]);
            }
        }

        if (slice.Count == 0)
        {
            return null;
        }

        Token last = slice[^1];
        slice.Add(new Token(TokenKind.EndOfFile, string.Empty, last.Position + last.Text.Length));
        Parser typeParser = new(slice);
        TypeSyntax? type = typeParser.TryParseTypeSyntax(allowSizedArrays: false);
        return type is not null && typeParser.Current.Kind == TokenKind.EndOfFile && typeParser._diagnostics.Count == 0 ? type : null;
    }

    private Expression ParsePrimary()
    {
        int start = _position;
        switch (Current.Kind)
        {
            case TokenKind.IntegerLiteral:
                return Mark(new LiteralExpression(LiteralKind.Integer, Next().Text), start);
            case TokenKind.FloatLiteral:
                return Mark(new LiteralExpression(LiteralKind.Float, Next().Text), start);
            case TokenKind.StringLiteral:
                return Mark(new LiteralExpression(LiteralKind.String, Next().Text), start);
            case TokenKind.InterpolatedStringLiteral:
                return Mark(ParseInterpolatedStringExpression(), start);
            case TokenKind.CharLiteral:
                return Mark(new LiteralExpression(LiteralKind.Char, Next().Text), start);
            case TokenKind.True:
                return Mark(new LiteralExpression(LiteralKind.True, Next().Text), start);
            case TokenKind.False:
                return Mark(new LiteralExpression(LiteralKind.False, Next().Text), start);
            case TokenKind.Null:
                return Mark(new LiteralExpression(LiteralKind.Null, Next().Text), start);
            case TokenKind.OpenParen:
                return Mark(LooksLikeParenthesizedLambda() ? ParseParenthesizedLambda() : ParseParenthesizedOrTuple(), start);
            case TokenKind.OpenBrace:
                return Mark(new BlockExpression(WithNestedContext(ParseBlockStatement)), start);
            case TokenKind.OpenBracket:
                return Mark(ParseCollectionExpression(), start);
            case TokenKind.If:
                return Mark(ParseIfExpression(), start);
            case TokenKind.New:
                return Mark(ParseNewExpression(), start);
            case TokenKind.Identifier:
                return ParseIdentifierBasedPrimary(start);
            default:
                AddDiagnostic(DiagnosticCodes.Syntax, $"Unexpected token '{DescribeToken(Current)}' in expression.", Current.Span);
                Token unexpected = Current;
                if (Current.Kind is not (TokenKind.EndOfFile or TokenKind.NewLine or TokenKind.CloseBrace or TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.Semicolon))
                {
                    Next();
                }

                return Mark(new IdentifierExpression(unexpected.Text.Length == 0 ? "default" : unexpected.Text), start);
        }
    }

    private static string DescribeToken(Token token)
        => token.Kind switch
        {
            TokenKind.EndOfFile => "end of file",
            TokenKind.NewLine => "end of line",
            _ => token.Text,
        };

    private Expression ParseIdentifierBasedPrimary(int start)
    {
        if (Current.Text == "_" && Peek(1).Kind == TokenKind.FatArrow)
        {
            return Mark(ParseSingleParameterLambda(), start);
        }

        if (Current.Text == "_")
        {
            Token discard = Next();
            if (!IsAssignmentOperator(Current.Kind))
            {
                AddDiagnostic(DiagnosticCodes.Syntax, "Discard '_' cannot be used as a value.", discard.Span);
            }

            return Mark(new DiscardExpression(), start);
        }

        if (Peek(1).Kind == TokenKind.FatArrow)
        {
            return Mark(ParseSingleParameterLambda(), start);
        }

        if (Current.Text == "throw")
        {
            Next();
            return Mark(new ThrowExpression(ParseExpression()), start);
        }

        string name = ParseIdentifierWithOptionalGenericSuffix();
        if (Current.Kind == TokenKind.OpenBrace && Peek(1).Kind == TokenKind.For)
        {
            return Mark(ParseAggregationExpression(name), start);
        }

        return Mark(new IdentifierExpression(name), start);
    }

    private Expression ParseSingleParameterLambda()
    {
        int start = _position;
        BindingTarget parameter = ParseBindingTarget();
        Expect(TokenKind.FatArrow, "Expected '=>' in lambda expression.");
        LambdaParameter lambdaParameter = Mark(new LambdaParameter(ArgumentModifier.None, null, parameter), start);
        return new LambdaExpression(Immutable.List(lambdaParameter), ParseLambdaBody());
    }

    private LambdaBody ParseLambdaBody()
    {
        SkipExpressionNewLines();
        return Current.Kind == TokenKind.OpenBrace
            ? new LambdaBlockBody(WithNestedContext(ParseBlockStatement))
            : new LambdaExpressionBody(WithNestedContext(ParseExpression));
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
        return new LambdaExpression(parameters, ParseLambdaBody());
    }

    private LambdaParameter ParseLambdaParameter()
    {
        int start = _position;
        int savedDiagnostics = _diagnostics.Count;
        ArgumentModifier modifier = ParseOptionalArgumentModifier();
        TypeSyntax? type = TryParseTypeSyntax(allowSizedArrays: false);
        if (type is not null && TryParseBindingTarget(out BindingTarget? typedTarget))
        {
            return Mark(new LambdaParameter(modifier, type, typedTarget!), start);
        }

        Restore(start, savedDiagnostics);
        return Mark(new LambdaParameter(ArgumentModifier.None, null, ParseBindingTarget()), start);
    }

    private Expression ParseParenthesizedOrTuple()
    {
        Expect(TokenKind.OpenParen, "Expected '('.");
        Expression result = WithNestedContext(() =>
        {
            SkipSeparators();
            if (Current.Kind == TokenKind.For)
            {
                return ParseForGeneratorExpression();
            }

            Expression first = ParseExpression();
            if (Match(TokenKind.Arrow))
            {
                return ParseArrowGeneratorExpression(first);
            }

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
        });

        SkipSeparators();
        Expect(TokenKind.CloseParen, result is TupleExpression ? "Expected ')' after tuple expression." : "Expected ')' after parenthesized expression.");
        return result;
    }

    private GeneratorExpression ParseArrowGeneratorExpression(Expression source)
    {
        (BindingTarget? indexTarget, BindingTarget itemTarget) = ParseIterationBinding();
        LambdaBody body = ParseGeneratorBody();
        return new GeneratorExpression(indexTarget, itemTarget, source, body);
    }

    private GeneratorExpression ParseForGeneratorExpression()
    {
        Expect(TokenKind.For, "Expected 'for' in generator expression.");
        (BindingTarget? indexTarget, BindingTarget itemTarget) = ParseIterationBinding();
        Expect(TokenKind.In, "Expected 'in' in generator expression.");
        Expression source = ParseExpression();
        LambdaBody body = ParseGeneratorBody();
        return new GeneratorExpression(indexTarget, itemTarget, source, body);
    }

    // `x`, `i, x`, `(a, b)` or `i, (a, b)` (spec §16.4).
    private (BindingTarget? IndexTarget, BindingTarget ItemTarget) ParseIterationBinding()
    {
        BindingTarget first = ParseBindingTarget();
        if (Match(TokenKind.Comma))
        {
            return (first, ParseBindingTarget());
        }

        return (null, first);
    }

    private LambdaBody ParseGeneratorBody()
    {
        if (Match(TokenKind.Do))
        {
            return ParseLambdaBody();
        }

        if (Current.Kind == TokenKind.OpenBrace)
        {
            return new LambdaBlockBody(WithNestedContext(ParseBlockStatement));
        }

        AddDiagnostic(DiagnosticCodes.Syntax, "Expected `do` or a block body in the generator.", Current.Span);
        return new LambdaExpressionBody(ParseExpression());
    }

    private bool TryParseBareGeneratorExpression(out Expression? expression)
    {
        int savedPosition = _position;
        int savedDiagnostics = _diagnostics.Count;
        expression = null;

        if (Current.Kind == TokenKind.For)
        {
            expression = Mark(ParseForGeneratorExpression(), savedPosition);
            return true;
        }

        Expression source = ParseExpression();
        if (Match(TokenKind.Arrow))
        {
            expression = Mark(ParseArrowGeneratorExpression(source), savedPosition);
            return true;
        }

        Restore(savedPosition, savedDiagnostics);
        return false;
    }

    // Expression position `if` requires `else` (spec §12.1).
    private Expression ParseIfExpression()
    {
        Token ifToken = Expect(TokenKind.If, "Expected 'if'.");
        Expression condition = ParseExpression();
        SkipExpressionNewLines();
        Expect(TokenKind.Then, "Expected 'then' in the `if` expression.");
        SkipExpressionNewLines();
        Expression thenExpression = ParseExpression();
        int beforeElse = _position;
        SkipSeparators();
        if (Current.Kind != TokenKind.Else)
        {
            _position = beforeElse;
            AddDiagnostic(
                DiagnosticCodes.IfExpressionWithoutElse,
                "An `if` used as a value needs an `else` branch: `if c then a else b`.",
                ifToken.Span);
            return new IfExpression(condition, thenExpression, new IdentifierExpression("default"));
        }

        Next();
        SkipSeparators();
        Expression elseExpression = ParseExpression();
        return new IfExpression(condition, thenExpression, elseExpression);
    }

    private InterpolatedStringExpression ParseInterpolatedStringExpression()
    {
        Token token = Expect(TokenKind.InterpolatedStringLiteral, "Expected interpolated string literal.");
        return new InterpolatedStringExpression(ParseInterpolatedStringParts(token));
    }

    // `$"..."` and `$@"..."`. A hole is `{expression[,alignment][:format]}`; the `,` and `:` count only outside
    // brackets and literals, so a conditional must be parenthesized (spec §19).
    private IReadOnlyList<InterpolatedStringPart> ParseInterpolatedStringParts(Token token)
    {
        List<InterpolatedStringPart> parts = [];
        System.Text.StringBuilder text = new();
        string raw = token.Text;
        bool verbatim = raw.StartsWith("$@", StringComparison.Ordinal) || raw.StartsWith("@$", StringComparison.Ordinal);
        int index = verbatim ? 3 : 2;
        int end = raw.Length > 0 && raw[^1] == '"' ? raw.Length - 1 : raw.Length;

        while (index < end)
        {
            char ch = raw[index];
            if (ch == '{')
            {
                if (index + 1 < end && raw[index + 1] == '{')
                {
                    text.Append('{');
                    index += 2;
                    continue;
                }

                FlushTextPart();
                int holeStart = index + 1;
                int holeEnd = FindInterpolationHoleEnd(raw, holeStart, end);
                parts.Add(ParseInterpolationHole(raw[holeStart..holeEnd], token.Position + holeStart));
                index = Math.Min(end, holeEnd + 1);
                continue;
            }

            if (ch == '}' && index + 1 < end && raw[index + 1] == '}')
            {
                text.Append('}');
                index += 2;
                continue;
            }

            if (verbatim)
            {
                if (ch == '"' && index + 1 < end && raw[index + 1] == '"')
                {
                    text.Append('"');
                    index += 2;
                    continue;
                }

                text.Append(ch);
                index++;
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

    // Index of the `}` closing the hole that starts at `start`.
    private static int FindInterpolationHoleEnd(string raw, int start, int end)
    {
        int depth = 0;
        int index = start;
        while (index < end)
        {
            char ch = raw[index];
            if (ch is '"' or '\'')
            {
                index = SkipStringLike(raw, index, ch);
                continue;
            }

            if (ch is '(' or '[' or '{')
            {
                depth++;
            }
            else if (ch is ')' or ']')
            {
                depth--;
            }
            else if (ch == '}')
            {
                if (depth == 0)
                {
                    return index;
                }

                depth--;
            }

            index++;
        }

        return end;
    }

    private InterpolatedStringInterpolationPart ParseInterpolationHole(string holeText, int sourceOffset)
    {
        (int expressionLength, string? alignment, string? format) = SplitInterpolationHole(holeText);
        Expression expression = ParseInterpolatedHoleExpression(holeText[..expressionLength], sourceOffset);
        return new InterpolatedStringInterpolationPart(expression, alignment, format);
    }

    // Splits `expr,align:format` at the first top-level `,` and `:`.
    private static (int ExpressionLength, string? Alignment, string? Format) SplitInterpolationHole(string hole)
    {
        int depth = 0;
        int alignmentStart = -1;
        for (int i = 0; i < hole.Length; i++)
        {
            char ch = hole[i];
            if (ch is '"' or '\'')
            {
                i = SkipStringLike(hole, i, ch) - 1;
                continue;
            }

            if (ch is '(' or '[' or '{')
            {
                depth++;
            }
            else if (ch is ')' or ']' or '}')
            {
                depth--;
            }
            else if (depth == 0 && ch == ',' && alignmentStart < 0)
            {
                alignmentStart = i;
            }
            else if (depth == 0 && ch == ':' && !(i + 1 < hole.Length && hole[i + 1] == '='))
            {
                int expressionLength = alignmentStart >= 0 ? alignmentStart : i;
                string? alignment = alignmentStart >= 0 ? hole[(alignmentStart + 1)..i].Trim() : null;
                return (expressionLength, alignment, hole[(i + 1)..]);
            }
        }

        return alignmentStart >= 0
            ? (alignmentStart, hole[(alignmentStart + 1)..].Trim(), null)
            : (hole.Length, null, null);
    }

    private Expression ParseInterpolatedHoleExpression(string expressionText, int sourceOffset)
    {
        Lexer lexer = new(expressionText);
        IReadOnlyList<Token> tokens = lexer.Lex();
        AddInterpolatedDiagnostics(lexer.Diagnostics, sourceOffset);

        Parser parser = new(tokens);
        Expression expression = parser.ParseExpression();
        parser.SkipSeparators();
        if (parser.Current.Kind != TokenKind.EndOfFile)
        {
            AddDiagnostic(
                DiagnosticCodes.Syntax,
                "Unexpected trailing tokens in interpolated-string expression.",
                new TextSpan(sourceOffset + parser.Current.Span.Start, parser.Current.Span.Length));
        }

        AddInterpolatedDiagnostics(parser.Diagnostics, sourceOffset);
        parser._spans.CopyShiftedTo(_spans, sourceOffset);
        foreach (UnaryExpression notExpression in parser._notKeywordExpressions)
        {
            _notKeywordExpressions.Add(notExpression);
        }

        return expression;
    }

    private void AddInterpolatedDiagnostics(IReadOnlyList<Diagnostic> diagnostics, int sourceOffset)
    {
        foreach (Diagnostic diagnostic in diagnostics)
        {
            _diagnostics.Add(diagnostic with { Span = new TextSpan(sourceOffset + diagnostic.Span.Start, diagnostic.Span.Length) });
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
