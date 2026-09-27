using System.Collections.Concurrent;
using Pscp.Transpiler;

namespace Pscp.LanguageServer;

internal sealed record DocumentSnapshot(Uri Uri, int Version, string Text, LineIndex LineIndex);

internal sealed class LineIndex
{
    private readonly int[] _lineStarts;

    public LineIndex(string text)
    {
        // LSP line breaks are `\n`, `\r\n` and a lone `\r` (guide §5.2).
        List<int> starts = [0];
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n' || (text[i] == '\r' && (i + 1 == text.Length || text[i + 1] != '\n')))
            {
                starts.Add(i + 1);
            }
        }

        _lineStarts = starts.ToArray();
        TextLength = text.Length;
    }

    public int TextLength { get; }

    public int GetOffset(int line, int character)
    {
        if (_lineStarts.Length == 0)
        {
            return 0;
        }

        line = Math.Clamp(line, 0, _lineStarts.Length - 1);
        int start = _lineStarts[line];
        int lineEnd = line + 1 < _lineStarts.Length ? _lineStarts[line + 1] : TextLength;
        return Math.Clamp(start + character, start, lineEnd);
    }

    public (int line, int character) GetPosition(int offset)
    {
        offset = Math.Clamp(offset, 0, TextLength);
        int line = Array.BinarySearch(_lineStarts, offset);
        if (line < 0)
        {
            line = ~line - 1;
        }

        line = Math.Max(line, 0);
        return (line, Math.Max(0, offset - _lineStarts[line]));
    }
}

internal sealed class DocumentStore
{
    private readonly ConcurrentDictionary<Uri, DocumentSnapshot> _documents = new();

    public IReadOnlyCollection<DocumentSnapshot> OpenDocuments => _documents.Values.ToArray();

    public DocumentSnapshot Open(Uri uri, string text, int version)
    {
        DocumentSnapshot snapshot = new(uri, version, text, new LineIndex(text));
        _documents[uri] = snapshot;
        return snapshot;
    }

    public DocumentSnapshot Change(Uri uri, int version, string text)
    {
        DocumentSnapshot snapshot = new(uri, version, text, new LineIndex(text));
        _documents[uri] = snapshot;
        return snapshot;
    }

    public bool Close(Uri uri, out DocumentSnapshot? snapshot)
        => _documents.TryRemove(uri, out snapshot);

    public bool TryGet(Uri uri, out DocumentSnapshot? snapshot)
        => _documents.TryGetValue(uri, out snapshot);
}

internal enum ServerDiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4,
}

internal enum PscpServerSymbolKind
{
    Function,
    Local,
    Parameter,
    LoopVariable,
    Intrinsic,
    Type,
    Method,
    Property,
}

internal sealed record ScopeSpan(int Start, int End);

internal sealed record PscpServerDiagnostic(
    string Code,
    string Message,
    TextSpan Span,
    ServerDiagnosticSeverity Severity,
    string? RelatedSymbolId = null,
    TextSpan? RelatedSpan = null);

internal sealed record PscpServerSymbol(
    string Id,
    string Name,
    PscpServerSymbolKind Kind,
    TextSpan DeclarationSpan,
    TextSpan SelectionSpan,
    ScopeSpan Scope,
    string? TypeDisplay,
    bool IsMutable,
    bool IsIntrinsic,
    string? ContainerSymbolId,
    string? Documentation);

internal sealed record PscpServerReference(
    string SymbolId,
    TextSpan Span,
    bool IsDeclaration,
    bool IsWrite);

internal sealed record SemanticTokenClassification(
    TextSpan Span,
    string TokenType,
    IReadOnlyList<string> Modifiers);

internal sealed record PscpDocumentSymbol(
    string Name,
    int Kind,
    TextSpan Range,
    TextSpan SelectionRange,
    IReadOnlyList<PscpDocumentSymbol> Children,
    // Guide §10.4: the type or the signature, and "input" for a declaration fed by the input shorthand.
    string? Detail = null);

// A document symbol under construction: children are added while the declaration's body is analysed.
internal sealed class PscpDocumentSymbolBuilder(string name, int kind, TextSpan range, TextSpan selectionRange, string? detail)
{
    public List<PscpDocumentSymbolBuilder> Children { get; } = [];

    public TextSpan Range { get; set; } = range;

    public PscpDocumentSymbol Build()
        => new(name, kind, Range, selectionRange, Children.OrderBy(child => child.Range.Start).Select(child => child.Build()).ToArray(), detail);
}

internal sealed record PscpCompletionEntry(
    string Label,
    int Kind,
    string? Detail,
    string? Documentation,
    string? InsertText = null,
    int? InsertTextFormat = null,
    string? SortText = null,
    // `labelDetails` (guide §8.4): the parameter shape and the result type.
    string? LabelDetail = null,
    string? LabelDescription = null)
{
    // A copy in completion group `group` (guide §8.3). Entries carry their own name-ordered suffix.
    public PscpCompletionEntry InGroup(string group)
        => this with { SortText = group + (SortText is { Length: > 1 } text && char.IsDigit(text[0]) ? text[1..] : SortText ?? Label) };
}

// One parameter of one overload, with its offsets into that overload's label. Guide §9.4 requires the offset
// form so the editor can highlight the parameter being typed.
internal sealed record PscpSignatureParameter(string Label, int Start, int End);

internal sealed record PscpSignatureForm(string Label, IReadOnlyList<PscpSignatureParameter> Parameters, bool IsVariadic);

internal sealed record PscpSignatureEntry(IReadOnlyList<PscpSignatureForm> Forms, string? Documentation)
{
    public string Label => Forms.Count > 0 ? Forms[0].Label : string.Empty;

    public IReadOnlyList<string> Parameters
        => Forms.Count > 0 ? Forms[0].Parameters.Select(parameter => parameter.Label).ToArray() : [];

    // `min(a, b, ...) / min(xs)` becomes two overloads; each one's parameters carry their label offsets.
    public static PscpSignatureEntry FromSignature(string signature, string? documentation)
        => new(signature.Split(" / ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseForm)
            .ToArray(),
            documentation);

    private static PscpSignatureForm ParseForm(string label)
    {
        int open = label.IndexOf('(');
        int close = label.LastIndexOf(')');
        if (open < 0 || close <= open)
        {
            return new PscpSignatureForm(label, [], false);
        }

        List<PscpSignatureParameter> parameters = [];
        bool variadic = false;
        int depth = 0;
        int start = open + 1;
        for (int i = open + 1; i <= close; i++)
        {
            char character = label[i];
            if (character is '(' or '<' or '[')
            {
                depth++;
                continue;
            }

            if (character is ')' or '>' or ']')
            {
                if (i < close)
                {
                    depth--;
                    continue;
                }
            }
            else if (character != ',' || depth > 0)
            {
                continue;
            }

            AddParameter(label, parameters, ref variadic, start, i);
            start = i + 1;
        }

        return new PscpSignatureForm(label, parameters, variadic);
    }

    private static void AddParameter(string label, List<PscpSignatureParameter> parameters, ref bool variadic, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(label[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(label[end - 1]))
        {
            end--;
        }

        if (end <= start)
        {
            return;
        }

        string text = label[start..end];
        if (text == "...")
        {
            variadic = true;
        }

        parameters.Add(new PscpSignatureParameter(text, start, end));
    }
}

internal sealed record PscpHoverEntry(string Markdown);

// Guide §12.1 and appendix C: each kind has its own setting.
internal enum PscpInlayHintKind
{
    InferredType,
    RewriteResult,
    AccumulatorType,
    ParameterName,
}

internal sealed record PscpInlayHintEntry(
    TextSpan Span,
    string Label,
    PscpInlayHintKind Kind = PscpInlayHintKind.InferredType,
    string? Tooltip = null);

internal sealed record PscpCodeActionEntry(
    string Title,
    string Kind,
    TextSpan Span,
    string ReplacementText);

internal sealed class PscpAnalysisResult
{
    public required DocumentSnapshot Snapshot { get; init; }
    public required IReadOnlyList<Token> Tokens { get; init; }
    public required IReadOnlyList<PscpServerDiagnostic> Diagnostics { get; init; }
    public required IReadOnlyList<PscpServerSymbol> Symbols { get; init; }
    public required IReadOnlyList<PscpServerReference> References { get; init; }
    public required IReadOnlyList<SemanticTokenClassification> SemanticTokens { get; init; }
    public required IReadOnlyList<PscpDocumentSymbol> DocumentSymbols { get; init; }
    public required IReadOnlyDictionary<int, string> TokenToSymbolId { get; init; }
    public required IReadOnlyDictionary<int, PscpHoverEntry> HoverByTokenIndex { get; init; }
    public required IReadOnlyDictionary<int, PscpCompletionEntry> IntrinsicMembers { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, PscpServerSymbol>> TypeMembers { get; init; }
    public required IReadOnlyDictionary<string, PscpSignatureEntry> Signatures { get; init; }
    public required IReadOnlyList<PscpCodeActionEntry> CodeActions { get; init; }
    public required IReadOnlyList<PscpInlayHintEntry> InlayHints { get; init; }

    // The shared front-end result of this version (tokens, tree, diagnostics), used by the generated C# preview.
    public PscpFrontEndResult? FrontEnd { get; set; }

    public Token? FindTokenAtOffset(int offset)
    {
        Token? found = null;
        foreach (Token token in Tokens)
        {
            if (token.Kind == TokenKind.EndOfFile)
            {
                break;
            }

            if (offset >= token.Position && offset < token.Position + Math.Max(token.Text.Length, 1))
            {
                found = token;
            }

            if (token.Position > offset)
            {
                break;
            }
        }

        return found;
    }
}
