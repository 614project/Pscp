namespace Pscp.LanguageServer;

internal static class PscpIntrinsics
{
    public static readonly IReadOnlyDictionary<string, PscpCompletionEntry> Globals =
        new Dictionary<string, PscpCompletionEntry>(StringComparer.Ordinal)
        {
            ["stdin"] = new("stdin", 6, "intrinsic object", "Contest-oriented input helper object."),
            ["stdout"] = new("stdout", 6, "intrinsic object", "Contest-oriented output helper object."),
            ["Array"] = new("Array", 7, "type", "Built-in .NET array type with `Array.zero(n)` helper surface."),
        };

    public static readonly IReadOnlyDictionary<string, PscpCompletionEntry> StdinMembers =
        new Dictionary<string, PscpCompletionEntry>(StringComparer.Ordinal)
        {
            ["readInt"] = Completion("readInt", "readInt()", "Reads one integer token.", 2),
            ["readLong"] = Completion("readLong", "readLong()", "Reads one long integer token.", 2),
            ["readDouble"] = Completion("readDouble", "readDouble()", "Reads one floating-point token.", 2),
            ["readDecimal"] = Completion("readDecimal", "readDecimal()", "Reads one decimal token.", 2),
            ["readBool"] = Completion("readBool", "readBool()", "Reads one boolean token.", 2),
            ["readChar"] = Completion("readChar", "readChar()", "Reads one character token.", 2),
            ["readString"] = Completion("readString", "readString()", "Reads one string token.", 2),
            ["readLine"] = Completion("readLine", "readLine()", "Reads the next physical input line.", 2),
            ["readRestOfLine"] = Completion("readRestOfLine", "readRestOfLine()", "Reads the current physical line remainder.", 2),
            ["readLines"] = Completion("readLines", "readLines(${1:n})", "Reads `n` full lines.", 15, "readLines($1)"),
            ["readWords"] = Completion("readWords", "readWords()", "Reads one line and splits it into words.", 2),
            ["readChars"] = Completion("readChars", "readChars()", "Reads one line as `char[]`.", 2),
            ["readArray"] = Completion("readArray", "readArray<T>(${1:n})", "Compile-time generic sugar that lowers to specialized typed array reads.", 15, "readArray<T>($1)"),
            ["readList"] = Completion("readList", "readList<T>(${1:n})", "Compile-time generic sugar that lowers to specialized typed reads and `List<T>` materialization.", 15, "readList<T>($1)"),
            ["readLinkedList"] = Completion("readLinkedList", "readLinkedList<T>(${1:n})", "Compile-time generic sugar that lowers to specialized typed reads and `LinkedList<T>` materialization.", 15, "readLinkedList<T>($1)"),
            ["readTuple2"] = Completion("readTuple2", "readTuple2<T1, T2>()", "Compile-time generic sugar that lowers to specialized tuple reads.", 2),
            ["readTuple3"] = Completion("readTuple3", "readTuple3<T1, T2, T3>()", "Compile-time generic sugar that lowers to specialized tuple reads.", 2),
            ["readTuples2"] = Completion("readTuples2", "readTuples2<T1, T2>(${1:n})", "Compile-time generic sugar that lowers to specialized tuple-array reads.", 15, "readTuples2<T1, T2>($1)"),
            ["readTuples3"] = Completion("readTuples3", "readTuples3<T1, T2, T3>(${1:n})", "Compile-time generic sugar that lowers to specialized tuple-array reads.", 15, "readTuples3<T1, T2, T3>($1)"),
            ["readNestedArray"] = Completion("readNestedArray", "readNestedArray<T>(${1:n}, ${2:m})", "Compile-time generic sugar that lowers to specialized typed nested-array reads.", 15, "readNestedArray<T>($1, $2)"),
            ["readGridInt"] = Completion("readGridInt", "readGridInt(${1:n}, ${2:m})", "Reads an `int` grid.", 15, "readGridInt($1, $2)"),
            ["readGridLong"] = Completion("readGridLong", "readGridLong(${1:n}, ${2:m})", "Reads a `long` grid.", 15, "readGridLong($1, $2)"),
            ["readCharGrid"] = Completion("readCharGrid", "readCharGrid(${1:n})", "Reads `n` lines as `char[][]`.", 15, "readCharGrid($1)"),
            ["readWordGrid"] = Completion("readWordGrid", "readWordGrid(${1:n})", "Reads `n` lines as `string[][]`.", 15, "readWordGrid($1)"),
        };

    public static readonly IReadOnlyDictionary<string, PscpCompletionEntry> StdoutMembers =
        new Dictionary<string, PscpCompletionEntry>(StringComparer.Ordinal)
        {
            ["write"] = Completion("write", "write(${1:value})", "Writes a value without a trailing newline.", 15, "write($1)"),
            ["writeln"] = Completion("writeln", "writeln(${1:value})", "Writes a value with a trailing newline.", 15, "writeln($1)"),
            ["flush"] = Completion("flush", "flush()", "Flushes buffered output.", 2),
            ["lines"] = Completion("lines", "lines(${1:values})", "Writes one element per line.", 15, "lines($1)"),
            ["grid"] = Completion("grid", "grid(${1:grid})", "Writes a grid line by line.", 15, "grid($1)"),
            ["join"] = Completion("join", "join(${1:sep}, ${2:values})", "Writes values joined by a separator.", 15, "join($1, $2)"),
        };

    public static readonly IReadOnlyDictionary<string, PscpCompletionEntry> ArrayMembers =
        new Dictionary<string, PscpCompletionEntry>(StringComparer.Ordinal)
        {
            ["zero"] = Completion("zero", "zero(${1:n})", "Creates a zero-initialized array from type context.", 15, "zero($1)"),
        };

    public static readonly IReadOnlyDictionary<string, PscpCompletionEntry> ComparatorMembers =
        new Dictionary<string, PscpCompletionEntry>(StringComparer.Ordinal)
        {
            ["asc"] = new("asc", 10, "ascending comparer", "Comparator sugar that resolves to the default ascending comparer for the receiver type."),
            ["desc"] = new("desc", 10, "descending comparer", "Comparator sugar that resolves to the default descending comparer for the receiver type."),
        };

    public static readonly IReadOnlyDictionary<string, PscpCompletionEntry> IntrinsicFunctions =
        new Dictionary<string, PscpCompletionEntry>(StringComparer.Ordinal)
        {
            ["sum"] = Function("sum", "sum(${1:values})", "Sums the elements of an iterable.", "sum($1)"),
            ["sumBy"] = Function("sumBy", "sumBy(${1:values}, ${2:selector})", "Maps each element and sums the projected values.", "sumBy($1, $2)"),
            ["min"] = Function("min", "min(${1:left}, ${2:right})", "Returns the smaller of two values, or the minimum element of a sequence.", "min($1, $2)"),
            ["max"] = Function("max", "max(${1:left}, ${2:right})", "Returns the larger of two values, or the maximum element of a sequence.", "max($1, $2)"),
            ["minBy"] = Function("minBy", "minBy(${1:values}, ${2:keySelector})", "Returns the element whose key is minimal.", "minBy($1, $2)"),
            ["maxBy"] = Function("maxBy", "maxBy(${1:values}, ${2:keySelector})", "Returns the element whose key is maximal.", "maxBy($1, $2)"),
            ["count"] = Function("count", "count(${1:values}, ${2:predicate})", "Counts the elements that satisfy the predicate.", "count($1, $2)"),
            ["any"] = Function("any", "any(${1:values}, ${2:predicate})", "Returns whether any element satisfies the predicate.", "any($1, $2)"),
            ["all"] = Function("all", "all(${1:values}, ${2:predicate})", "Returns whether all elements satisfy the predicate.", "all($1, $2)"),
            ["find"] = Function("find", "find(${1:values}, ${2:predicate})", "Returns the first matching element, or the default value.", "find($1, $2)"),
            ["findIndex"] = Function("findIndex", "findIndex(${1:values}, ${2:predicate})", "Returns the index of the first matching element, or `-1`.", "findIndex($1, $2)"),
            ["findLastIndex"] = Function("findLastIndex", "findLastIndex(${1:values}, ${2:predicate})", "Returns the index of the last matching element, or `-1`.", "findLastIndex($1, $2)"),
            ["sort"] = Function("sort", "sort(${1:values})", "Returns the values sorted by their default ordering.", "sort($1)"),
            ["sortBy"] = Function("sortBy", "sortBy(${1:values}, ${2:keySelector})", "Returns the values sorted by the projected key.", "sortBy($1, $2)"),
            ["sortWith"] = Function("sortWith", "sortWith(${1:values}, ${2:comparer})", "Returns the values sorted with a custom comparer.", "sortWith($1, $2)"),
            ["distinct"] = Function("distinct", "distinct(${1:values})", "Returns distinct elements preserving source order where possible.", "distinct($1)"),
            ["reverse"] = Function("reverse", "reverse(${1:values})", "Returns the values in reverse order.", "reverse($1)"),
            ["copy"] = Function("copy", "copy(${1:values})", "Returns a copied materialized sequence.", "copy($1)"),
            ["abs"] = Function("abs", "abs(${1:value})", "Returns the absolute value using the math intrinsic lowering.", "abs($1)"),
            ["sqrt"] = Function("sqrt", "sqrt(${1:value})", "Returns the square root using the math intrinsic lowering.", "sqrt($1)"),
            ["clamp"] = Function("clamp", "clamp(${1:value}, ${2:min}, ${3:max})", "Clamps a value into the given inclusive range.", "clamp($1, $2, $3)"),
            ["gcd"] = Function("gcd", "gcd(${1:left}, ${2:right})", "Returns the greatest common divisor of two integers.", "gcd($1, $2)"),
            ["lcm"] = Function("lcm", "lcm(${1:left}, ${2:right})", "Returns the least common multiple of two integers.", "lcm($1, $2)"),
            ["floor"] = Function("floor", "floor(${1:value})", "Returns the floor using Math-compatible lowering.", "floor($1)"),
            ["ceil"] = Function("ceil", "ceil(${1:value})", "Returns the ceiling using Math-compatible lowering.", "ceil($1)"),
            ["round"] = Function("round", "round(${1:value})", "Returns the rounded value using Math-compatible lowering.", "round($1)"),
            ["pow"] = Function("pow", "pow(${1:value}, ${2:power})", "Returns the value raised to the given power.", "pow($1, $2)"),
            ["popcount"] = Function("popcount", "popcount(${1:value})", "Counts one-bits in an integer value.", "popcount($1)"),
            ["bitLength"] = Function("bitLength", "bitLength(${1:value})", "Returns the effective bit length of an integer value.", "bitLength($1)"),
            ["groupCount"] = Function("groupCount", "groupCount(${1:values})", "Counts occurrences of each distinct value.", "groupCount($1)"),
            ["freq"] = Function("freq", "freq(${1:values})", "Alias of `groupCount`.", "freq($1)"),
            ["index"] = Function("index", "index(${1:values})", "Maps each distinct value to its first index.", "index($1)"),
            ["chmin"] = Function("chmin", "chmin(ref ${1:target}, ${2:value})", "Updates `target` when `value` is smaller, and returns whether it changed.", "chmin(ref $1, $2)"),
            ["chmax"] = Function("chmax", "chmax(ref ${1:target}, ${2:value})", "Updates `target` when `value` is larger, and returns whether it changed.", "chmax(ref $1, $2)"),
        };

    public static readonly IReadOnlyDictionary<string, PscpSignatureEntry> Signatures =
        new Dictionary<string, PscpSignatureEntry>(StringComparer.Ordinal)
        {
            ["stdin.readInt"] = new("stdin.readInt()", Array.Empty<string>(), "Reads one integer token."),
            ["stdin.readLong"] = new("stdin.readLong()", Array.Empty<string>(), "Reads one long integer token."),
            ["stdin.readDouble"] = new("stdin.readDouble()", Array.Empty<string>(), "Reads one floating-point token."),
            ["stdin.readDecimal"] = new("stdin.readDecimal()", Array.Empty<string>(), "Reads one decimal token."),
            ["stdin.readBool"] = new("stdin.readBool()", Array.Empty<string>(), "Reads one boolean token."),
            ["stdin.readChar"] = new("stdin.readChar()", Array.Empty<string>(), "Reads one character token."),
            ["stdin.readString"] = new("stdin.readString()", Array.Empty<string>(), "Reads one string token."),
            ["stdin.readLine"] = new("stdin.readLine()", Array.Empty<string>(), "Reads the next physical input line."),
            ["stdin.readRestOfLine"] = new("stdin.readRestOfLine()", Array.Empty<string>(), "Reads the current physical line remainder."),
            ["stdin.readLines"] = new("stdin.readLines(n)", new[] { "n" }, "Reads `n` lines."),
            ["stdin.readWords"] = new("stdin.readWords()", Array.Empty<string>(), "Reads one line and splits it into words."),
            ["stdin.readChars"] = new("stdin.readChars()", Array.Empty<string>(), "Reads one line as `char[]`."),
            ["stdin.readArray"] = new("stdin.readArray<T>(n)", new[] { "n" }, "Compile-time generic sugar that lowers to specialized typed array reads."),
            ["stdin.readList"] = new("stdin.readList<T>(n)", new[] { "n" }, "Compile-time generic sugar that lowers to specialized typed reads and `List<T>` materialization."),
            ["stdin.readLinkedList"] = new("stdin.readLinkedList<T>(n)", new[] { "n" }, "Compile-time generic sugar that lowers to specialized typed reads and `LinkedList<T>` materialization."),
            ["stdin.readTuple2"] = new("stdin.readTuple2<T1, T2>()", Array.Empty<string>(), "Compile-time generic sugar that lowers to specialized tuple reads."),
            ["stdin.readTuple3"] = new("stdin.readTuple3<T1, T2, T3>()", Array.Empty<string>(), "Compile-time generic sugar that lowers to specialized tuple reads."),
            ["stdin.readTuples2"] = new("stdin.readTuples2<T1, T2>(n)", new[] { "n" }, "Compile-time generic sugar that lowers to specialized tuple-array reads."),
            ["stdin.readTuples3"] = new("stdin.readTuples3<T1, T2, T3>(n)", new[] { "n" }, "Compile-time generic sugar that lowers to specialized tuple-array reads."),
            ["stdin.readNestedArray"] = new("stdin.readNestedArray<T>(n, m)", new[] { "n", "m" }, "Compile-time generic sugar that lowers to specialized typed nested-array reads."),
            ["stdin.readGridInt"] = new("stdin.readGridInt(n, m)", new[] { "n", "m" }, "Reads an integer grid."),
            ["stdin.readGridLong"] = new("stdin.readGridLong(n, m)", new[] { "n", "m" }, "Reads a long integer grid."),
            ["stdin.readCharGrid"] = new("stdin.readCharGrid(n)", new[] { "n" }, "Reads `n` lines as a character grid."),
            ["stdin.readWordGrid"] = new("stdin.readWordGrid(n)", new[] { "n" }, "Reads `n` lines as a word grid."),
            ["stdout.write"] = new("stdout.write(value)", new[] { "value" }, "Writes one rendered value without newline."),
            ["stdout.writeln"] = new("stdout.writeln(value)", new[] { "value" }, "Writes one rendered value with newline."),
            ["stdout.flush"] = new("stdout.flush()", Array.Empty<string>(), "Flushes buffered output."),
            ["stdout.lines"] = new("stdout.lines(values)", new[] { "values" }, "Writes one value per line."),
            ["stdout.grid"] = new("stdout.grid(grid)", new[] { "grid" }, "Writes a grid line by line."),
            ["stdout.join"] = new("stdout.join(sep, values)", new[] { "sep", "values" }, "Writes values joined by a separator."),
            ["Array.zero"] = new("Array.zero(n)", new[] { "n" }, "Creates a zero-initialized array from the target type context."),
            ["sum"] = new("sum(values)", new[] { "values" }, "Sums the elements of an iterable."),
            ["sumBy"] = new("sumBy(values, selector)", new[] { "values", "selector" }, "Maps each element and sums the projected values."),
            ["min"] = new("min(left, right)", new[] { "left", "right" }, "Returns the smaller of two values."),
            ["max"] = new("max(left, right)", new[] { "left", "right" }, "Returns the larger of two values."),
            ["minBy"] = new("minBy(values, keySelector)", new[] { "values", "keySelector" }, "Returns the element whose key is minimal."),
            ["maxBy"] = new("maxBy(values, keySelector)", new[] { "values", "keySelector" }, "Returns the element whose key is maximal."),
            ["count"] = new("count(values, predicate)", new[] { "values", "predicate" }, "Counts matching elements."),
            ["any"] = new("any(values, predicate)", new[] { "values", "predicate" }, "Returns whether any element matches."),
            ["all"] = new("all(values, predicate)", new[] { "values", "predicate" }, "Returns whether all elements match."),
            ["find"] = new("find(values, predicate)", new[] { "values", "predicate" }, "Returns the first matching element."),
            ["findIndex"] = new("findIndex(values, predicate)", new[] { "values", "predicate" }, "Returns the first matching index, or `-1`."),
            ["findLastIndex"] = new("findLastIndex(values, predicate)", new[] { "values", "predicate" }, "Returns the last matching index, or `-1`."),
            ["sort"] = new("sort(values)", new[] { "values" }, "Returns the values sorted by their default ordering."),
            ["sortBy"] = new("sortBy(values, keySelector)", new[] { "values", "keySelector" }, "Returns the values sorted by a projected key."),
            ["sortWith"] = new("sortWith(values, comparer)", new[] { "values", "comparer" }, "Returns the values sorted with a custom comparer."),
            ["distinct"] = new("distinct(values)", new[] { "values" }, "Returns distinct values."),
            ["reverse"] = new("reverse(values)", new[] { "values" }, "Returns the values in reverse order."),
            ["copy"] = new("copy(values)", new[] { "values" }, "Returns a copied materialized sequence."),
            ["abs"] = new("abs(value)", new[] { "value" }, "Returns the absolute value."),
            ["sqrt"] = new("sqrt(value)", new[] { "value" }, "Returns the square root."),
            ["clamp"] = new("clamp(value, min, max)", new[] { "value", "min", "max" }, "Clamps a value into the given inclusive range."),
            ["gcd"] = new("gcd(left, right)", new[] { "left", "right" }, "Returns the greatest common divisor of two integers."),
            ["lcm"] = new("lcm(left, right)", new[] { "left", "right" }, "Returns the least common multiple of two integers."),
            ["floor"] = new("floor(value)", new[] { "value" }, "Returns the floor using Math-compatible semantics."),
            ["ceil"] = new("ceil(value)", new[] { "value" }, "Returns the ceiling using Math-compatible semantics."),
            ["round"] = new("round(value)", new[] { "value" }, "Returns the rounded value using Math-compatible semantics."),
            ["pow"] = new("pow(value, power)", new[] { "value", "power" }, "Returns the value raised to the given power."),
            ["popcount"] = new("popcount(value)", new[] { "value" }, "Counts one-bits in an integer value."),
            ["bitLength"] = new("bitLength(value)", new[] { "value" }, "Returns the effective bit length of an integer value."),
            ["groupCount"] = new("groupCount(values)", new[] { "values" }, "Counts occurrences of each distinct value."),
            ["freq"] = new("freq(values)", new[] { "values" }, "Alias of `groupCount`."),
            ["index"] = new("index(values)", new[] { "values" }, "Maps each distinct value to its first index."),
            ["chmin"] = new("chmin(ref target, value)", new[] { "target", "value" }, "Updates `target` when `value` is smaller."),
            ["chmax"] = new("chmax(ref target, value)", new[] { "target", "value" }, "Updates `target` when `value` is larger."),
        };

    public static readonly IReadOnlyDictionary<string, string> HoverDocs = CreateHoverDocs();

    public static readonly IReadOnlySet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "let", "var", "mut", "rec", "if", "then", "else", "for", "in", "do", "while",
        "break", "continue", "return", "true", "false", "null", "and", "or", "xor", "not",
        "where", "new", "class", "struct", "record", "namespace", "using",
        "ref", "out", "is", "public", "private", "protected", "internal", "this", "base",
        "operator", "switch",
    };

    public static readonly IReadOnlySet<string> BuiltinTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "int", "long", "double", "decimal", "bool", "char", "string", "void", "List", "LinkedList",
        "Queue", "Stack", "HashSet", "Dictionary", "PriorityQueue", "SortedSet",
        "IEnumerable", "IComparable", "Comparer", "Array",
    };

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
        Dictionary<string, string> docs = Signatures.ToDictionary(
            pair => pair.Key,
            pair => $"```pscp\n{pair.Value.Label}\n```\n\n{pair.Value.Documentation}",
            StringComparer.Ordinal);

        docs["comparer.asc"] = "```pscp\nT.asc\n```\n\nComparator sugar that resolves to the default ascending comparer for the receiver type.";
        docs["comparer.desc"] = "```pscp\nT.desc\n```\n\nComparator sugar that resolves to the default descending comparer for the receiver type.";
        return docs;
    }

    private static string StripGenericSuffix(string name)
    {
        int genericIndex = name.IndexOf('<');
        return genericIndex >= 0 ? name[..genericIndex] : name;
    }

    private static PscpCompletionEntry Completion(string label, string detail, string documentation, int kind, string? insertText = null)
        => new(label, kind, detail, documentation, insertText, insertText is null ? null : 2, label);

    private static PscpCompletionEntry Function(string label, string detail, string documentation, string insertText)
        => new(label, 3, detail, documentation, insertText, 2, label);
}
