using Pscp.Transpiler;

namespace Pscp.LanguageServer;

// Hover for the PSCP-only operators (guide §7.2). The sentences describe the lowering the transpiler
// performs, so they stay next to the spec sections they cite.
internal static class PscpOperatorHovers
{
    // Spec §26.2: a rewrite applies only when the operand's static type is exactly one of these.
    private static readonly IReadOnlyDictionary<string, (string? Add, string? Remove, string? Peek, string? Take)> RewriteTable =
        new Dictionary<string, (string?, string?, string?, string?)>(StringComparer.Ordinal)
        {
            ["List"] = ("Add(v)` : `void", null, null, null),
            ["LinkedList"] = ("AddLast(v)` : `void", null, null, null),
            ["HashSet"] = ("Add(v)` : `bool", "Remove(v)` : `bool", null, null),
            ["SortedSet"] = ("Add(v)` : `bool", "Remove(v)` : `bool", null, null),
            ["Dictionary"] = ("TryAdd(k, v)` : `bool", "Remove(k)` : `bool", null, null),
            ["Stack"] = ("Push(v)` : `void", null, "Peek()", "Pop()"),
            ["Queue"] = ("Enqueue(v)` : `void", null, "Peek()", "Dequeue()"),
            ["PriorityQueue"] = ("Enqueue(e, p)` : `void", null, "Peek()", "Dequeue()"),
        };

    public static string? ForOperator(TokenKind kind)
        => kind switch
        {
            TokenKind.ColonEqual => Hover(
                "x := e",
                "Assignment that yields a value: it assigns and then produces the assigned value, so it can be used inside an expression.",
                "(x = e)",
                "13.4 `=`와 `:=`",
                "134-와-"),
            TokenKind.PipeGreater => Hover(
                "value |> f",
                "Pipe: the left value becomes the first argument of the right-hand call. A collection helper becomes a member call on the value.",
                "f(value)",
                "13.3 pipe",
                "133-pipe"),
            TokenKind.LessPipe => Hover(
                "f <| value",
                "Reverse pipe: the right value becomes the last argument of the left-hand call.",
                "f(value)",
                "13.3 pipe",
                "133-pipe"),
            TokenKind.Spaceship => Hover(
                "a <=> b",
                "Comparison in the PSCP default order: negative, zero or positive. Strings compare ordinally and tuples element-wise.",
                "Comparer<T>.Default.Compare(a, b)",
                "25.1 기본 순서",
                "251-기본-순서"),
            TokenKind.DotDot => Hover(
                "a..b",
                "Range with the end excluded outside an indexer. Inside an indexer `..` is ambiguous and is reported as PSCP2303: write `..<` or `..=`.",
                "for (int i = a; i < b; i++)",
                "14 range",
                "14-range"),
            TokenKind.DotDotLess => Hover(
                "a..<b",
                "Range with the end excluded. The bounds are evaluated once, in the order start, step, end.",
                "for (int i = a; i < b; i++)",
                "14 range",
                "14-range"),
            TokenKind.DotDotEqual => Hover(
                "a..=b",
                "Range with the end included. In an indexer it lowers to the C# end-exclusive form.",
                "for (int i = a; i <= b; i++)  //  a..(b + 1) in an indexer",
                "14 range",
                "14-range"),
            TokenKind.Arrow => Hover(
                "xs -> x { ... }",
                "Fast iteration: it walks the collection with the concrete enumerator and does not allocate an intermediate sequence.",
                "foreach (var x in xs) { ... }",
                "16.4 fast iteration",
                "164-fast-iteration"),
            _ => null,
        };

    // Spec §26.2. `typeDisplay` is the receiver's static type as the analyzer knows it.
    public static string? ForRewrite(TokenKind kind, string? typeDisplay)
    {
        if (typeDisplay is null)
        {
            return null;
        }

        string name = typeDisplay.Split('<', 2)[0].Split('.')[^1];
        if (!RewriteTable.TryGetValue(name, out (string? Add, string? Remove, string? Peek, string? Take) entry))
        {
            return null;
        }

        (string? member, string shape, string meaning) = kind switch
        {
            TokenKind.PlusEqual => (entry.Add, "x += v", "Data-structure rewrite: it adds the value through the underlying .NET method."),
            TokenKind.MinusEqual => (entry.Remove, "x -= v", "Data-structure rewrite: it removes the value through the underlying .NET method."),
            TokenKind.Tilde => (entry.Peek, "~x", "Data-structure rewrite: it reads the next element without removing it."),
            TokenKind.MinusMinus => (entry.Take, "--x", "Data-structure rewrite: it removes the next element and yields it."),
            _ => (null, string.Empty, string.Empty),
        };

        return member is null
            ? null
            : Hover($"{shape}      // {typeDisplay}", meaning, $"x.{member}", "26 자료구조 연산자 rewrite", "26-자료구조-연산자-rewrite");
    }

    private static string Hover(string shape, string meaning, string lowering, string section, string anchor)
        => $"```pscp\n{shape}\n```\n\n{meaning}\n\n→ C#: `{lowering}`\n\n[Spec §{section}]({DiagnosticCodes.SpecUrl}#{Uri.EscapeDataString(anchor)})";
}
