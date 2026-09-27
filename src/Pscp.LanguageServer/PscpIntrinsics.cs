using Pscp.Transpiler;

namespace Pscp.LanguageServer;

// Completion, hover and signature tables built from the shared intrinsic catalog (PscpIntrinsicDocs, guide
// principle 3). Sort groups follow guide §8.3.
internal static class PscpIntrinsics
{
    public const string SortLocal = "0";
    public const string SortTypeMember = "1";
    public const string SortTopLevel = "2";
    public const string SortIntrinsic = "3";
    public const string SortDotNet = "4";
    public const string SortKeyword = "5";

    public static readonly IReadOnlyDictionary<string, PscpCompletionEntry> Globals = Table(
        PscpIntrinsicDocs.Of(IntrinsicDocKind.Object).Select(doc => new PscpCompletionEntry(doc.Name, 6, doc.Name, PscpIntrinsicDocs.FormatMarkdown(doc), null, null, SortIntrinsic + doc.Name))
            .Append(new PscpCompletionEntry("Array", 7, ".NET type", "The .NET array type (`Array.Sort`, `Array.Fill`, ...).", null, null, SortDotNet + "Array")));

    public static readonly IReadOnlyDictionary<string, PscpCompletionEntry> StdinMembers = Members(IntrinsicDocKind.StdinMember, 2);

    public static readonly IReadOnlyDictionary<string, PscpCompletionEntry> StdoutMembers = Members(IntrinsicDocKind.StdoutMember, 2);

    // `Array.zero(n)` is deprecated (spec appendix C): it is no longer offered.
    public static readonly IReadOnlyDictionary<string, PscpCompletionEntry> ArrayMembers = new Dictionary<string, PscpCompletionEntry>(StringComparer.Ordinal);

    public static readonly IReadOnlyDictionary<string, PscpCompletionEntry> ComparatorMembers = Table(
        PscpIntrinsicDocs.Of(IntrinsicDocKind.Comparator).Select(doc => new PscpCompletionEntry(doc.Name, 10, doc.Signature, PscpIntrinsicDocs.FormatMarkdown(doc), null, null, SortIntrinsic + doc.Name)));

    // Guide §8.1: member completion offers the collection helpers, the aggregate member aliases, and
    // `lowerBound`/`upperBound`.
    public static readonly IReadOnlyDictionary<string, PscpCompletionEntry> CollectionMembers = Table(
        PscpIntrinsicDocs.Of(IntrinsicDocKind.CollectionHelper)
            .Concat(PscpIntrinsicDocs.Of(IntrinsicDocKind.Aggregate).Where(doc => doc.Name is not ("chmin" or "chmax")))
            .Select(doc => doc.Kind == IntrinsicDocKind.Aggregate ? AggregateMember(doc) : Completion(doc, kind: 2)));

    // Free forms: aggregates and math. Collection helpers are member-only (guide §8.2).
    public static readonly IReadOnlyDictionary<string, PscpCompletionEntry> IntrinsicFunctions = Table(
        PscpIntrinsicDocs.Of(IntrinsicDocKind.Aggregate).Concat(PscpIntrinsicDocs.Of(IntrinsicDocKind.Math)).Select(doc => Completion(doc, kind: 3)));

    public static readonly IReadOnlyDictionary<string, PscpSignatureEntry> Signatures = CreateSignatures();

    public static readonly IReadOnlyDictionary<string, string> HoverDocs = CreateHoverDocs();

    public static readonly IReadOnlySet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "let", "var", "mut", "rec", "if", "then", "else", "for", "in", "do", "while",
        "break", "continue", "return", "true", "false", "null", "and", "or", "xor", "not",
        "where", "new", "class", "struct", "record", "namespace", "using",
        "ref", "out", "is", "public", "private", "protected", "internal", "this", "base",
        "operator", "switch",
    };

    // Guide §8.1: the keywords that can start a statement.
    public static readonly IReadOnlyList<string> StatementKeywords =
    [
        "break", "class", "continue", "do", "for", "if", "let", "mut", "namespace", "rec",
        "record", "return", "struct", "using", "var", "while",
    ];

    // Guide §8.2: `class`, `namespace`, `using`, `operator` and `where` cannot start an expression, so the
    // expression position offers only these.
    public static readonly IReadOnlyList<string> ExpressionKeywords =
    [
        "false", "if", "new", "not", "null", "switch", "this", "true",
    ];

    // Guide §8.1 and §8.6: the output shorthand and the statement snippets a statement start offers.
    public static readonly IReadOnlyList<PscpCompletionEntry> StatementSnippets =
    [
        new PscpCompletionEntry("=", 15, "= value", "Output shorthand: writes the value without a trailing newline (spec §18.1).", "= ${1:value}", 2, SortKeyword + "0="),
        new PscpCompletionEntry("+=", 15, "+= value", "Output shorthand: writes the value followed by a newline (spec §18.1).", "+= ${1:value}", 2, SortKeyword + "0+="),
        new PscpCompletionEntry(
            "tc",
            15,
            "test case loop",
            "The test-case shape: reads the case count and loops over it.",
            "int ${1:t} =\nfor _ in 0..<${1:t} {\n\t$0\n}",
            2,
            SortKeyword + "0tc"),
        new PscpCompletionEntry(
            "rec",
            15,
            "rec function",
            "A recursive function (`rec` is required for self-recursion, spec §12.4).",
            "rec ${1:int} ${2:solve}(${3:int n}) {\n\t$0\n}",
            2,
            SortKeyword + "0rec"),
        new PscpCompletionEntry(
            "record struct",
            15,
            "record struct",
            "A value record: the compact shape for a point or an edge (spec §26).",
            "record struct ${1:Point}(${2:int x, int y})",
            2,
            SortKeyword + "0record"),
        new PscpCompletionEntry(
            "->",
            15,
            "-> iteration",
            "Iteration shorthand: `xs -> x { ... }` (spec §15.3).",
            "${1:xs} -> ${2:x} {\n\t$0\n}",
            2,
            SortKeyword + "0->"),
    ];

    // Guide §8.1: after `is` and in a switch arm pattern.
    public static readonly IReadOnlyList<string> PatternKeywords = ["and", "not", "null", "or"];

    public static readonly IReadOnlySet<string> BuiltinTypes = PscpIntrinsicCatalog.BuiltinTypes;

    public static readonly IReadOnlyDictionary<string, string> TypeCompletionDetails =
        BuiltinTypes.ToDictionary(
            value => value,
            value => value switch
            {
                "Array" => ".NET type",
                "IEnumerable" or "IComparable" => ".NET interface",
                "Comparer" => ".NET utility type",
                _ => "type",
            },
            StringComparer.Ordinal);

    public static bool IsTypeLikeReceiverName(string receiverName)
    {
        string normalized = StripGenericSuffix(receiverName);
        if (normalized.EndsWith("[]", StringComparison.Ordinal))
        {
            normalized = normalized[..^2];
        }

        return BuiltinTypes.Contains(normalized)
            || (!string.IsNullOrWhiteSpace(normalized) && (char.IsUpper(normalized[0]) || normalized[0] == '('));
    }

    private static IReadOnlyDictionary<string, string> CreateHoverDocs()
    {
        Dictionary<string, string> docs = new(StringComparer.Ordinal);
        foreach (IntrinsicDoc doc in PscpIntrinsicDocs.All)
        {
            string markdown = PscpIntrinsicDocs.FormatMarkdown(doc);
            switch (doc.Kind)
            {
                case IntrinsicDocKind.StdinMember:
                    docs[$"stdin.{doc.Name}"] = markdown;
                    break;
                case IntrinsicDocKind.StdoutMember:
                    docs[$"stdout.{doc.Name}"] = markdown;
                    break;
                case IntrinsicDocKind.Comparator:
                    docs[$"comparer.{doc.Name}"] = markdown;
                    break;
                case IntrinsicDocKind.CollectionHelper:
                    docs[$"collection.{doc.Name}"] = markdown;
                    break;
                default:
                    docs[doc.Name] = markdown;
                    break;
            }
        }

        foreach (IntrinsicDoc doc in PscpIntrinsicDocs.Of(IntrinsicDocKind.Aggregate))
        {
            docs.TryAdd($"collection.{doc.Name}", PscpIntrinsicDocs.FormatMarkdown(doc));
        }

        return docs;
    }

    private static IReadOnlyDictionary<string, PscpSignatureEntry> CreateSignatures()
    {
        Dictionary<string, PscpSignatureEntry> signatures = new(StringComparer.Ordinal);
        foreach (IntrinsicDoc doc in PscpIntrinsicDocs.All)
        {
            string key = doc.Kind switch
            {
                IntrinsicDocKind.StdinMember => $"stdin.{doc.Name}",
                IntrinsicDocKind.StdoutMember => $"stdout.{doc.Name}",
                _ => doc.Name,
            };
            signatures.TryAdd(key, new PscpSignatureEntry(FirstForm(doc.Signature), doc.Parameters, doc.Summary));
        }

        return signatures;
    }

    private static string StripGenericSuffix(string name)
    {
        int genericIndex = name.IndexOf('<');
        return genericIndex >= 0 ? name[..genericIndex] : name;
    }

    private static IReadOnlyDictionary<string, PscpCompletionEntry> Table(IEnumerable<PscpCompletionEntry> entries)
    {
        Dictionary<string, PscpCompletionEntry> table = new(StringComparer.Ordinal);
        foreach (PscpCompletionEntry entry in entries)
        {
            table.TryAdd(entry.Label, entry);
        }

        return table;
    }

    private static IReadOnlyDictionary<string, PscpCompletionEntry> Members(IntrinsicDocKind docKind, int completionKind)
        => Table(PscpIntrinsicDocs.Of(docKind).Select(doc => Completion(doc, completionKind)));

    // `detail` is readable text; the snippet goes to `insertText` (guide §8.4).
    private static PscpCompletionEntry Completion(IntrinsicDoc doc, int kind)
        => new(doc.Name, kind, FirstForm(doc.Signature), PscpIntrinsicDocs.FormatMarkdown(doc), Snippet(doc), 2, SortIntrinsic + doc.Name);

    private static PscpCompletionEntry AggregateMember(IntrinsicDoc doc)
    {
        IReadOnlyList<string> parameters = doc.Name is "sumBy" or "minBy" or "maxBy" ? doc.Parameters.Skip(1).ToArray() : [];
        string insert = parameters.Count == 0 ? $"{doc.Name}()" : $"{doc.Name}({string.Join(", ", parameters.Select((parameter, index) => $"${{{index + 1}:{parameter}}}"))})";
        string detail = parameters.Count == 0 ? $"xs.{doc.Name}()" : $"xs.{doc.Name}({string.Join(", ", parameters)})";
        return new PscpCompletionEntry(doc.Name, 2, detail, PscpIntrinsicDocs.FormatMarkdown(doc), insert, 2, SortIntrinsic + doc.Name);
    }

    private static string FirstForm(string signature)
    {
        int separator = signature.IndexOf(" / ", StringComparison.Ordinal);
        return separator < 0 ? signature : signature[..separator];
    }

    // `stdin.readArray<T>(n)` → `readArray<${1:T}>(${2:n})`; `chmin(ref target, value)` → `chmin(ref ${1:target}, ${2:value})`.
    private static string Snippet(IntrinsicDoc doc)
    {
        string form = FirstForm(doc.Signature);
        int nameStart = form.IndexOf(doc.Name, StringComparison.Ordinal);
        string call = nameStart < 0 ? doc.Name + "()" : form[nameStart..];
        int placeholder = 1;
        string ReplaceList(string list)
            => string.Join(", ", list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(part => part != "...")
                .Select(part => part.StartsWith("ref ", StringComparison.Ordinal)
                    ? $"ref ${{{placeholder++}:{part[4..]}}}"
                    : $"${{{placeholder++}:{part}}}"));

        string result = call;
        int genericOpen = result.IndexOf('<');
        int genericClose = genericOpen < 0 ? -1 : result.IndexOf('>', genericOpen);
        if (genericOpen >= 0 && genericClose > genericOpen)
        {
            result = result[..(genericOpen + 1)] + ReplaceList(result[(genericOpen + 1)..genericClose]) + result[genericClose..];
        }

        int open = result.LastIndexOf('(');
        int close = result.LastIndexOf(')');
        if (open >= 0 && close > open)
        {
            result = result[..(open + 1)] + ReplaceList(result[(open + 1)..close]) + result[close..];
        }

        return result;
    }
}
