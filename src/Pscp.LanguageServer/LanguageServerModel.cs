using System.Collections.Concurrent;
using Pscp.Transpiler;

namespace Pscp.LanguageServer;

internal sealed record DocumentSnapshot(Uri Uri, int Version, string Text, LineIndex LineIndex);

internal sealed class LineIndex
{
    private readonly int[] _lineStarts;

    public LineIndex(string text)
    {
        List<int> starts = [0];
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
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
    string? RelatedSymbolId = null);

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
    IReadOnlyList<PscpDocumentSymbol> Children);

internal sealed record PscpCompletionEntry(
    string Label,
    int Kind,
    string? Detail,
    string? Documentation,
    string? InsertText = null,
    int? InsertTextFormat = null,
    string? SortText = null);

internal sealed record PscpSignatureEntry(
    string Label,
    IReadOnlyList<string> Parameters,
    string? Documentation);

internal sealed record PscpHoverEntry(string Markdown);

internal sealed record PscpInlayHintEntry(TextSpan Span, string Label);

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
    public required IReadOnlyDictionary<string, PscpSignatureEntry> Signatures { get; init; }
    public required IReadOnlyList<PscpCodeActionEntry> CodeActions { get; init; }
    public required IReadOnlyList<PscpInlayHintEntry> InlayHints { get; init; }

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
