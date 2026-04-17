namespace Pscp.Transpiler;

internal sealed partial class CSharpEmitter
{
    partial void EmitRuntimeHelpers()
    {
        string programText = _writer.ToString();
        bool verbose = _options.HelperEmission == HelperEmissionMode.Verbose;
        bool wroteBlock = false;

        if (verbose || programText.Contains("__PscpArray.", StringComparison.Ordinal))
        {
            EmitArrayHelpers();
            wroteBlock = true;
        }

        if (verbose || programText.Contains("__PscpThunk.run(", StringComparison.Ordinal))
        {
            if (wroteBlock)
            {
                _writer.WriteLine();
            }

            EmitThunkHelpers();
            wroteBlock = true;
        }

        if (verbose || NeedsSequenceHelpers(programText))
        {
            if (wroteBlock)
            {
                _writer.WriteLine();
            }

            EmitSequenceHelpers();
            wroteBlock = true;
        }

        if (verbose || _emitStdin)
        {
            if (wroteBlock)
            {
                _writer.WriteLine();
            }

            EmitStdinHelpers(programText, verbose);
            wroteBlock = true;
        }

        if (verbose || _emitStdout)
        {
            if (wroteBlock)
            {
                _writer.WriteLine();
            }

            EmitStdoutHelpers(programText, verbose);
            wroteBlock = true;
        }

        if (verbose || _stdoutNeedsFallbackHelpers)
        {
            _writer.WriteLine();
            EmitRenderHelpers();
        }
    }

    private void EmitArrayHelpers()
    {
        WriteRuntimeBlock(
            """
            public static class __PscpArray
            {
                public static T[] zero<T>(int n) => new T[n];

                public static T[] fillNew<T>(int n) where T : new()
                {
                    T[] result = new T[n];
                    for (int i = 0; i < n; i++)
                    {
                        result[i] = new T();
                    }

                    return result;
                }

                public static T[][] jagged<T>(int n, int m)
                {
                    T[][] result = new T[n][];
                    for (int i = 0; i < n; i++)
                    {
                        result[i] = new T[m];
                    }

                    return result;
                }
            }
            """);
    }

    private void EmitThunkHelpers()
    {
        WriteRuntimeBlock(
            """
            public static class __PscpThunk
            {
                public static T run<T>(Func<T> thunk) => thunk();
            }
            """);
    }

    private void EmitSequenceHelpers()
    {
        WriteRuntimeBlock(
            """
            public static class __PscpSeq
            {
                public static T[] arrayOf<T>(params T[] items) => items;
                public static IEnumerable<T> one<T>(T value) => new[] { value };
                public static IEnumerable<T> concat<T>(params IEnumerable<T>[] sequences) => sequences.SelectMany(static sequence => sequence);
                public static T[] toArray<T>(IEnumerable<T> sequence) => sequence as T[] ?? sequence.ToArray();
                public static List<T> toList<T>(IEnumerable<T> sequence) => sequence.ToList();
                public static LinkedList<T> toLinkedList<T>(IEnumerable<T> sequence) => new(sequence);
                public static int compare<T>(T left, T right) => Comparer<T>.Default.Compare(left, right);

                public static IEnumerable<int> rangeInt(int start, int end, bool inclusive)
                    => rangeInt(start, end, 1, inclusive);

                public static IEnumerable<int> rangeInt(int start, int end, int step, bool inclusive)
                {
                    if (step == 0) throw new InvalidOperationException("Range step cannot be zero.");
                    if (step > 0)
                    {
                        for (int i = start; inclusive ? i <= end : i < end; i += step)
                        {
                            yield return i;
                        }

                        yield break;
                    }

                    for (int i = start; inclusive ? i >= end : i > end; i += step)
                    {
                        yield return i;
                    }
                }

                public static IEnumerable<long> rangeLong(long start, long end, bool inclusive)
                    => rangeLong(start, end, 1L, inclusive);

                public static IEnumerable<long> rangeLong(long start, long end, long step, bool inclusive)
                {
                    if (step == 0) throw new InvalidOperationException("Range step cannot be zero.");
                    if (step > 0)
                    {
                        for (long i = start; inclusive ? i <= end : i < end; i += step)
                        {
                            yield return i;
                        }

                        yield break;
                    }

                    for (long i = start; inclusive ? i >= end : i > end; i += step)
                    {
                        yield return i;
                    }
                }

                public static TResult[] map<T, TResult>(this IEnumerable<T> source, Func<T, TResult> selector)
                {
                    List<TResult> result = new();
                    foreach (T item in source)
                    {
                        result.Add(selector(item));
                    }

                    return result.ToArray();
                }

                public static T[] filter<T>(this IEnumerable<T> source, Func<T, bool> predicate)
                {
                    List<T> result = new();
                    foreach (T item in source)
                    {
                        if (predicate(item))
                        {
                            result.Add(item);
                        }
                    }

                    return result.ToArray();
                }

                public static TState fold<T, TState>(this IEnumerable<T> source, TState seed, Func<TState, T, TState> folder)
                {
                    TState state = seed;
                    foreach (T item in source)
                    {
                        state = folder(state, item);
                    }

                    return state;
                }

                public static TState[] scan<T, TState>(this IEnumerable<T> source, TState seed, Func<TState, T, TState> folder)
                {
                    List<TState> values = new();
                    TState state = seed;
                    foreach (T item in source)
                    {
                        state = folder(state, item);
                        values.Add(state);
                    }

                    return values.ToArray();
                }

                public static (TResult[] mapped, TState state) mapFold<T, TResult, TState>(this IEnumerable<T> source, TState seed, Func<TState, T, (TResult mapped, TState state)> folder)
                {
                    List<TResult> values = new();
                    TState state = seed;
                    foreach (T item in source)
                    {
                        (TResult mapped, TState next) = folder(state, item);
                        values.Add(mapped);
                        state = next;
                    }

                    return (values.ToArray(), state);
                }

                public static int sum(this IEnumerable<int> source)
                {
                    int total = 0;
                    foreach (int item in source) total += item;
                    return total;
                }

                public static long sum(this IEnumerable<long> source)
                {
                    long total = 0;
                    foreach (long item in source) total += item;
                    return total;
                }

                public static double sum(this IEnumerable<double> source)
                {
                    double total = 0d;
                    foreach (double item in source) total += item;
                    return total;
                }

                public static decimal sum(this IEnumerable<decimal> source)
                {
                    decimal total = 0m;
                    foreach (decimal item in source) total += item;
                    return total;
                }

                public static int sumBy<T>(this IEnumerable<T> source, Func<T, int> selector)
                {
                    int total = 0;
                    foreach (T item in source) total += selector(item);
                    return total;
                }

                public static long sumBy<T>(this IEnumerable<T> source, Func<T, long> selector)
                {
                    long total = 0;
                    foreach (T item in source) total += selector(item);
                    return total;
                }

                public static double sumBy<T>(this IEnumerable<T> source, Func<T, double> selector)
                {
                    double total = 0d;
                    foreach (T item in source) total += selector(item);
                    return total;
                }

                public static decimal sumBy<T>(this IEnumerable<T> source, Func<T, decimal> selector)
                {
                    decimal total = 0m;
                    foreach (T item in source) total += selector(item);
                    return total;
                }

                public static T min<T>(T left, T right) => Comparer<T>.Default.Compare(left, right) <= 0 ? left : right;
                public static T max<T>(T left, T right) => Comparer<T>.Default.Compare(left, right) >= 0 ? left : right;

                public static T min<T>(this IEnumerable<T> source)
                {
                    using IEnumerator<T> enumerator = source.GetEnumerator();
                    if (!enumerator.MoveNext()) throw new InvalidOperationException("Sequence contains no elements.");
                    T best = enumerator.Current;
                    while (enumerator.MoveNext())
                    {
                        if (Comparer<T>.Default.Compare(enumerator.Current, best) < 0)
                        {
                            best = enumerator.Current;
                        }
                    }

                    return best;
                }

                public static T max<T>(this IEnumerable<T> source)
                {
                    using IEnumerator<T> enumerator = source.GetEnumerator();
                    if (!enumerator.MoveNext()) throw new InvalidOperationException("Sequence contains no elements.");
                    T best = enumerator.Current;
                    while (enumerator.MoveNext())
                    {
                        if (Comparer<T>.Default.Compare(enumerator.Current, best) > 0)
                        {
                            best = enumerator.Current;
                        }
                    }

                    return best;
                }

                public static T minBy<T, TKey>(this IEnumerable<T> source, Func<T, TKey> selector)
                {
                    using IEnumerator<T> enumerator = source.GetEnumerator();
                    if (!enumerator.MoveNext()) throw new InvalidOperationException("Sequence contains no elements.");
                    T bestItem = enumerator.Current;
                    TKey bestKey = selector(bestItem);
                    while (enumerator.MoveNext())
                    {
                        T item = enumerator.Current;
                        TKey key = selector(item);
                        if (Comparer<TKey>.Default.Compare(key, bestKey) < 0)
                        {
                            bestItem = item;
                            bestKey = key;
                        }
                    }

                    return bestItem;
                }

                public static T maxBy<T, TKey>(this IEnumerable<T> source, Func<T, TKey> selector)
                {
                    using IEnumerator<T> enumerator = source.GetEnumerator();
                    if (!enumerator.MoveNext()) throw new InvalidOperationException("Sequence contains no elements.");
                    T bestItem = enumerator.Current;
                    TKey bestKey = selector(bestItem);
                    while (enumerator.MoveNext())
                    {
                        T item = enumerator.Current;
                        TKey key = selector(item);
                        if (Comparer<TKey>.Default.Compare(key, bestKey) > 0)
                        {
                            bestItem = item;
                            bestKey = key;
                        }
                    }

                    return bestItem;
                }

                public static int count<T>(this IEnumerable<T> source, Func<T, bool> predicate)
                {
                    int total = 0;
                    foreach (T item in source)
                    {
                        if (predicate(item)) total++;
                    }

                    return total;
                }

                public static bool any<T>(this IEnumerable<T> source, Func<T, bool> predicate)
                {
                    foreach (T item in source)
                    {
                        if (predicate(item)) return true;
                    }

                    return false;
                }

                public static bool all<T>(this IEnumerable<T> source, Func<T, bool> predicate)
                {
                    foreach (T item in source)
                    {
                        if (!predicate(item)) return false;
                    }

                    return true;
                }

                public static T? find<T>(this IEnumerable<T> source, Func<T, bool> predicate)
                {
                    foreach (T item in source)
                    {
                        if (predicate(item)) return item;
                    }

                    return default;
                }

                public static int findIndex<T>(this IEnumerable<T> source, Func<T, bool> predicate)
                {
                    int index = 0;
                    foreach (T item in source)
                    {
                        if (predicate(item)) return index;
                        index++;
                    }

                    return -1;
                }

                public static int findLastIndex<T>(this IEnumerable<T> source, Func<T, bool> predicate)
                {
                    int found = -1;
                    int index = 0;
                    foreach (T item in source)
                    {
                        if (predicate(item)) found = index;
                        index++;
                    }

                    return found;
                }

                public static T[] sort<T>(this IEnumerable<T> source)
                {
                    List<T> list = source.ToList();
                    list.Sort();
                    return list.ToArray();
                }

                public static T[] sortBy<T, TKey>(this IEnumerable<T> source, Func<T, TKey> selector)
                {
                    List<T> list = source.ToList();
                    list.Sort((left, right) => Comparer<TKey>.Default.Compare(selector(left), selector(right)));
                    return list.ToArray();
                }

                public static T[] sortWith<T>(this IEnumerable<T> source, Func<T, T, int> comparer)
                {
                    List<T> list = source.ToList();
                    list.Sort((left, right) => comparer(left, right));
                    return list.ToArray();
                }

                public static T[] sortWith<T>(this IEnumerable<T> source, Comparison<T> comparer)
                {
                    List<T> list = source.ToList();
                    list.Sort(comparer);
                    return list.ToArray();
                }

                public static T[] sortWith<T>(this IEnumerable<T> source, IComparer<T> comparer)
                {
                    List<T> list = source.ToList();
                    list.Sort(comparer);
                    return list.ToArray();
                }

                public static bool chmin<T>(ref T target, T value)
                {
                    if (Comparer<T>.Default.Compare(value, target) < 0)
                    {
                        target = value;
                        return true;
                    }

                    return false;
                }

                public static bool chmax<T>(ref T target, T value)
                {
                    if (Comparer<T>.Default.Compare(value, target) > 0)
                    {
                        target = value;
                        return true;
                    }

                    return false;
                }

                public static T[] distinct<T>(this IEnumerable<T> source) => source.Distinct().ToArray();
                public static T[] reverse<T>(this IEnumerable<T> source) => source.Reverse().ToArray();
                public static T[] copy<T>(this IEnumerable<T> source) => source.ToArray();

                public static Dictionary<T, int> groupCount<T>(this IEnumerable<T> source) where T : notnull
                {
                    Dictionary<T, int> result = new();
                    foreach (T item in source)
                    {
                        result[item] = result.TryGetValue(item, out int count) ? count + 1 : 1;
                    }

                    return result;
                }

                public static Dictionary<T, int> freq<T>(this IEnumerable<T> source) where T : notnull
                    => source.groupCount();

                public static Dictionary<T, int> index<T>(this IEnumerable<T> source) where T : notnull
                {
                    Dictionary<T, int> result = new();
                    int index = 0;
                    foreach (T item in source)
                    {
                        if (!result.ContainsKey(item)) result[item] = index;
                        index++;
                    }

                    return result;
                }
            }
            """);
    }

    private void EmitStdinHelpers(string programText, bool verbose)
    {
        static bool Contains(string text, string value)
            => text.Contains(value, StringComparison.Ordinal);

        bool needInt = verbose || Contains(programText, "stdin.@int(") || Contains(programText, "stdin.arrayInt(") || Contains(programText, "stdin.gridInt(");
        bool needLong = verbose || Contains(programText, "stdin.@long(") || Contains(programText, "stdin.arrayLong(") || Contains(programText, "stdin.gridLong(");
        bool needDouble = verbose || Contains(programText, "stdin.@double(") || Contains(programText, "stdin.arrayDouble(");
        bool needDecimal = verbose || Contains(programText, "stdin.@decimal(") || Contains(programText, "stdin.arrayDecimal(");
        bool needBool = verbose || Contains(programText, "stdin.@bool(") || Contains(programText, "stdin.arrayBool(");
        bool needChar = verbose || Contains(programText, "stdin.@char(") || Contains(programText, "stdin.arrayChar(");
        bool needString = verbose || Contains(programText, "stdin.str(") || Contains(programText, "stdin.arrayString(");
        bool needLine = verbose || Contains(programText, "stdin.line(") || Contains(programText, "stdin.lines(") || Contains(programText, "stdin.words(") || Contains(programText, "stdin.chars(") || Contains(programText, "stdin.charGrid(") || Contains(programText, "stdin.wordGrid(");
        bool needLines = verbose || Contains(programText, "stdin.lines(");
        bool needWords = verbose || Contains(programText, "stdin.words(") || Contains(programText, "stdin.wordGrid(");
        bool needChars = verbose || Contains(programText, "stdin.chars(") || Contains(programText, "stdin.charGrid(");
        bool needArrayInt = verbose || Contains(programText, "stdin.arrayInt(");
        bool needArrayLong = verbose || Contains(programText, "stdin.arrayLong(");
        bool needArrayDouble = verbose || Contains(programText, "stdin.arrayDouble(");
        bool needArrayDecimal = verbose || Contains(programText, "stdin.arrayDecimal(");
        bool needArrayBool = verbose || Contains(programText, "stdin.arrayBool(");
        bool needArrayChar = verbose || Contains(programText, "stdin.arrayChar(");
        bool needArrayString = verbose || Contains(programText, "stdin.arrayString(");
        bool needGenericRead = verbose || Contains(programText, "stdin.read<");
        bool needGenericArray = verbose || Contains(programText, "stdin.array<");
        bool needList = verbose || Contains(programText, "stdin.list<");
        bool needLinkedList = verbose || Contains(programText, "stdin.linkedList<");
        bool needTuple2 = verbose || Contains(programText, "stdin.tuple2<");
        bool needTuple3 = verbose || Contains(programText, "stdin.tuple3<");
        bool needTuples2 = verbose || Contains(programText, "stdin.tuples2<");
        bool needTuples3 = verbose || Contains(programText, "stdin.tuples3<");
        bool needNestedArray = verbose || Contains(programText, "stdin.nestedArray<");
        bool needGridInt = verbose || Contains(programText, "stdin.gridInt(");
        bool needGridLong = verbose || Contains(programText, "stdin.gridLong(");
        bool needCharGrid = verbose || Contains(programText, "stdin.charGrid(");
        bool needWordGrid = verbose || Contains(programText, "stdin.wordGrid(");

        needArrayInt |= needGridInt;
        needArrayLong |= needGridLong;

        needGenericArray |= needList || needLinkedList || needNestedArray;
        needGenericRead |= needGenericArray || needTuple2 || needTuple3 || needTuples2 || needTuples3;
        needInt |= needGenericRead;
        needLong |= needGenericRead;
        needDouble |= needGenericRead;
        needDecimal |= needGenericRead;
        needBool |= needGenericRead;
        needChar |= needGenericRead;
        needString |= needGenericRead;

        bool needNextToken = verbose || needDouble || needDecimal || needBool || needChar || needString || needGenericRead;
        bool needReadInt = verbose || needInt || needArrayInt || needGridInt;
        bool needReadLong = verbose || needLong || needArrayLong || needGridLong;
        bool needParseBool = verbose || needBool || needGenericRead;

        System.Text.StringBuilder builder = new();
        builder.AppendLine("public sealed class __PscpStdin");
        builder.AppendLine("{");
        builder.AppendLine("    private readonly StreamReader _reader = new(Console.OpenStandardInput(), Encoding.UTF8, false, 1 << 16);");
        builder.AppendLine();

        if (needInt) builder.AppendLine("    public int @int() => ReadInt();");
        if (needLong) builder.AppendLine("    public long @long() => ReadLong();");
        if (needDouble) builder.AppendLine("    public double @double() => double.Parse(NextToken(), CultureInfo.InvariantCulture);");
        if (needDecimal) builder.AppendLine("    public decimal @decimal() => decimal.Parse(NextToken(), CultureInfo.InvariantCulture);");
        if (needBool) builder.AppendLine("    public bool @bool() => ParseBool(NextToken());");
        if (needChar)
        {
            builder.AppendLine(
                """
                    public char @char()
                    {
                        string token = NextToken();
                        return token.Length == 0 ? '\0' : token[0];
                    }
                """);
        }

        if (needString) builder.AppendLine("    public string str() => NextToken();");
        if (needLine) builder.AppendLine("    public string line() => _reader.ReadLine() ?? string.Empty;");
        if (needLines)
        {
            builder.AppendLine(
                """
                    public string[] lines(int n)
                    {
                        string[] result = new string[n];
                        for (int i = 0; i < n; i++) result[i] = line();
                        return result;
                    }
                """);
        }

        if (needWords) builder.AppendLine("    public string[] words() => line().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);");
        if (needChars) builder.AppendLine("    public char[] chars() => line().ToCharArray();");

        if (needArrayInt)
        {
            builder.AppendLine(
                """
                    public int[] arrayInt(int n)
                    {
                        int[] result = new int[n];
                        for (int i = 0; i < n; i++) result[i] = @int();
                        return result;
                    }
                """);
        }

        if (needArrayLong)
        {
            builder.AppendLine(
                """
                    public long[] arrayLong(int n)
                    {
                        long[] result = new long[n];
                        for (int i = 0; i < n; i++) result[i] = @long();
                        return result;
                    }
                """);
        }

        if (needArrayDouble)
        {
            builder.AppendLine(
                """
                    public double[] arrayDouble(int n)
                    {
                        double[] result = new double[n];
                        for (int i = 0; i < n; i++) result[i] = @double();
                        return result;
                    }
                """);
        }

        if (needArrayDecimal)
        {
            builder.AppendLine(
                """
                    public decimal[] arrayDecimal(int n)
                    {
                        decimal[] result = new decimal[n];
                        for (int i = 0; i < n; i++) result[i] = @decimal();
                        return result;
                    }
                """);
        }

        if (needArrayBool)
        {
            builder.AppendLine(
                """
                    public bool[] arrayBool(int n)
                    {
                        bool[] result = new bool[n];
                        for (int i = 0; i < n; i++) result[i] = @bool();
                        return result;
                    }
                """);
        }

        if (needArrayChar)
        {
            builder.AppendLine(
                """
                    public char[] arrayChar(int n)
                    {
                        char[] result = new char[n];
                        for (int i = 0; i < n; i++) result[i] = @char();
                        return result;
                    }
                """);
        }

        if (needArrayString)
        {
            builder.AppendLine(
                """
                    public string[] arrayString(int n)
                    {
                        string[] result = new string[n];
                        for (int i = 0; i < n; i++) result[i] = str();
                        return result;
                    }
                """);
        }

        if (needGenericRead)
        {
            builder.AppendLine(
                """
                    public T read<T>()
                    {
                        if (typeof(T) == typeof(int)) return (T)(object)@int();
                        if (typeof(T) == typeof(long)) return (T)(object)@long();
                        if (typeof(T) == typeof(double)) return (T)(object)@double();
                        if (typeof(T) == typeof(decimal)) return (T)(object)@decimal();
                        if (typeof(T) == typeof(bool)) return (T)(object)@bool();
                        if (typeof(T) == typeof(char)) return (T)(object)@char();
                        if (typeof(T) == typeof(string)) return (T)(object)str();
                        throw new InvalidOperationException($"Unsupported stdin.read<{typeof(T).Name}>() call.");
                    }
                """);
        }

        if (needGenericArray)
        {
            builder.AppendLine(
                """
                    public T[] array<T>(int n)
                    {
                        if (typeof(T) == typeof(int)) return (T[])(object)arrayInt(n);
                        if (typeof(T) == typeof(long)) return (T[])(object)arrayLong(n);
                        if (typeof(T) == typeof(double)) return (T[])(object)arrayDouble(n);
                        if (typeof(T) == typeof(decimal)) return (T[])(object)arrayDecimal(n);
                        if (typeof(T) == typeof(bool)) return (T[])(object)arrayBool(n);
                        if (typeof(T) == typeof(char)) return (T[])(object)arrayChar(n);
                        if (typeof(T) == typeof(string)) return (T[])(object)arrayString(n);

                        T[] result = new T[n];
                        for (int i = 0; i < n; i++) result[i] = read<T>();
                        return result;
                    }
                """);
        }

        if (needList) builder.AppendLine("    public List<T> list<T>(int n) => new(array<T>(n));");
        if (needLinkedList) builder.AppendLine("    public LinkedList<T> linkedList<T>(int n) => new(array<T>(n));");
        if (needTuple2) builder.AppendLine("    public (T1, T2) tuple2<T1, T2>() => (read<T1>(), read<T2>());");
        if (needTuple3) builder.AppendLine("    public (T1, T2, T3) tuple3<T1, T2, T3>() => (read<T1>(), read<T2>(), read<T3>());");

        if (needTuples2)
        {
            builder.AppendLine(
                """
                    public (T1, T2)[] tuples2<T1, T2>(int n)
                    {
                        (T1, T2)[] result = new (T1, T2)[n];
                        for (int i = 0; i < n; i++) result[i] = tuple2<T1, T2>();
                        return result;
                    }
                """);
        }

        if (needTuples3)
        {
            builder.AppendLine(
                """
                    public (T1, T2, T3)[] tuples3<T1, T2, T3>(int n)
                    {
                        (T1, T2, T3)[] result = new (T1, T2, T3)[n];
                        for (int i = 0; i < n; i++) result[i] = tuple3<T1, T2, T3>();
                        return result;
                    }
                """);
        }

        if (needNestedArray)
        {
            builder.AppendLine(
                """
                    public T[][] nestedArray<T>(int n, int m)
                    {
                        T[][] result = new T[n][];
                        for (int i = 0; i < n; i++) result[i] = array<T>(m);
                        return result;
                    }
                """);
        }

        if (needGridInt)
        {
            builder.AppendLine(
                """
                    public int[][] gridInt(int n, int m)
                    {
                        int[][] result = new int[n][];
                        for (int i = 0; i < n; i++) result[i] = arrayInt(m);
                        return result;
                    }
                """);
        }

        if (needGridLong)
        {
            builder.AppendLine(
                """
                    public long[][] gridLong(int n, int m)
                    {
                        long[][] result = new long[n][];
                        for (int i = 0; i < n; i++) result[i] = arrayLong(m);
                        return result;
                    }
                """);
        }

        if (needCharGrid)
        {
            builder.AppendLine(
                """
                    public char[][] charGrid(int n)
                    {
                        char[][] result = new char[n][];
                        for (int i = 0; i < n; i++) result[i] = line().ToCharArray();
                        return result;
                    }
                """);
        }

        if (needWordGrid)
        {
            builder.AppendLine(
                """
                    public string[][] wordGrid(int n)
                    {
                        string[][] result = new string[n][];
                        for (int i = 0; i < n; i++) result[i] = words();
                        return result;
                    }
                """);
        }

        if (needNextToken)
        {
            builder.AppendLine(
                """
                    private string NextToken()
                    {
                        int next;
                        while ((next = _reader.Peek()) >= 0 && char.IsWhiteSpace((char)next))
                        {
                            _reader.Read();
                        }

                        if (next < 0)
                        {
                            throw new EndOfStreamException("Unexpected end of input.");
                        }

                        StringBuilder token = new();
                        while ((next = _reader.Peek()) >= 0 && !char.IsWhiteSpace((char)next))
                        {
                            token.Append((char)_reader.Read());
                        }

                        return token.ToString();
                    }
                """);
        }

        if (needReadInt)
        {
            builder.AppendLine(
                """
                    private int ReadInt()
                    {
                        int next;
                        while ((next = _reader.Peek()) >= 0 && char.IsWhiteSpace((char)next))
                        {
                            _reader.Read();
                        }

                        if (next < 0)
                        {
                            throw new EndOfStreamException("Unexpected end of input.");
                        }

                        int sign = 1;
                        if (next == '-')
                        {
                            sign = -1;
                            _reader.Read();
                        }

                        int value = 0;
                        bool hasDigit = false;
                        while ((next = _reader.Peek()) >= 0 && char.IsDigit((char)next))
                        {
                            hasDigit = true;
                            value = (value * 10) + (_reader.Read() - '0');
                        }

                        if (!hasDigit)
                        {
                            throw new FormatException("Expected an integer token.");
                        }

                        if ((next = _reader.Peek()) >= 0 && !char.IsWhiteSpace((char)next))
                        {
                            throw new FormatException("Invalid integer token.");
                        }

                        return sign < 0 ? -value : value;
                    }
                """);
        }

        if (needReadLong)
        {
            builder.AppendLine(
                """
                    private long ReadLong()
                    {
                        int next;
                        while ((next = _reader.Peek()) >= 0 && char.IsWhiteSpace((char)next))
                        {
                            _reader.Read();
                        }

                        if (next < 0)
                        {
                            throw new EndOfStreamException("Unexpected end of input.");
                        }

                        long sign = 1L;
                        if (next == '-')
                        {
                            sign = -1L;
                            _reader.Read();
                        }

                        long value = 0L;
                        bool hasDigit = false;
                        while ((next = _reader.Peek()) >= 0 && char.IsDigit((char)next))
                        {
                            hasDigit = true;
                            value = (value * 10L) + (_reader.Read() - '0');
                        }

                        if (!hasDigit)
                        {
                            throw new FormatException("Expected a long integer token.");
                        }

                        if ((next = _reader.Peek()) >= 0 && !char.IsWhiteSpace((char)next))
                        {
                            throw new FormatException("Invalid long integer token.");
                        }

                        return sign < 0 ? -value : value;
                    }
                """);
        }

        if (needParseBool)
        {
            builder.AppendLine(
                """
                    private static bool ParseBool(string token)
                    {
                        if (string.Equals(token, "true", StringComparison.OrdinalIgnoreCase)) return true;
                        if (string.Equals(token, "false", StringComparison.OrdinalIgnoreCase)) return false;
                        if (token == "1") return true;
                        if (token == "0") return false;
                        return !string.IsNullOrEmpty(token);
                    }
                """);
        }

        builder.AppendLine("}");
        WriteRuntimeBlock(builder.ToString());
    }

    private void EmitStdoutHelpers(string programText, bool verbose)
    {
        if (!verbose && !_stdoutNeedsFallbackHelpers)
        {
            EmitDirectStdoutHelpers();
            return;
        }

        WriteRuntimeBlock(
            """
            public sealed class __PscpStdout
            {
                private readonly StreamWriter _writer = new(Console.OpenStandardOutput(), new UTF8Encoding(false), 1 << 16) { AutoFlush = false };

                public void flush() => _writer.Flush();

                public void write(int value) => _writer.Write(value.ToString(CultureInfo.InvariantCulture));
                public void write(long value) => _writer.Write(value.ToString(CultureInfo.InvariantCulture));
                public void write(double value) => _writer.Write(value.ToString(CultureInfo.InvariantCulture));
                public void write(decimal value) => _writer.Write(value.ToString(CultureInfo.InvariantCulture));
                public void write(bool value) => _writer.Write(value ? "True" : "False");
                public void write(char value) => _writer.Write(value);
                public void write(string? value) => _writer.Write(value ?? string.Empty);

                public void write<T1, T2>((T1, T2) value)
                {
                    WriteValue(value.Item1);
                    _writer.Write(' ');
                    WriteValue(value.Item2);
                }

                public void write<T1, T2, T3>((T1, T2, T3) value)
                {
                    WriteValue(value.Item1);
                    _writer.Write(' ');
                    WriteValue(value.Item2);
                    _writer.Write(' ');
                    WriteValue(value.Item3);
                }

                public void write(int[] values) => WriteJoined(values);
                public void write(long[] values) => WriteJoined(values);
                public void write(double[] values) => WriteJoined(values);
                public void write(decimal[] values) => WriteJoined(values);
                public void write(bool[] values) => WriteJoined(values);
                public void write(char[] values) => WriteJoined(values);
                public void write(string[] values) => WriteJoined(values);
                public void write<T>(T[] values) => WriteJoined(values);
                public void write<T>(IEnumerable<T> values) => WriteJoined(values);
                public void write<T>(T value) => WriteValue(value);

                public void writeln()
                    => _writer.WriteLine();

                public void writeln(int value)
                {
                    write(value);
                    _writer.WriteLine();
                }

                public void writeln(long value)
                {
                    write(value);
                    _writer.WriteLine();
                }

                public void writeln(double value)
                {
                    write(value);
                    _writer.WriteLine();
                }

                public void writeln(decimal value)
                {
                    write(value);
                    _writer.WriteLine();
                }

                public void writeln(bool value)
                {
                    write(value);
                    _writer.WriteLine();
                }

                public void writeln(char value)
                {
                    write(value);
                    _writer.WriteLine();
                }

                public void writeln(string? value)
                {
                    write(value);
                    _writer.WriteLine();
                }

                public void writeln<T1, T2>((T1, T2) value)
                {
                    write(value);
                    _writer.WriteLine();
                }

                public void writeln<T1, T2, T3>((T1, T2, T3) value)
                {
                    write(value);
                    _writer.WriteLine();
                }

                public void writeln(int[] values)
                {
                    write(values);
                    _writer.WriteLine();
                }

                public void writeln(long[] values)
                {
                    write(values);
                    _writer.WriteLine();
                }

                public void writeln(double[] values)
                {
                    write(values);
                    _writer.WriteLine();
                }

                public void writeln(decimal[] values)
                {
                    write(values);
                    _writer.WriteLine();
                }

                public void writeln(bool[] values)
                {
                    write(values);
                    _writer.WriteLine();
                }

                public void writeln(char[] values)
                {
                    write(values);
                    _writer.WriteLine();
                }

                public void writeln(string[] values)
                {
                    write(values);
                    _writer.WriteLine();
                }

                public void writeln<T>(T[] values)
                {
                    write(values);
                    _writer.WriteLine();
                }

                public void writeln<T>(IEnumerable<T> values)
                {
                    write(values);
                    _writer.WriteLine();
                }

                public void writeln<T>(T value)
                {
                    write(value);
                    _writer.WriteLine();
                }

                public void lines<T>(IEnumerable<T> values)
                {
                    foreach (T value in values)
                    {
                        writeln(value);
                    }
                }

                public void grid<T>(IEnumerable<IEnumerable<T>> grid)
                {
                    foreach (IEnumerable<T> row in grid)
                    {
                        writeln(row);
                    }
                }

                public void join<T>(string separator, IEnumerable<T> values)
                {
                    bool first = true;
                    foreach (T value in values)
                    {
                        if (!first) _writer.Write(separator);
                        first = false;
                        WriteValue(value);
                    }
                }

                private void WriteJoined<T>(IEnumerable<T> values)
                {
                    bool first = true;
                    foreach (T value in values)
                    {
                        if (!first) _writer.Write(' ');
                        first = false;
                        WriteValue(value);
                    }
                }

                private void WriteValue(int value) => _writer.Write(value.ToString(CultureInfo.InvariantCulture));
                private void WriteValue(long value) => _writer.Write(value.ToString(CultureInfo.InvariantCulture));
                private void WriteValue(double value) => _writer.Write(value.ToString(CultureInfo.InvariantCulture));
                private void WriteValue(decimal value) => _writer.Write(value.ToString(CultureInfo.InvariantCulture));
                private void WriteValue(bool value) => _writer.Write(value ? "True" : "False");
                private void WriteValue(char value) => _writer.Write(value);
                private void WriteValue(string? value) => _writer.Write(value ?? string.Empty);

                private void WriteValue<T1, T2>((T1, T2) value)
                {
                    WriteValue(value.Item1);
                    _writer.Write(' ');
                    WriteValue(value.Item2);
                }

                private void WriteValue<T1, T2, T3>((T1, T2, T3) value)
                {
                    WriteValue(value.Item1);
                    _writer.Write(' ');
                    WriteValue(value.Item2);
                    _writer.Write(' ');
                    WriteValue(value.Item3);
                }

                private void WriteValue<T>(T value)
                    => _writer.Write(__PscpRender.format(value));
            }
            """);
    }

    private void EmitDirectStdoutHelpers()
    {
        System.Text.StringBuilder builder = new();
        builder.AppendLine("public sealed class __PscpStdout");
        builder.AppendLine("{");
        builder.AppendLine("    private readonly StreamWriter _writer = new(Console.OpenStandardOutput(), new UTF8Encoding(false), 1 << 16) { AutoFlush = false };");
        builder.AppendLine();
        builder.AppendLine("    public void flush() => _writer.Flush();");
        builder.AppendLine();

        foreach (string kind in _stdoutDirectScalarKinds.OrderBy(static value => value, StringComparer.Ordinal))
        {
            builder.AppendLine(kind switch
            {
                "int" => "    public void write(int value) => _writer.Write(value.ToString(CultureInfo.InvariantCulture));",
                "long" => "    public void write(long value) => _writer.Write(value.ToString(CultureInfo.InvariantCulture));",
                "double" => "    public void write(double value) => _writer.Write(value.ToString(CultureInfo.InvariantCulture));",
                "decimal" => "    public void write(decimal value) => _writer.Write(value.ToString(CultureInfo.InvariantCulture));",
                "bool" => "    public void write(bool value) => _writer.Write(value ? \"True\" : \"False\");",
                "char" => "    public void write(char value) => _writer.Write(value);",
                "string" => "    public void write(string? value) => _writer.Write(value ?? string.Empty);",
                _ => string.Empty,
            });

            builder.AppendLine(
                kind switch
                {
                    "int" => """
                        public void writeln(int value)
                        {
                            write(value);
                            _writer.WriteLine();
                        }
                        """,
                    "long" => """
                        public void writeln(long value)
                        {
                            write(value);
                            _writer.WriteLine();
                        }
                        """,
                    "double" => """
                        public void writeln(double value)
                        {
                            write(value);
                            _writer.WriteLine();
                        }
                        """,
                    "decimal" => """
                        public void writeln(decimal value)
                        {
                            write(value);
                            _writer.WriteLine();
                        }
                        """,
                    "bool" => """
                        public void writeln(bool value)
                        {
                            write(value);
                            _writer.WriteLine();
                        }
                        """,
                    "char" => """
                        public void writeln(char value)
                        {
                            write(value);
                            _writer.WriteLine();
                        }
                        """,
                    "string" => """
                        public void writeln(string? value)
                        {
                            write(value);
                            _writer.WriteLine();
                        }
                        """,
                    _ => string.Empty,
                });
        }

        if (_stdoutDirectScalarKinds.Count == 0)
        {
            builder.AppendLine("    public void writeln() => _writer.WriteLine();");
        }

        foreach (string kind in _stdoutDirectArrayKinds.OrderBy(static value => value, StringComparer.Ordinal))
        {
            builder.AppendLine(kind switch
            {
                "int" => """
                    public void write(int[] values)
                    {
                        for (int i = 0; i < values.Length; i++)
                        {
                            if (i > 0) _writer.Write(' ');
                            write(values[i]);
                        }
                    }

                    public void writeln(int[] values)
                    {
                        write(values);
                        _writer.WriteLine();
                    }
                    """,
                "long" => """
                    public void write(long[] values)
                    {
                        for (int i = 0; i < values.Length; i++)
                        {
                            if (i > 0) _writer.Write(' ');
                            write(values[i]);
                        }
                    }

                    public void writeln(long[] values)
                    {
                        write(values);
                        _writer.WriteLine();
                    }
                    """,
                "double" => """
                    public void write(double[] values)
                    {
                        for (int i = 0; i < values.Length; i++)
                        {
                            if (i > 0) _writer.Write(' ');
                            write(values[i]);
                        }
                    }

                    public void writeln(double[] values)
                    {
                        write(values);
                        _writer.WriteLine();
                    }
                    """,
                "decimal" => """
                    public void write(decimal[] values)
                    {
                        for (int i = 0; i < values.Length; i++)
                        {
                            if (i > 0) _writer.Write(' ');
                            write(values[i]);
                        }
                    }

                    public void writeln(decimal[] values)
                    {
                        write(values);
                        _writer.WriteLine();
                    }
                    """,
                "bool" => """
                    public void write(bool[] values)
                    {
                        for (int i = 0; i < values.Length; i++)
                        {
                            if (i > 0) _writer.Write(' ');
                            write(values[i]);
                        }
                    }

                    public void writeln(bool[] values)
                    {
                        write(values);
                        _writer.WriteLine();
                    }
                    """,
                "char" => """
                    public void write(char[] values)
                    {
                        for (int i = 0; i < values.Length; i++)
                        {
                            if (i > 0) _writer.Write(' ');
                            write(values[i]);
                        }
                    }

                    public void writeln(char[] values)
                    {
                        write(values);
                        _writer.WriteLine();
                    }
                    """,
                "string" => """
                    public void write(string[] values)
                    {
                        for (int i = 0; i < values.Length; i++)
                        {
                            if (i > 0) _writer.Write(' ');
                            write(values[i]);
                        }
                    }

                    public void writeln(string[] values)
                    {
                        write(values);
                        _writer.WriteLine();
                    }
                    """,
                _ => string.Empty,
            });
        }

        builder.AppendLine("}");
        WriteRuntimeBlock(builder.ToString());
    }

    private void EmitRenderHelpers()
    {
        WriteRuntimeBlock(
            """
            public static class __PscpRender
            {
                public static string format<T>(T value)
                {
                    return value switch
                    {
                        null => string.Empty,
                        string text => text,
                        bool boolean => boolean ? "True" : "False",
                        char ch => ch.ToString(),
                        System.Runtime.CompilerServices.ITuple tuple => FormatTuple(tuple),
                        System.Collections.IEnumerable enumerable when value is not string => FormatEnumerable(enumerable),
                        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
                        _ => value?.ToString() ?? string.Empty,
                    };
                }

                private static string FormatTuple(System.Runtime.CompilerServices.ITuple tuple)
                {
                    StringBuilder builder = new();
                    for (int i = 0; i < tuple.Length; i++)
                    {
                        if (i > 0) builder.Append(' ');
                        builder.Append(format(tuple[i]));
                    }

                    return builder.ToString();
                }

                private static string FormatEnumerable(System.Collections.IEnumerable enumerable)
                {
                    StringBuilder builder = new();
                    bool first = true;
                    foreach (object? item in enumerable)
                    {
                        if (!first) builder.Append(' ');
                        first = false;
                        builder.Append(format(item));
                    }

                    return builder.ToString();
                }
            }
            """);
    }

    private void WriteRuntimeBlock(string text)
    {
        foreach (string line in text.Replace("\r", string.Empty).Split('\n'))
        {
            _writer.WriteLine(line);
        }
    }

    private static bool NeedsSequenceHelpers(string programText)
        => programText.Contains("__PscpSeq.", StringComparison.Ordinal)
            || programText.Contains(".map(", StringComparison.Ordinal)
            || programText.Contains(".filter(", StringComparison.Ordinal)
            || programText.Contains(".fold(", StringComparison.Ordinal)
            || programText.Contains(".scan(", StringComparison.Ordinal)
            || programText.Contains(".mapFold(", StringComparison.Ordinal)
            || programText.Contains(".sort(", StringComparison.Ordinal)
            || programText.Contains(".sortBy(", StringComparison.Ordinal)
            || programText.Contains(".sortWith(", StringComparison.Ordinal)
            || programText.Contains(".distinct(", StringComparison.Ordinal)
            || programText.Contains(".reverse(", StringComparison.Ordinal)
            || programText.Contains(".copy(", StringComparison.Ordinal)
            || programText.Contains(".groupCount(", StringComparison.Ordinal)
            || programText.Contains(".freq(", StringComparison.Ordinal)
            || programText.Contains(".index(", StringComparison.Ordinal);
}
