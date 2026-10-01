namespace Pscp.Transpiler;

public enum IntrinsicDocKind
{
    // `stdin`, `stdout`.
    Object,
    StdinMember,
    StdoutMember,
    // `min`, `max`, `sum`, `sumBy`, `minBy`, `maxBy`, `chmin`, `chmax`: free, space-call and member forms (spec §22).
    Aggregate,
    // `abs`, `pow`, ...: free and space-call forms (spec §23).
    Math,
    // Member-only collection helpers (spec §24); names allowed as pipe targets.
    CollectionHelper,
    // `T.asc`, `T.desc` (spec §25.2).
    Comparator,
}

// One intrinsic as the editor presents it. `Signature` is readable text (never snippet syntax); `Parameters` are
// the argument names used for snippets and signature help.
public sealed record IntrinsicDoc(
    string Name,
    IntrinsicDocKind Kind,
    string Signature,
    IReadOnlyList<string> Parameters,
    string ResultType,
    string Summary,
    string? SpecSection = null);

// The documentation of every PSCP intrinsic (guide principle 3): the language server's completion, hover and
// signature help come from here. Deprecated names are not listed; they are in PscpSemanticAnalyzer.
public static class PscpIntrinsicDocs
{
    public static readonly IReadOnlyList<IntrinsicDoc> All =
    [
        new("stdin", IntrinsicDocKind.Object, "stdin", [], "stdin", "Fast token and line input (spec §17). `int n =` declarations read from it too.", "§17.2"),
        new("stdout", IntrinsicDocKind.Object, "stdout", [], "stdout", "Buffered output shared with `Console.Out`, flushed at the end of the program (spec §18).", "§18.2"),

        new("readInt", IntrinsicDocKind.StdinMember, "stdin.readInt()", [], "int", "Reads one integer token (optional sign, decimal digits).", "§17.3"),
        new("readLong", IntrinsicDocKind.StdinMember, "stdin.readLong()", [], "long", "Reads one `long` token.", "§17.3"),
        new("readDouble", IntrinsicDocKind.StdinMember, "stdin.readDouble()", [], "double", "Reads one `double` token (invariant culture, exponent allowed).", "§17.3"),
        new("readDecimal", IntrinsicDocKind.StdinMember, "stdin.readDecimal()", [], "decimal", "Reads one `decimal` token (invariant culture).", "§17.3"),
        new("readBool", IntrinsicDocKind.StdinMember, "stdin.readBool()", [], "bool", "Reads `true`, `false`, `1` or `0` in any case.", "§17.3"),
        new("readString", IntrinsicDocKind.StdinMember, "stdin.readString()", [], "string", "Reads the next whitespace-separated token.", "§17.3"),
        new("readChar", IntrinsicDocKind.StdinMember, "stdin.readChar()", [], "char", "Skips whitespace and reads **one character**; the rest of the token stays unread.", "§17.3"),
        new("readArray", IntrinsicDocKind.StdinMember, "stdin.readArray<T>(n)", ["n"], "T[]", "Reads `n` values of a token-readable `T` (scalars or flat tuples).", "§17.2"),
        new("readList", IntrinsicDocKind.StdinMember, "stdin.readList<T>(n)", ["n"], "List<T>", "Reads `n` values into a `List<T>`.", "§17.2"),
        new("readLinkedList", IntrinsicDocKind.StdinMember, "stdin.readLinkedList<T>(n)", ["n"], "LinkedList<T>", "Reads `n` values into a `LinkedList<T>`.", "§17.2"),
        new("readTuple", IntrinsicDocKind.StdinMember, "stdin.readTuple<T1, T2, ...>()", [], "(T1, T2, ...)", "Reads 2 to 7 values in order as a tuple.", "§17.2"),
        new("readGrid", IntrinsicDocKind.StdinMember, "stdin.readGrid<T>(n, m)", ["n", "m"], "T[][]", "Reads an `n × m` grid of tokens (characters when `T` is `char`).", "§17.2"),
        new("readLine", IntrinsicDocKind.StdinMember, "stdin.readLine()", [], "string", "Reads a line. After a token read it returns the rest of the current line, or the next line when nothing is left (spec §17.4).", "§17.4"),
        new("readRestOfLine", IntrinsicDocKind.StdinMember, "stdin.readRestOfLine()", [], "string", "Returns the rest of the current line as it is, possibly empty.", "§17.4"),
        new("readLines", IntrinsicDocKind.StdinMember, "stdin.readLines(n)", ["n"], "string[]", "Calls `readLine()` `n` times.", "§17.4"),
        new("readWords", IntrinsicDocKind.StdinMember, "stdin.readWords()", [], "string[]", "Reads a line and splits it on whitespace; an empty line gives an empty array.", "§17.4"),
        new("readChars", IntrinsicDocKind.StdinMember, "stdin.readChars()", [], "char[]", "Reads a line as `char[]`.", "§17.4"),
        new("readCharGrid", IntrinsicDocKind.StdinMember, "stdin.readCharGrid(n)", ["n"], "char[][]", "Reads `n` lines as characters; spaces inside the lines are kept.", "§17.4"),
        new("readWordGrid", IntrinsicDocKind.StdinMember, "stdin.readWordGrid(n)", ["n"], "string[][]", "Reads `n` lines split into words.", "§17.4"),
        new("hasNext", IntrinsicDocKind.StdinMember, "stdin.hasNext()", [], "bool", "Whether another token is left. May skip whitespace.", "§17.5"),
        new("hasNextLine", IntrinsicDocKind.StdinMember, "stdin.hasNextLine()", [], "bool", "Whether the next `readLine()` can return a line.", "§17.5"),

        new("write", IntrinsicDocKind.StdoutMember, "stdout.write(value)", ["value"], "void", "Writes a rendered value without a line break (same as `= value`).", "§18.2"),
        new("writeln", IntrinsicDocKind.StdoutMember, "stdout.writeln(value)", ["value"], "void", "Writes a rendered value and `\\n` (same as `+= value`); `writeln()` writes only `\\n`.", "§18.2"),
        new("flush", IntrinsicDocKind.StdoutMember, "stdout.flush()", [], "void", "Flushes the buffer. Call it before waiting for an interactive judge.", "§30.3"),
        new("lines", IntrinsicDocKind.StdoutMember, "stdout.lines(values)", ["values"], "void", "Writes each element on its own line.", "§18.2"),
        new("grid", IntrinsicDocKind.StdoutMember, "stdout.grid(rows)", ["rows"], "void", "Writes each row on its own line (same as `+= rows`).", "§18.2"),
        new("join", IntrinsicDocKind.StdoutMember, "stdout.join(separator, values)", ["separator", "values"], "void", "Writes the values joined by a `string` or `char` separator, without a line break.", "§18.2"),

        new("min", IntrinsicDocKind.Aggregate, "min(a, b, ...) / min(xs)", ["a", "b"], "T", "The smallest argument, or the smallest element of one iterable, in the default order. Ties keep the first value. An empty iterable is a precondition violation.", "§22.2"),
        new("max", IntrinsicDocKind.Aggregate, "max(a, b, ...) / max(xs)", ["a", "b"], "T", "The largest argument, or the largest element of one iterable, in the default order. Ties keep the first value.", "§22.2"),
        new("sum", IntrinsicDocKind.Aggregate, "sum(xs)", ["xs"], "T", "The sum of the elements; `0` when empty. `long total = sum a` accumulates in `long` (spec §22.6).", "§22.3"),
        new("sumBy", IntrinsicDocKind.Aggregate, "sumBy(xs, f)", ["xs", "f"], "T", "The sum of `f(x)` over the elements, evaluating `f` once per element.", "§22.3"),
        new("minBy", IntrinsicDocKind.Aggregate, "minBy(xs, key)", ["xs", "key"], "T", "The element with the smallest key; ties keep the first element.", "§22.4"),
        new("maxBy", IntrinsicDocKind.Aggregate, "maxBy(xs, key)", ["xs", "key"], "T", "The element with the largest key; ties keep the first element.", "§22.4"),
        new("chmin", IntrinsicDocKind.Aggregate, "chmin(ref target, value)", ["ref target", "value"], "bool", "Sets `target` to `value` when `value` is strictly smaller, and returns whether it changed.", "§22.5"),
        new("chmax", IntrinsicDocKind.Aggregate, "chmax(ref target, value)", ["ref target", "value"], "bool", "Sets `target` to `value` when `value` is strictly larger, and returns whether it changed.", "§22.5"),

        new("abs", IntrinsicDocKind.Math, "abs(x)", ["x"], "T", "Absolute value of an `int`, `long`, `double` or `decimal`.", "§23.1"),
        new("sqrt", IntrinsicDocKind.Math, "sqrt(x)", ["x"], "double", "Square root.", "§23.1"),
        new("clamp", IntrinsicDocKind.Math, "clamp(x, lo, hi)", ["x", "lo", "hi"], "T", "`x` limited to `[lo, hi]`; `lo > hi` is a precondition violation.", "§23.1"),
        new("gcd", IntrinsicDocKind.Math, "gcd(a, b)", ["a", "b"], "T", "Non-negative greatest common divisor; `gcd(0, 0) = 0`.", "§23.1"),
        new("lcm", IntrinsicDocKind.Math, "lcm(a, b)", ["a", "b"], "T", "Non-negative least common multiple; `0` when either is `0`.", "§23.1"),
        new("floor", IntrinsicDocKind.Math, "floor(x) / floor(a, b)", ["a", "b"], "T", "`floor(x)` rounds a `double`/`decimal` down. `floor(a, b)` is integer division rounded down: `floor(-7, 2) = -4`.", "§23.1"),
        new("ceil", IntrinsicDocKind.Math, "ceil(x) / ceil(a, b)", ["a", "b"], "T", "`ceil(x)` rounds a `double`/`decimal` up. `ceil(a, b)` is integer division rounded up: `ceil(7, 2) = 4`.", "§23.1"),
        new("round", IntrinsicDocKind.Math, "round(x) / round(x, digits)", ["x", "digits"], "T", "Rounds half **away from zero**: `round 2.5 = 3`, `round -2.5 = -3`.", "§23.1"),
        new("pow", IntrinsicDocKind.Math, "pow(a, e) / pow(a, e, m)", ["a", "e", "m"], "T", "Exact integer power for `int`/`long` (never `Math.Pow`), real power for `double`; `pow(a, e, m)` is `a^e mod m` in `[0, m)`.", "§23.1"),
        new("popcount", IntrinsicDocKind.Math, "popcount(x)", ["x"], "int", "Number of one bits (two's complement).", "§23.1"),
        new("bitLength", IntrinsicDocKind.Math, "bitLength(x)", ["x"], "int", "Bits needed to write `x`: `bitLength(0) = 0`, `bitLength(5) = 3`.", "§23.1"),

        new("map", IntrinsicDocKind.CollectionHelper, "xs.map(f)", ["f"], "U[]", "A **new array** of `f(x)`; the receiver is not changed.", "§24.3"),
        new("filter", IntrinsicDocKind.CollectionHelper, "xs.filter(p)", ["p"], "T[]", "A new array of the elements that satisfy `p`.", "§24.3"),
        new("fold", IntrinsicDocKind.CollectionHelper, "xs.fold(seed, f)", ["seed", "f"], "S", "Folds from the left: `f(f(seed, x0), x1) ...`.", "§24.3"),
        new("scan", IntrinsicDocKind.CollectionHelper, "xs.scan(seed, f)", ["seed", "f"], "S[]", "The fold states, starting with `seed`.", "§24.3"),
        new("mapFold", IntrinsicDocKind.CollectionHelper, "xs.mapFold(seed, f)", ["seed", "f"], "(U[], S)", "Maps while carrying a state; `f` returns `(mapped, nextState)`.", "§24.3"),
        new("any", IntrinsicDocKind.CollectionHelper, "xs.any(p)", ["p"], "bool", "Whether an element satisfies `p` (or, without `p`, whether there is an element).", "§24.3"),
        new("all", IntrinsicDocKind.CollectionHelper, "xs.all(p)", ["p"], "bool", "Whether every element satisfies `p`.", "§24.3"),
        new("count", IntrinsicDocKind.CollectionHelper, "xs.count(p)", ["p"], "int", "The number of elements (that satisfy `p`).", "§24.3"),
        new("find", IntrinsicDocKind.CollectionHelper, "xs.find(p) / xs.find(p, fallback)", ["p", "fallback"], "T?", "The first element that satisfies `p`, or `null` (value types: an empty nullable) / `fallback`.", "§24.3"),
        new("findIndex", IntrinsicDocKind.CollectionHelper, "xs.findIndex(p)", ["p"], "int", "The index of the first element that satisfies `p`, or `-1`.", "§24.3"),
        new("findLastIndex", IntrinsicDocKind.CollectionHelper, "xs.findLastIndex(p)", ["p"], "int", "The index of the last element that satisfies `p`, or `-1`.", "§24.3"),
        new("sort", IntrinsicDocKind.CollectionHelper, "xs.sort()", [], "T[]", "A **new sorted array** in the default order (stable); the receiver is not changed. Sort in place with `Array.Sort(xs)`.", "§24.3"),
        new("sortBy", IntrinsicDocKind.CollectionHelper, "xs.sortBy(key)", ["key"], "T[]", "A new array stably sorted by `key`, evaluated once per element.", "§24.3"),
        new("sortWith", IntrinsicDocKind.CollectionHelper, "xs.sortWith(comparer)", ["comparer"], "T[]", "A new array stably sorted with an `IComparer<T>` (`int.desc`) or a comparison.", "§24.3"),
        new("distinct", IntrinsicDocKind.CollectionHelper, "xs.distinct()", [], "T[]", "A new array without repeated elements, in first-occurrence order.", "§24.3"),
        new("reverse", IntrinsicDocKind.CollectionHelper, "xs.reverse()", [], "T[]", "A new reversed array; the receiver is not changed.", "§24.3"),
        new("copy", IntrinsicDocKind.CollectionHelper, "xs.copy()", [], "T[]", "A new array with the same elements.", "§24.3"),
        new("freq", IntrinsicDocKind.CollectionHelper, "xs.freq()", [], "Dictionary<T, int>", "How many times each value occurs.", "§24.3"),
        new("index", IntrinsicDocKind.CollectionHelper, "xs.index()", [], "Dictionary<T, int>", "Each value mapped to the index of its first occurrence.", "§24.3"),
        new("lowerBound", IntrinsicDocKind.CollectionHelper, "xs.lowerBound(value)", ["value"], "int", "The first index whose element is not less than `value` in a sorted array or list.", "§24.3"),
        new("upperBound", IntrinsicDocKind.CollectionHelper, "xs.upperBound(value)", ["value"], "int", "The first index whose element is greater than `value` in a sorted array or list.", "§24.3"),

        new("asc", IntrinsicDocKind.Comparator, "T.asc", [], "IComparer<T>", "The default order of `T` (ordinal strings, element-wise tuples, `operator<=>`).", "§25.2"),
        new("desc", IntrinsicDocKind.Comparator, "T.desc", [], "IComparer<T>", "The reverse of the default order of `T`.", "§25.2"),
    ];

    public static IEnumerable<IntrinsicDoc> Of(IntrinsicDocKind kind)
        => All.Where(doc => doc.Kind == kind);

    // Hover and completion documentation: the summary and a link to the spec section.
    public static string FormatMarkdown(IntrinsicDoc doc)
        => doc.SpecSection is null
            ? $"```pscp\n{doc.Signature}\n```\n\n{doc.Summary}"
            : $"```pscp\n{doc.Signature}\n```\n\n{doc.Summary}\n\nSpec {doc.SpecSection}";
}
