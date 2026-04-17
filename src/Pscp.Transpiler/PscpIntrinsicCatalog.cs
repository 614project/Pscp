namespace Pscp.Transpiler;

internal static class PscpIntrinsicCatalog
{
    public static readonly IReadOnlySet<string> IntrinsicCallNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "sum", "sumBy", "min", "max", "minBy", "maxBy",
        "count", "any", "all", "find", "findIndex", "findLastIndex",
        "sort", "sortBy", "sortWith", "distinct", "reverse", "copy",
        "groupCount", "freq", "index", "chmin", "chmax"
    };

    public static readonly IReadOnlySet<string> BuiltinTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "int", "long", "double", "decimal", "bool", "char", "string", "void",
        "List", "LinkedList", "Queue", "Stack", "HashSet", "Dictionary", "PriorityQueue",
        "SortedSet", "IEnumerable", "IComparable", "Comparer", "Array"
    };

    public static readonly IReadOnlySet<string> GlobalValues = new HashSet<string>(StringComparer.Ordinal)
    {
        "stdin", "stdout", "Array"
    };

    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> KnownMembers =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["stdin"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "int", "long", "double", "decimal", "bool", "char", "str", "line",
                "lines", "words", "chars", "array", "list", "linkedList", "tuple2", "tuple3",
                "tuples2", "tuples3", "gridInt", "gridLong", "charGrid", "wordGrid", "read", "nestedArray"
            },
            ["stdout"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "write", "writeln", "flush", "lines", "grid", "join"
            },
            ["Array"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "zero"
            },
        };

    public static bool IsMathMinMaxCompatible(TypeSyntax? type)
        => type is NamedTypeSyntax { TypeArguments.Count: 0 } named
            && named.Name is "int" or "long" or "double" or "decimal";

    public static string StripGenericSuffix(string name)
    {
        int genericIndex = name.IndexOf('<');
        return genericIndex >= 0 ? name[..genericIndex] : name;
    }

    public static bool TryGetKnownMembers(string receiverName, out IReadOnlySet<string>? members)
        => KnownMembers.TryGetValue(receiverName, out members);
}
