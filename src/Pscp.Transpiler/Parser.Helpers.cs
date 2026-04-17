namespace Pscp.Transpiler;

public sealed partial class Parser
{
    private static readonly HashSet<string> ModifierNames =
    [
        "public",
        "private",
        "protected",
        "internal",
        "static",
        "readonly",
        "sealed",
        "abstract",
        "partial",
        "virtual",
        "override",
        "unsafe",
        "extern",
        "async",
        "required",
    ];

    private static readonly HashSet<string> SectionAccessNames =
    [
        "public",
        "private",
        "protected",
        "internal",
    ];

    private bool TryReadAssignmentOperator(out AssignmentOperator assignmentOperator)
    {
        assignmentOperator = Current.Kind switch
        {
            TokenKind.Equal => AssignmentOperator.Assign,
            TokenKind.PlusEqual => AssignmentOperator.AddAssign,
            TokenKind.MinusEqual => AssignmentOperator.SubtractAssign,
            TokenKind.StarEqual => AssignmentOperator.MultiplyAssign,
            TokenKind.SlashEqual => AssignmentOperator.DivideAssign,
            TokenKind.PercentEqual => AssignmentOperator.ModuloAssign,
            _ => AssignmentOperator.Assign,
        };

        if (Current.Kind is not TokenKind.Equal
            and not TokenKind.PlusEqual
            and not TokenKind.MinusEqual
            and not TokenKind.StarEqual
            and not TokenKind.SlashEqual
            and not TokenKind.PercentEqual)
        {
            return false;
        }

        Next();
        return true;
    }

    private bool TryReadShiftOperator(out BinaryOperator binaryOperator)
    {
        binaryOperator = BinaryOperator.Add;
        if (Current.Kind == TokenKind.LessThan
            && Peek(1).Kind == TokenKind.LessThan
            && Current.Position + Current.Text.Length == Peek(1).Position)
        {
            Next();
            Next();
            binaryOperator = BinaryOperator.ShiftLeft;
            return true;
        }

        if (Current.Kind == TokenKind.GreaterThan
            && Peek(1).Kind == TokenKind.GreaterThan
            && Current.Position + Current.Text.Length == Peek(1).Position)
        {
            Next();
            Next();
            binaryOperator = BinaryOperator.ShiftRight;
            return true;
        }

        return false;
    }

    private bool TryConsumeSemicolon()
    {
        if (Current.Kind == TokenKind.Semicolon)
        {
            Next();
            return true;
        }

        return false;
    }

    private void SkipSeparators()
    {
        while (Current.Kind is TokenKind.NewLine or TokenKind.Semicolon)
        {
            Next();
        }
    }

    private bool IsStatementTerminator(TokenKind kind)
        => kind is TokenKind.NewLine or TokenKind.Semicolon or TokenKind.CloseBrace or TokenKind.EndOfFile;

    private bool ShouldParseThenBranchAsStatement()
        => Current.Kind == TokenKind.NewLine
            || StartsStatementOnlyForm(Current.Kind)
            || !HasElseBeforeCurrentStatementTerminator();

    private static bool StartsStatementOnlyForm(TokenKind kind)
        => kind is TokenKind.Return
            or TokenKind.Break
            or TokenKind.Continue
            or TokenKind.If
            or TokenKind.While
            or TokenKind.For
            or TokenKind.Equal
            or TokenKind.PlusEqual
            or TokenKind.Let
            or TokenKind.Var
            or TokenKind.Mut;

    private bool HasElseBeforeCurrentStatementTerminator()
    {
        int parenDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;

        for (int i = _position; i < _tokens.Count; i++)
        {
            TokenKind kind = _tokens[i].Kind;
            switch (kind)
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
                    if (braceDepth == 0 && parenDepth == 0 && bracketDepth == 0)
                    {
                        return false;
                    }

                    if (braceDepth > 0)
                    {
                        braceDepth--;
                    }
                    break;
                case TokenKind.NewLine:
                case TokenKind.Semicolon:
                case TokenKind.EndOfFile:
                    if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                    {
                        return false;
                    }
                    break;
                case TokenKind.Else:
                    if (parenDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                    {
                        return true;
                    }
                    break;
            }
        }

        return false;
    }

    private static bool IsTypeNameToken(TokenKind kind)
        => kind == TokenKind.Identifier;

    private bool TryParseIdentifier(out string? name)
    {
        name = null;
        if (Current.Kind != TokenKind.Identifier)
        {
            return false;
        }

        name = Next().Text;
        return true;
    }

    private Token Expect(TokenKind kind, string message)
    {
        if (Current.Kind == kind)
        {
            return Next();
        }

        _diagnostics.Add(new Diagnostic(message, Current.Span));
        return new Token(kind, string.Empty, Current.Position);
    }

    private Token ExpectTypeNameToken(string message)
    {
        if (IsTypeNameToken(Current.Kind))
        {
            return Next();
        }

        _diagnostics.Add(new Diagnostic(message, Current.Span));
        return new Token(TokenKind.Identifier, "object", Current.Position);
    }

    private bool Match(TokenKind kind)
    {
        if (Current.Kind != kind)
        {
            return false;
        }

        Next();
        return true;
    }

    private Token Next()
    {
        Token token = Current;
        if (_position < _tokens.Count - 1)
        {
            _position++;
        }

        return token;
    }

    private Token Peek(int offset)
    {
        int index = Math.Min(_position + offset, _tokens.Count - 1);
        return _tokens[index];
    }

    private void Restore(int position, int diagnosticsCount)
    {
        _position = position;
        if (_diagnostics.Count > diagnosticsCount)
        {
            _diagnostics.RemoveRange(diagnosticsCount, _diagnostics.Count - diagnosticsCount);
        }
    }

    private IReadOnlyList<string> ParseModifiers()
    {
        List<string> modifiers = [];
        while (Current.Kind == TokenKind.Identifier
            && ModifierNames.Contains(Current.Text)
            && !IsSectionLabelStart())
        {
            modifiers.Add(Next().Text);
        }

        return modifiers;
    }

    private bool IsSectionLabelStart()
        => Current.Kind == TokenKind.Identifier
            && SectionAccessNames.Contains(Current.Text)
            && Peek(1).Kind == TokenKind.Colon;

    private ArgumentModifier ParseOptionalArgumentModifier()
    {
        if (Match(TokenKind.Ref))
        {
            return ArgumentModifier.Ref;
        }

        if (Match(TokenKind.Out))
        {
            return ArgumentModifier.Out;
        }

        if (Match(TokenKind.In))
        {
            return ArgumentModifier.In;
        }

        return ArgumentModifier.None;
    }

    private string TokensToText(int startInclusive, int endExclusive)
    {
        if (endExclusive <= startInclusive)
        {
            return string.Empty;
        }

        System.Text.StringBuilder builder = new();
        Token? previous = null;
        for (int i = startInclusive; i < endExclusive; i++)
        {
            Token token = _tokens[i];
            if (token.Kind == TokenKind.NewLine)
            {
                continue;
            }

            if (previous is not null && NeedsSpace(previous, token))
            {
                builder.Append(' ');
            }

            builder.Append(token.Text);
            previous = token;
        }

        return builder.ToString();
    }

    private static bool NeedsSpace(Token previous, Token current)
    {
        if (previous.Text.Length == 0 || current.Text.Length == 0)
        {
            return false;
        }

        char left = previous.Text[^1];
        char right = current.Text[0];
        return (char.IsLetterOrDigit(left) || left == '_')
            && (char.IsLetterOrDigit(right) || right == '_');
    }


    private bool IsCurrentAdjacentToPreviousToken()
    {
        if (_position == 0)
        {
            return true;
        }

        Token previous = _tokens[Math.Max(0, _position - 1)];
        return previous.Position + previous.Text.Length == Current.Position;
    }
    private Token Current => _tokens[Math.Min(_position, _tokens.Count - 1)];
}

