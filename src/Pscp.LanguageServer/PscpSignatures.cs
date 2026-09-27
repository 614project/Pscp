using Pscp.Transpiler;

namespace Pscp.LanguageServer;

// Signature help for space-call (guide §9.1, spec §11.2). A space-call has no brackets to key off, so the
// head and the argument count come from the token run between the statement start and the cursor.
internal static class PscpSignatures
{
    public static bool TryFindSpaceCall(PscpAnalysisResult analysis, int offset, out string? key, out int activeParameter)
    {
        key = null;
        activeParameter = 0;
        IReadOnlyList<Token> tokens = analysis.Tokens;
        int cursor = FirstTokenAtOrAfter(tokens, offset);
        int statementStart = StatementStart(tokens, cursor);

        // The head is the last name before the cursor that starts an application and has a signature.
        for (int head = cursor - 1; head >= statementStart; head--)
        {
            if (tokens[head].Kind != TokenKind.Identifier || !IsHeadPosition(tokens, head, statementStart))
            {
                continue;
            }

            string name = BuildName(tokens, head);
            if (!analysis.Signatures.ContainsKey(name) && !analysis.Signatures.ContainsKey(LastSegment(name)))
            {
                continue;
            }

            int nameEnd = head;
            while (nameEnd + 2 < tokens.Count && tokens[nameEnd + 1].Kind == TokenKind.Dot)
            {
                nameEnd += 2;
            }

            // A parenthesised call is not a space-call; the caller's bracket search already handled it.
            if (nameEnd + 1 < tokens.Count && tokens[nameEnd + 1].Kind == TokenKind.OpenParen
                && tokens[nameEnd + 1].Position == tokens[nameEnd].Position + tokens[nameEnd].Text.Length)
            {
                return false;
            }

            int arguments = CountSpaceCallArguments(tokens, nameEnd + 1, offset);
            if (arguments == 0)
            {
                return false;
            }

            key = name;
            activeParameter = arguments - 1;
            return true;
        }

        return false;
    }

    // Guide §9.4: the overload whose shape fits the arguments typed so far.
    public static int ChooseOverload(IReadOnlyList<PscpSignatureForm> forms, int argumentCount)
    {
        int best = 0;
        int bestScore = int.MinValue;
        for (int i = 0; i < forms.Count; i++)
        {
            PscpSignatureForm form = forms[i];
            int named = form.Parameters.Count(parameter => parameter.Label != "...");
            // An exact fit wins; a variadic form accepts anything from its named count upwards.
            int score = named == argumentCount ? 100
                : form.IsVariadic && argumentCount >= named ? 50
                : -Math.Abs(named - argumentCount);
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }

        return best;
    }

    private static int FirstTokenAtOrAfter(IReadOnlyList<Token> tokens, int offset)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Position >= offset || tokens[i].Kind == TokenKind.EndOfFile)
            {
                return i;
            }
        }

        return tokens.Count;
    }

    // Spec §11.2: a space-call lives inside one statement, so the search never crosses a statement boundary.
    private static int StatementStart(IReadOnlyList<Token> tokens, int cursor)
    {
        int depth = 0;
        for (int i = cursor - 1; i >= 0; i--)
        {
            switch (tokens[i].Kind)
            {
                case TokenKind.CloseParen:
                case TokenKind.CloseBracket:
                    depth++;
                    break;
                case TokenKind.OpenParen:
                case TokenKind.OpenBracket:
                    if (depth == 0)
                    {
                        return i + 1;
                    }

                    depth--;
                    break;
                case TokenKind.NewLine:
                case TokenKind.Semicolon:
                case TokenKind.OpenBrace:
                case TokenKind.CloseBrace:
                case TokenKind.Then:
                case TokenKind.Else:
                case TokenKind.Do:
                case TokenKind.Comma:
                    if (depth == 0)
                    {
                        return i + 1;
                    }

                    break;
            }
        }

        return 0;
    }

    // A name is a head when nothing before it could be its own operand.
    private static bool IsHeadPosition(IReadOnlyList<Token> tokens, int index, int statementStart)
    {
        if (index <= statementStart)
        {
            return true;
        }

        // `xs.map` is a member name, not a head in its own right.
        if (tokens[index - 1].Kind == TokenKind.Dot)
        {
            return false;
        }

        return tokens[index - 1].Kind is not (TokenKind.Identifier or TokenKind.IntegerLiteral or TokenKind.FloatLiteral
            or TokenKind.StringLiteral or TokenKind.InterpolatedStringLiteral or TokenKind.CharLiteral
            or TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.True or TokenKind.False or TokenKind.Null);
    }

    private static string BuildName(IReadOnlyList<Token> tokens, int index)
    {
        string name = tokens[index].Text;
        while (index + 2 < tokens.Count && tokens[index + 1].Kind == TokenKind.Dot && tokens[index + 2].Kind == TokenKind.Identifier)
        {
            name += "." + tokens[index + 2].Text;
            index += 2;
        }

        return name;
    }

    private static string LastSegment(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[(dot + 1)..];
    }

    // Spec §11.2: each whitespace-separated atom is one argument, and every element of a parenthesised group
    // is an argument of its own, so `min (a, b) c` passes three.
    private static int CountSpaceCallArguments(IReadOnlyList<Token> tokens, int start, int offset)
    {
        int arguments = 0;
        bool previousEndsAtom = false;
        for (int i = start; i < tokens.Count; i++)
        {
            Token token = tokens[i];
            if (token.Position >= offset || token.Kind is TokenKind.EndOfFile or TokenKind.NewLine or TokenKind.Semicolon)
            {
                break;
            }

            if (token.Kind == TokenKind.Dot)
            {
                // A member access continues the current atom.
                i++;
                continue;
            }

            if (token.Kind is TokenKind.OpenParen or TokenKind.OpenBracket)
            {
                int close = FindMatching(tokens, i, token.Kind, token.Kind == TokenKind.OpenParen ? TokenKind.CloseParen : TokenKind.CloseBracket);
                bool indexer = previousEndsAtom && token.Kind == TokenKind.OpenBracket;
                if (!indexer)
                {
                    arguments++;
                    if (token.Kind == TokenKind.OpenParen)
                    {
                        arguments += CountTopLevelCommas(tokens, i + 1, close < 0 ? tokens.Count : close);
                    }
                }

                if (close < 0 || tokens[close].Position >= offset)
                {
                    // The group is still being typed: the cursor is inside its last element.
                    return arguments;
                }

                i = close;
                previousEndsAtom = true;
                continue;
            }

            if (!CanStartAtom(token.Kind))
            {
                // An operator means the run is an expression, not an argument list.
                previousEndsAtom = false;
                continue;
            }

            if (!previousEndsAtom || token.Kind is TokenKind.Identifier or TokenKind.IntegerLiteral or TokenKind.FloatLiteral
                or TokenKind.StringLiteral or TokenKind.InterpolatedStringLiteral or TokenKind.CharLiteral
                or TokenKind.True or TokenKind.False or TokenKind.Null)
            {
                arguments++;
            }

            previousEndsAtom = true;
        }

        // The cursor sits after a space, ready for the next argument.
        return arguments + 1;
    }

    private static bool CanStartAtom(TokenKind kind)
        => kind is TokenKind.Identifier or TokenKind.IntegerLiteral or TokenKind.FloatLiteral or TokenKind.StringLiteral
            or TokenKind.InterpolatedStringLiteral or TokenKind.CharLiteral or TokenKind.True or TokenKind.False
            or TokenKind.Null or TokenKind.OpenParen or TokenKind.OpenBracket;

    private static int FindMatching(IReadOnlyList<Token> tokens, int open, TokenKind openKind, TokenKind closeKind)
    {
        int depth = 0;
        for (int i = open; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == openKind)
            {
                depth++;
            }
            else if (tokens[i].Kind == closeKind && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static int CountTopLevelCommas(IReadOnlyList<Token> tokens, int start, int endExclusive)
    {
        int depth = 0;
        int commas = 0;
        for (int i = start; i < endExclusive && i < tokens.Count; i++)
        {
            switch (tokens[i].Kind)
            {
                case TokenKind.OpenParen:
                case TokenKind.OpenBracket:
                case TokenKind.OpenBrace:
                    depth++;
                    break;
                case TokenKind.CloseParen:
                case TokenKind.CloseBracket:
                case TokenKind.CloseBrace:
                    depth--;
                    break;
                case TokenKind.Comma when depth == 0:
                    commas++;
                    break;
            }
        }

        return commas;
    }
}
