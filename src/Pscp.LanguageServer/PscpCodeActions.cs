using System.Text.RegularExpressions;
using Pscp.Transpiler;

namespace Pscp.LanguageServer;

internal sealed record PscpTextEdit(TextSpan Span, string NewText);

internal sealed record PscpQuickFix(string Title, PscpServerDiagnostic Diagnostic, IReadOnlyList<PscpTextEdit> Edits, bool IsPreferred);

// Quick fixes bound to diagnostic codes (guide §12.2, appendix B). Every edit is computed from the front-end tokens
// of the analyzed version, so it applies to exactly that version.
internal static class PscpCodeActions
{
    // Deprecated `stdin` members and helpers renamed without changing their arguments (spec §17.8, appendix C).
    private static readonly IReadOnlyDictionary<string, string> Renames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["int"] = "readInt", ["long"] = "readLong", ["double"] = "readDouble", ["decimal"] = "readDecimal",
        ["bool"] = "readBool", ["char"] = "readChar", ["str"] = "readString", ["line"] = "readLine",
        ["lines"] = "readLines", ["words"] = "readWords", ["chars"] = "readChars",
        ["array"] = "readArray", ["list"] = "readList", ["linkedList"] = "readLinkedList",
        ["readTuple2"] = "readTuple", ["readTuple3"] = "readTuple", ["tuple2"] = "readTuple", ["tuple3"] = "readTuple",
        ["readGridInt"] = "readGrid<int>", ["gridInt"] = "readGrid<int>", ["readGridLong"] = "readGrid<long>", ["gridLong"] = "readGrid<long>",
        ["readNestedArray"] = "readGrid", ["nestedArray"] = "readGrid",
        ["charGrid"] = "readCharGrid", ["wordGrid"] = "readWordGrid",
        ["groupCount"] = "freq",
    };

    public static string? GetReplacement(string deprecatedName)
        => Renames.TryGetValue(deprecatedName, out string? replacement) ? replacement : null;

    public static IReadOnlyList<PscpQuickFix> Compute(PscpAnalysisResult analysis, TextSpan range)
    {
        List<PscpQuickFix> fixes = [];
        foreach (PscpServerDiagnostic diagnostic in analysis.Diagnostics)
        {
            if (!Overlaps(diagnostic.Span, range))
            {
                continue;
            }

            switch (diagnostic.Code)
            {
                case DiagnosticCodes.ImmutableMutation:
                case DiagnosticCodes.UninitializedImmutable:
                    AddMutableFix(analysis, diagnostic, fixes);
                    break;
                case DiagnosticCodes.MissingRec:
                    AddRecFix(analysis, diagnostic, fixes);
                    break;
                case DiagnosticCodes.UnnecessaryRec:
                    AddRemoveRecFix(analysis, diagnostic, fixes);
                    break;
                case DiagnosticCodes.MissingReturnValue when diagnostic.Message.Contains("`:=`", StringComparison.Ordinal):
                case DiagnosticCodes.AssignmentInExpression:
                    AddValueAssignmentFix(analysis, diagnostic, fixes);
                    break;
                case DiagnosticCodes.AmbiguousSliceBound:
                    AddSliceFixes(analysis, diagnostic, fixes);
                    break;
                case DiagnosticCodes.Deprecated:
                    AddRenameFix(analysis, diagnostic, fixes);
                    break;
                case DiagnosticCodes.CultureOrderedStrings:
                    AddOrdinalComparerFix(analysis, diagnostic, fixes);
                    break;
                case DiagnosticCodes.UnsupportedStatement when diagnostic.Message.Contains("foreach", StringComparison.Ordinal):
                    AddForeachFix(analysis, diagnostic, fixes);
                    break;
            }
        }

        return fixes;
    }

    private static bool Overlaps(TextSpan span, TextSpan range)
        => span.Start <= range.End && range.Start <= span.Start + Math.Max(span.Length, 1);

    // `int x = 3` → `mut int x = 3`, `let x = 3` → `var x = 3` (spec §9.1).
    private static void AddMutableFix(PscpAnalysisResult analysis, PscpServerDiagnostic diagnostic, List<PscpQuickFix> fixes)
    {
        TextSpan declaration = diagnostic.RelatedSpan ?? diagnostic.Span;
        int nameIndex = TokenIndexAt(analysis.Tokens, declaration.Start);
        int start = nameIndex < 0 ? -1 : StatementStart(analysis.Tokens, nameIndex);
        if (start < 0)
        {
            return;
        }

        Token first = analysis.Tokens[start];
        switch (first.Kind)
        {
            case TokenKind.Let:
                fixes.Add(new PscpQuickFix("Declare it with `var`", diagnostic, [new PscpTextEdit(first.Span, "var")], IsPreferred: true));
                break;
            case TokenKind.Identifier or TokenKind.OpenParen when !IsKeywordLikeStatement(first):
                fixes.Add(new PscpQuickFix("Declare it with `mut`", diagnostic, [new PscpTextEdit(new TextSpan(first.Position, 0), "mut ")], IsPreferred: true));
                break;
        }
    }

    private static bool IsKeywordLikeStatement(Token token)
        => token.Text is "foreach" or "switch" or "goto" or "do" or "return" or "throw" or "try";

    // Every function of the recursive cycle gets `rec` (spec §10.3).
    private static void AddRecFix(PscpAnalysisResult analysis, PscpServerDiagnostic diagnostic, List<PscpQuickFix> fixes)
    {
        HashSet<string> cycle = [.. Regex.Matches(diagnostic.Message, "`([A-Za-z_][A-Za-z0-9_]*)`").Select(match => match.Groups[1].Value)];
        List<PscpTextEdit> edits = [];
        foreach (PscpServerDiagnostic other in analysis.Diagnostics)
        {
            if (other.Code != DiagnosticCodes.MissingRec)
            {
                continue;
            }

            int nameIndex = TokenIndexAt(analysis.Tokens, other.Span.Start);
            if (nameIndex < 0 || !cycle.Contains(analysis.Tokens[nameIndex].Text))
            {
                continue;
            }

            int start = StatementStart(analysis.Tokens, nameIndex);
            if (start >= 0 && analysis.Tokens[start].Kind != TokenKind.Rec)
            {
                edits.Add(new PscpTextEdit(new TextSpan(analysis.Tokens[start].Position, 0), "rec "));
            }
        }

        if (edits.Count > 0)
        {
            fixes.Add(new PscpQuickFix(edits.Count > 1 ? "Add `rec` to the recursive functions" : "Add `rec`", diagnostic, edits, IsPreferred: true));
        }
    }

    private static void AddRemoveRecFix(PscpAnalysisResult analysis, PscpServerDiagnostic diagnostic, List<PscpQuickFix> fixes)
    {
        int index = TokenIndexAt(analysis.Tokens, diagnostic.Span.Start);
        if (index < 0)
        {
            return;
        }

        int recIndex = analysis.Tokens[index].Kind == TokenKind.Rec ? index : StatementStart(analysis.Tokens, index);
        if (recIndex < 0 || analysis.Tokens[recIndex].Kind != TokenKind.Rec)
        {
            return;
        }

        Token rec = analysis.Tokens[recIndex];
        int end = rec.Position + rec.Text.Length;
        string text = analysis.Snapshot.Text;
        while (end < text.Length && text[end] is ' ' or '\t')
        {
            end++;
        }

        fixes.Add(new PscpQuickFix("Remove `rec`", diagnostic, [new PscpTextEdit(new TextSpan(rec.Position, end - rec.Position), string.Empty)], IsPreferred: true));
    }

    // `x = e` whose value is used → `x := e` (spec §13.4).
    private static void AddValueAssignmentFix(PscpAnalysisResult analysis, PscpServerDiagnostic diagnostic, List<PscpQuickFix> fixes)
    {
        foreach (Token token in TokensIn(analysis.Tokens, diagnostic.Span))
        {
            if (token.Kind == TokenKind.Equal)
            {
                fixes.Add(new PscpQuickFix("Use `:=`", diagnostic, [new PscpTextEdit(token.Span, ":=")], IsPreferred: true));
                return;
            }
        }
    }

    // `a[1..^1]` → `..<` or `..=` (spec §15.2). Neither is preferred: the intent decides.
    private static void AddSliceFixes(PscpAnalysisResult analysis, PscpServerDiagnostic diagnostic, List<PscpQuickFix> fixes)
    {
        foreach (Token token in TokensIn(analysis.Tokens, diagnostic.Span))
        {
            if (token.Kind == TokenKind.DotDot)
            {
                fixes.Add(new PscpQuickFix("Exclude the end (`..<`)", diagnostic, [new PscpTextEdit(token.Span, "..<")], IsPreferred: false));
                fixes.Add(new PscpQuickFix("Include the end (`..=`)", diagnostic, [new PscpTextEdit(token.Span, "..=")], IsPreferred: false));
                return;
            }
        }
    }

    private static void AddRenameFix(PscpAnalysisResult analysis, PscpServerDiagnostic diagnostic, List<PscpQuickFix> fixes)
    {
        int index = TokenIndexAt(analysis.Tokens, diagnostic.Span.Start);
        if (index < 0 || GetReplacement(analysis.Tokens[index].Text) is not string replacement)
        {
            return;
        }

        fixes.Add(new PscpQuickFix($"Replace with `{replacement}`", diagnostic, [new PscpTextEdit(analysis.Tokens[index].Span, replacement)], IsPreferred: true));
    }

    // `new SortedSet<string>()` → `new SortedSet<string>(string.asc)`, `Array.Sort(xs)` → `Array.Sort(xs, string.asc)`.
    private static void AddOrdinalComparerFix(PscpAnalysisResult analysis, PscpServerDiagnostic diagnostic, List<PscpQuickFix> fixes)
    {
        Token[] tokens = TokensIn(analysis.Tokens, diagnostic.Span).ToArray();
        int close = Array.FindLastIndex(tokens, token => token.Kind == TokenKind.CloseParen);
        if (close < 1)
        {
            return;
        }

        PscpTextEdit edit = tokens[close - 1].Kind == TokenKind.OpenParen
            ? new PscpTextEdit(new TextSpan(tokens[close].Position, 0), "string.asc")
            : new PscpTextEdit(new TextSpan(tokens[close].Position, 0), ", string.asc");
        fixes.Add(new PscpQuickFix("Pass `string.asc`", diagnostic, [edit], IsPreferred: true));
    }

    // `foreach (var x in xs)` → `for x in xs` (spec §6.3).
    private static void AddForeachFix(PscpAnalysisResult analysis, PscpServerDiagnostic diagnostic, List<PscpQuickFix> fixes)
    {
        IReadOnlyList<Token> tokens = analysis.Tokens;
        int start = TokenIndexAt(tokens, diagnostic.Span.Start);
        if (start < 0 || tokens[start].Text != "foreach" || start + 1 >= tokens.Count || tokens[start + 1].Kind != TokenKind.OpenParen)
        {
            return;
        }

        int depth = 0;
        int inIndex = -1;
        int close = -1;
        for (int i = start + 1; i < tokens.Count; i++)
        {
            if (tokens[i].Kind is TokenKind.OpenParen or TokenKind.OpenBracket)
            {
                depth++;
            }
            else if (tokens[i].Kind is TokenKind.CloseParen or TokenKind.CloseBracket)
            {
                depth--;
                if (depth == 0)
                {
                    close = i;
                    break;
                }
            }
            else if (tokens[i].Kind == TokenKind.In && depth == 1 && inIndex < 0)
            {
                inIndex = i;
            }
        }

        if (inIndex < 0 || close < 0 || tokens[inIndex - 1].Kind != TokenKind.Identifier)
        {
            return;
        }

        string text = analysis.Snapshot.Text;
        string source = text[(tokens[inIndex].Position + tokens[inIndex].Text.Length)..tokens[close].Position].Trim();
        string replacement = $"for {tokens[inIndex - 1].Text} in {source}";
        TextSpan span = new(tokens[start].Position, tokens[close].Position + 1 - tokens[start].Position);
        fixes.Add(new PscpQuickFix("Use `for ... in`", diagnostic, [new PscpTextEdit(span, replacement)], IsPreferred: true));
    }

    private static int TokenIndexAt(IReadOnlyList<Token> tokens, int offset)
    {
        for (int i = 0; i < tokens.Count; i++)
        {
            Token token = tokens[i];
            if (token.Kind is TokenKind.EndOfFile)
            {
                break;
            }

            if (token.Kind != TokenKind.NewLine && offset >= token.Position && offset < token.Position + Math.Max(token.Text.Length, 1))
            {
                return i;
            }

            if (token.Position > offset)
            {
                break;
            }
        }

        return -1;
    }

    private static IEnumerable<Token> TokensIn(IReadOnlyList<Token> tokens, TextSpan span)
        => tokens.Where(token => token.Kind is not (TokenKind.NewLine or TokenKind.EndOfFile)
            && token.Position >= span.Start
            && token.Position < span.Start + Math.Max(span.Length, 1));

    // The first token of the statement that contains `index`, or -1 inside parentheses (a parameter list or a
    // nested expression).
    private static int StatementStart(IReadOnlyList<Token> tokens, int index)
    {
        int depth = 0;
        int start = index;
        for (int i = index - 1; i >= 0; i--)
        {
            Token token = tokens[i];
            if (depth == 0 && token.Kind is TokenKind.NewLine or TokenKind.Semicolon or TokenKind.OpenBrace or TokenKind.CloseBrace)
            {
                break;
            }

            if (token.Kind is TokenKind.CloseParen or TokenKind.CloseBracket)
            {
                depth++;
            }
            else if (token.Kind is TokenKind.OpenParen or TokenKind.OpenBracket)
            {
                if (depth == 0)
                {
                    return -1;
                }

                depth--;
            }

            start = i;
        }

        return start;
    }
}
