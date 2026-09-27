using Pscp.Transpiler;

namespace Pscp.LanguageServer;

internal sealed record PscpFoldingRange(int StartLine, int StartCharacter, int EndLine, int EndCharacter, string? Kind);

// Folding and selection ranges (guide §10.5). Both are computed from the token stream: the analyzer's symbol
// table says nothing about the shape of a block, and the parse tree does not survive a syntax error.
internal static class PscpNavigation
{
    public static IReadOnlyList<PscpFoldingRange> ComputeFoldingRanges(PscpAnalysisResult analysis)
    {
        List<PscpFoldingRange> ranges = [];
        LineIndex lines = analysis.Snapshot.LineIndex;
        IReadOnlyList<Token> tokens = analysis.Tokens;
        Stack<int> openers = new();

        for (int i = 0; i < tokens.Count; i++)
        {
            switch (tokens[i].Kind)
            {
                case TokenKind.OpenBrace:
                case TokenKind.OpenParen:
                case TokenKind.OpenBracket:
                    openers.Push(i);
                    break;
                case TokenKind.CloseBrace:
                case TokenKind.CloseParen:
                case TokenKind.CloseBracket:
                    if (openers.Count > 0)
                    {
                        Add(ranges, lines, tokens[openers.Pop()].Position, tokens[i].Position, null);
                    }

                    break;
            }
        }

        AddLineRuns(ranges, analysis, lines);
        return ranges;
    }

    // A run of `using` lines, a run of comment lines, and a `// #region` … `// #endregion` pair.
    private static void AddLineRuns(List<PscpFoldingRange> ranges, PscpAnalysisResult analysis, LineIndex lines)
    {
        string[] text = analysis.Snapshot.Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        Stack<int> regions = new();
        int usingStart = -1;
        int commentStart = -1;

        for (int line = 0; line <= text.Length; line++)
        {
            string trimmed = line < text.Length ? text[line].Trim() : string.Empty;
            bool isUsing = line < text.Length && trimmed.StartsWith("using ", StringComparison.Ordinal);
            bool isComment = line < text.Length && trimmed.StartsWith("//", StringComparison.Ordinal);

            if (isComment && IsRegionMarker(trimmed, "#region"))
            {
                regions.Push(line);
            }
            else if (isComment && IsRegionMarker(trimmed, "#endregion") && regions.Count > 0)
            {
                int start = regions.Pop();
                ranges.Add(new PscpFoldingRange(start, text[start].Length, line, text[line].Length, "region"));
            }

            usingStart = Run(ranges, text, isUsing, usingStart, line, "imports");
            commentStart = Run(ranges, text, isComment, commentStart, line, "comment");
        }
    }

    private static int Run(List<PscpFoldingRange> ranges, string[] text, bool inRun, int start, int line, string kind)
    {
        if (inRun)
        {
            return start < 0 ? line : start;
        }

        if (start >= 0 && line - start >= 2)
        {
            ranges.Add(new PscpFoldingRange(start, text[start].Length, line - 1, text[line - 1].Length, kind));
        }

        return -1;
    }

    private static bool IsRegionMarker(string trimmed, string marker)
        => trimmed.TrimStart('/').TrimStart().StartsWith(marker, StringComparison.Ordinal);

    private static void Add(List<PscpFoldingRange> ranges, LineIndex lines, int openOffset, int closeOffset, string? kind)
    {
        (int startLine, int startCharacter) = lines.GetPosition(openOffset);
        (int endLine, int endCharacter) = lines.GetPosition(closeOffset);
        // A construct that fits on one line has nothing to fold.
        if (endLine > startLine)
        {
            ranges.Add(new PscpFoldingRange(startLine, startCharacter + 1, endLine, endCharacter, kind));
        }
    }

    // Guide §10.5: token → expression → statement → block → function → file. The nesting comes from the
    // bracket structure, plus the statement the offset sits in.
    public static IReadOnlyList<TextSpan> ComputeSelectionRange(PscpAnalysisResult analysis, int offset)
    {
        IReadOnlyList<Token> tokens = analysis.Tokens;
        List<TextSpan> spans = [];

        int tokenIndex = -1;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind is TokenKind.EndOfFile or TokenKind.NewLine)
            {
                continue;
            }

            if (tokens[i].Position <= offset && offset <= tokens[i].Position + Math.Max(tokens[i].Text.Length, 1))
            {
                tokenIndex = i;
                break;
            }
        }

        if (tokenIndex >= 0)
        {
            spans.Add(tokens[tokenIndex].Span);
        }

        // Each enclosing bracket pair, innermost first.
        List<TextSpan> brackets = [];
        Stack<int> openers = new();
        for (int i = 0; i < tokens.Count; i++)
        {
            switch (tokens[i].Kind)
            {
                case TokenKind.OpenBrace:
                case TokenKind.OpenParen:
                case TokenKind.OpenBracket:
                    openers.Push(i);
                    break;
                case TokenKind.CloseBrace:
                case TokenKind.CloseParen:
                case TokenKind.CloseBracket:
                    if (openers.Count > 0)
                    {
                        int open = openers.Pop();
                        int start = tokens[open].Position;
                        int end = tokens[i].Position + 1;
                        if (start <= offset && offset <= end)
                        {
                            brackets.Add(new TextSpan(start, end - start));
                        }
                    }

                    break;
            }
        }

        brackets.Sort((left, right) => left.Length.CompareTo(right.Length));

        // The statement around the offset sits between the innermost bracket pair and that pair itself.
        if (StatementSpan(tokens, offset, brackets.Count > 0 ? brackets[0] : null) is { } statement)
        {
            spans.Add(statement);
        }

        spans.AddRange(brackets);
        spans.Add(new TextSpan(0, analysis.Snapshot.Text.Length));

        // Keep only a strictly growing chain, which is what the protocol's parent links mean.
        List<TextSpan> chain = [];
        foreach (TextSpan span in spans)
        {
            if (chain.Count == 0)
            {
                chain.Add(span);
                continue;
            }

            TextSpan last = chain[^1];
            if (span.Start <= last.Start && span.End >= last.End && span.Length > last.Length)
            {
                chain.Add(span);
            }
        }

        return chain;
    }

    private static TextSpan? StatementSpan(IReadOnlyList<Token> tokens, int offset, TextSpan? within)
    {
        int start = within?.Start + 1 ?? 0;
        int end = within?.End - 1 ?? int.MaxValue;
        int statementStart = start;
        int depth = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            Token token = tokens[i];
            if (token.Position < start)
            {
                continue;
            }

            if (token.Position >= end || token.Kind == TokenKind.EndOfFile)
            {
                break;
            }

            switch (token.Kind)
            {
                case TokenKind.OpenBrace:
                case TokenKind.OpenParen:
                case TokenKind.OpenBracket:
                    depth++;
                    continue;
                case TokenKind.CloseBrace:
                case TokenKind.CloseParen:
                case TokenKind.CloseBracket:
                    depth--;
                    continue;
            }

            if (depth > 0 || token.Kind is not (TokenKind.NewLine or TokenKind.Semicolon))
            {
                continue;
            }

            if (token.Position >= offset)
            {
                return statementStart < token.Position ? new TextSpan(statementStart, token.Position - statementStart) : null;
            }

            statementStart = token.Position + Math.Max(token.Text.Length, 1);
        }

        return null;
    }
}
