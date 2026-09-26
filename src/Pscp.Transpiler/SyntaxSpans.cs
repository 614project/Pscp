namespace Pscp.Transpiler;

// Source locations of syntax nodes. The AST records carry no positions (they are compared structurally in a few
// places), so the parser keeps them here keyed by node identity. `GetName` returns the span of the declared name
// of a declaration node (function, type, method, binding) and falls back to the whole node.
public sealed class SyntaxSpans
{
    private readonly Dictionary<object, TextSpan> _spans = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, TextSpan> _nameSpans = new(ReferenceEqualityComparer.Instance);

    public static SyntaxSpans Empty { get; } = new();

    public TextSpan Get(object? node)
        => node is not null && _spans.TryGetValue(node, out TextSpan span) ? span : default;

    public bool TryGet(object? node, out TextSpan span)
    {
        if (node is not null && _spans.TryGetValue(node, out span))
        {
            return true;
        }

        span = default;
        return false;
    }

    public TextSpan GetName(object? node)
        => node is not null && _nameSpans.TryGetValue(node, out TextSpan span) ? span : Get(node);

    internal void Set(object node, TextSpan span)
    {
        if (span.Length >= 0)
        {
            _spans[node] = span;
        }
    }

    internal void SetName(object node, TextSpan span) => _nameSpans[node] = span;

    // Spans of a nested parse (an interpolation hole) moved to their position in the enclosing source.
    internal void CopyShiftedTo(SyntaxSpans target, int offset)
    {
        foreach ((object node, TextSpan span) in _spans)
        {
            target._spans[node] = new TextSpan(span.Start + offset, span.Length);
        }

        foreach ((object node, TextSpan span) in _nameSpans)
        {
            target._nameSpans[node] = new TextSpan(span.Start + offset, span.Length);
        }
    }

    // Rewritten nodes keep the location of the syntax they replace.
    internal void Copy(object from, object to)
    {
        if (ReferenceEquals(from, to))
        {
            return;
        }

        if (_spans.TryGetValue(from, out TextSpan span) && !_spans.ContainsKey(to))
        {
            _spans[to] = span;
        }

        if (_nameSpans.TryGetValue(from, out TextSpan nameSpan) && !_nameSpans.ContainsKey(to))
        {
            _nameSpans[to] = nameSpan;
        }
    }
}
