using System.Diagnostics;
using System.Text;
using Pscp.Transpiler;

internal sealed record TestCase(string Name, string Source, string Input, string ExpectedOutput);

internal static class Program
{
    public static Task<int> Main()
        => TestRunner.RunAsync();
}

internal static class TestRunner
{
    public static async Task<int> RunAsync()
    {
        string workspaceRoot = FindWorkspaceRoot();
        string generatedRoot = Path.Combine(workspaceRoot, ".generated-tests");
        Directory.CreateDirectory(generatedRoot);

        List<TestCase> testCases =
        [
            new(
                "BasicInputOutput",
                """
                int n =
                int m =
                += n + m
                """,
                "2 3",
                "5\n"),
            new(
                "RecursionAndSpaceCall",
                """
                rec int fact(int n) {
                    if n <= 1 then 1 else n * fact(n - 1)
                }

                int n =
                += fact n
                """,
                "5",
                "120\n"),
            new(
                "RangeSpreadAndCollection",
                """
                int n =
                int[] parent = [0..<n]
                let extra = [100, ..parent, 200]
                += extra
                """,
                "4",
                "100 0 1 2 3 200\n"),
            new(
                "TupleAndSwap",
                """
                (int, int, int) p = (6, 1, 4)
                mut int a = 1
                mut int b = 2
                (a, b) = (b, a)
                += p.2
                += (a, b)
                """,
                "",
                "1\n2 1\n"),
            new(
                "BuilderAggregationAndFastFor",
                """
                int n =
                let squares = [0..<n -> i do i * i]
                let total = sum { for x in squares do x }
                += total
                """,
                "4",
                "14\n"),
            new(
                "HelpersAndDirectApi",
                """
                int n =
                let arr = stdin.array<int>(n)
                let doubled = arr.map(x => x * 2).filter(x => x > 2)
                += doubled.sum()
                """,
                "4 1 2 3 4",
                "18\n"),
            new(
                "BinaryMinMaxIntrinsic",
                """
                int a = 7
                int b = 3
                += min a b
                += max a b
                """,
                "",
                "3\n7\n"),
            new(
                "ComparatorAndOutVarDecl",
                """
                int n =
                PriorityQueue<int, int> pq = new(int.desc)

                for i in 0..<n {
                    int x =
                    pq.Enqueue(x, x)
                }

                while pq.Count > 0 {
                    pq.TryDequeue(out int x, out _)
                    += x
                }
                """,
                "4 2 5 1 3",
                "5\n3\n2\n1\n"),
            new(
                "RefOutInAndChMinMax",
                """
                bool update(ref int x, out int y, in int z) {
                    x += z
                    y = x
                    true
                }

                mut int x = 5
                mut int y;

                _ = update(ref x, out y, in x)
                _ = chmin(ref x, 3)
                _ = chmax(ref y, 20)
                += (x, y)
                """,
                "",
                "3 20\n"),
            new(
                "RecordStructWithInterface",
                """
                record struct Job(int Id, int Arrival, long Time) : IComparable<Job> {
                    public readonly int CompareTo(Job other) {
                        if Arrival == other.Arrival {
                            return Time <=> other.Time
                        }
                        Arrival <=> other.Arrival
                    }
                }

                Job a = new(0, 1, 5L)
                Job b = new(1, 1, 2L)
                += a.CompareTo(b)
                """,
                "",
                "1\n"),
            new(
                "ClassMembersNamedArgsAndCStyleFor",
                """
                class Counter {
                    List<int> values;

                    Counter(int capacity) {
                        values = new(capacity: capacity)
                    }

                    void add(int x) => values.Add(x)

                    int sum() {
                        var total = 0
                        for (int i = 0; i < values.Count; i++) {
                            total += values[i]
                        }
                        total
                    }
                }

                Counter c = new(4)
                c.add(1)
                c.add(2)
                c.add(3)
                += c.sum()
                """,
                "",
                "6\n"),
            new(
                "LocalFunctionAndBuilderBlock",
                """
                int solve(int n) {
                    int twice(int x) {
                        x * 2
                    }

                    let total = sum [0..<n -> i {
                        let t = twice(i)
                        t + 1
                    }]
                    total
                }

                int n =
                += solve(n)
                """,
                "4",
                "16\n"),
            new(
                "TupleBindingPostfixAndIs",
                """
                (int, int, int) item = (1, 2, 3)
                let (a, _, c) = item
                mut int i = 0
                int[] arr = [10, 20, 30]
                let first = arr[i++]
                if first is 10 {
                    += (a, c, i)
                }
                """,
                "",
                "1 3 1\n"),
            new(
                "ConversionKeywords",
                """
                let a = int "123"
                let b = int true
                let c = bool "hello"
                let d = bool 0
                let e = string 123
                += a + b
                += c
                += d
                += e
                """,
                "",
                "124\nTrue\nFalse\n123\n"),
            new(
                "GeneratorAndValueAssignment",
                """
                int[] parent = [0, 1, 2, 3]
                let assigned = parent[1] := 7
                parent[3] = assigned
                let total = sum (0..<4 -> i do parent[i])
                += assigned
                += total
                """,
                "",
                "7\n16\n"),
            new(
                "ForGeneratorAndSlice",
                """
                string text = "abcdef"
                int[] arr = [10, 20, 30, 40, 50]
                let total = sum (for i in 0..<arr.Length do arr[i])
                += total
                += text[1..^1]
                += text[^1]
                += arr[..^2]
                """,
                "",
                "150\nbcde\nf\n10 20 30\n"),
            new(
                "TargetTypedNewBangAndListAddAssign",
                """
                int n = 4
                List<int>[] graph = new![n]
                graph[0] += 1
                graph[0] += 2
                graph[2] += 3
                += graph[0]
                += graph[1].Count
                += graph[2]
                """,
                "",
                "1 2\n0\n3\n"),
            new(
                "KnownDataStructureOperators",
                """
                HashSet<int> seen = new()
                Queue<int> q = new()
                Stack<int> s = new()
                PriorityQueue<int, int> pq = new(int.desc)

                let firstSeen = seen += 3
                let secondSeen = seen += 3

                q += 10
                q += 20
                s += 7
                s += 8
                pq += (5, 5)
                pq += (1, 1)

                += (firstSeen, secondSeen)
                += (~q, --q, ~s, --s)
                += (~pq, --pq)
                """,
                "",
                "True False\n10 10 8 8\n5 5\n"),
            new(
                "OrderingShorthandAndSectionLabels",
                """
                record struct Job(int Id, int Arrival, long Time) {
                    operator<=>(other) =>
                        if Arrival == other.Arrival then Time <=> other.Time
                        else Arrival <=> other.Arrival
                }

                class Runner {
                private:
                    int secret() => 5

                public:
                    int solve() => secret()
                }

                Job[] jobs = [new(0, 1, 5L), new(1, 0, 3L), new(2, 1, 2L)]
                let ordered = jobs.sortWith(Job.asc)
                Runner runner = new()
                += ordered[0].Id
                += ordered[1].Id
                += runner.solve()
                """,
                "",
                "1\n2\n5\n"),
            new(
                "InterpolatedStringAndArrayZero",
                """
                int n = 4
                int[] values = Array.zero(n)
                for i in 0..<n do values[i] = i + 1
                let msg = $"sum = {sum values}"
                += msg
                """,
                "",
                "sum = 10\n"),
            new(
                "CoordinateCompressionHelpers",
                """
                int n =
                int[n] xs =

                let sorted = xs.sort().distinct()
                let index = sorted.index()
                let compressed = [xs -> x do index[x]]

                += compressed
                """,
                "6 50 10 50 20 10 30",
                "3 0 3 1 0 2\n"),
            new(
                "ThenStatementFormsAndMathPassThrough",
                """
                bool positive(int x) {
                    if x > 0 then return true
                    false
                }

                int x = 9
                mut int y = 0
                if positive(x) then
                    y = int Math.Sqrt(x)
                += y
                """,
                "",
                "3\n"),
            new(
                "ArraySortPassThrough",
                """
                int[] values = [3, 1, 2]
                Array.Sort(values)
                += values
                """,
                "",
                "1 2 3\n"),
            new(
                "AggregateHelperFamily",
                """
                int[] arr = [1, 2, 3, 4, 2]
                += count(arr, x => x % 2 == 0)
                += any(arr, x => x > 3)
                += all(arr, x => x > 0)
                += find(arr, x => x > 3)
                += findIndex(arr, x => x == 3)
                += findLastIndex(arr, x => x == 2)
                """,
                "",
                "3\nTrue\nTrue\n4\n2\n4\n"),
            new(
                "ThisMemberAndReadIntrinsic",
                """
                class Counter {
                    int value;

                    void add(int delta) {
                        this.value += delta
                    }

                    int total() => this.value
                }

                Counter c = new()
                int extra = stdin.read<int>()
                c.add(extra)
                c.add(2)
                += c.total()
                """,
                "5",
                "7\n"),
            new(
                "SumByMinByMaxByHelpers",
                """
                record struct Point(int X, int Y)

                Point[] points = [new(1, 10), new(2, 3), new(3, 7)]
                += sumBy(points, p => p.Y)
                += minBy(points, p => p.Y).X
                += maxBy(points, p => p.Y).X
                """,
                "",
                "20\n2\n1\n"),
            new(
                "NestedArrayReaderAndInterpolation",
                """
                int n =
                int m =
                int[][] grid = stdin.nestedArray<int>(n, m)
                += $"first={grid[0][0]}, last={grid[^1][^1]}"
                """,
                "2 3 1 2 3 4 5 6",
                "first=1, last=6\n"),
        ];

        List<string> failures = [];

        foreach (TestCase testCase in testCases)
        {
            string safeName = string.Concat(testCase.Name.Where(char.IsLetterOrDigit));
            TranspilationResult result = PscpTranspiler.Transpile(
                NormalizeSource(testCase.Source),
                new TranspilationOptions("Pscp.Generated", safeName + "Program"));

            if (result.Diagnostics.Count > 0)
            {
                failures.Add($"{testCase.Name}: transpiler diagnostics\n{FormatDiagnostics(result.Diagnostics)}\nGenerated:\n{result.CSharpCode}");
                continue;
            }

            string caseDirectory = Path.Combine(generatedRoot, safeName);
            Directory.CreateDirectory(caseDirectory);

            await File.WriteAllTextAsync(Path.Combine(caseDirectory, "Program.cs"), result.CSharpCode, Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(caseDirectory, $"{safeName}.csproj"), CreateProjectFile(), Encoding.UTF8);

            ProcessResult runResult = await RunDotnetAsync(
                "run --project \"" + Path.Combine(caseDirectory, $"{safeName}.csproj") + "\"",
                caseDirectory,
                testCase.Input);

            if (runResult.ExitCode != 0)
            {
                failures.Add($"{testCase.Name}: generated program failed\nSTDOUT:\n{runResult.StdOut}\nSTDERR:\n{runResult.StdErr}\nGenerated:\n{result.CSharpCode}");
                continue;
            }

            string actual = NormalizeOutput(runResult.StdOut);
            string expected = NormalizeOutput(testCase.ExpectedOutput);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                failures.Add($"{testCase.Name}: output mismatch\nExpected: {Escape(expected)}\nActual:   {Escape(actual)}\nGenerated:\n{result.CSharpCode}");
            }
        }

        VerifyBinaryMinMaxLowering(failures);
        VerifyDirectRangeBuilderLowering(failures);
        VerifyDirectGeneratorAggregateLowering(failures);
        VerifyDirectRangeCollectionLowering(failures);
        VerifyDirectFastForLowering(failures);
        VerifyInterpolatedStringLowering(failures);
        VerifyDotNetPassThroughLowering(failures);
        VerifyStructObjectInitializerLowering(failures);
        VerifyImplicitFieldAccessibilityLowering(failures);
        VerifyAutoConstructArrayDeclarationLowering(failures);
        VerifyReverseCompareToLowering(failures);
        VerifyPostfixStatementLowering(failures);
        VerifyRuntimeAvoidsDynamicHelpers(failures);
        VerifySemanticDiagnostics(failures);

        if (failures.Count > 0)
        {
            Console.Error.WriteLine("Smoke tests failed.");
            foreach (string failure in failures)
            {
                Console.Error.WriteLine(new string('-', 80));
                Console.Error.WriteLine(failure);
            }

            return 1;
        }

        Console.WriteLine($"All {testCases.Count} transpiler smoke tests passed.");
        return 0;
    }


    private static void VerifyBinaryMinMaxLowering(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                int a = 7
                int b = 3
                let lo = min a b
                let hi = max a b
                += (lo, hi)
                """),
            new TranspilationOptions("Pscp.Generated", "BinaryMinMaxLoweringProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"BinaryMinMaxLowering: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        if (!result.CSharpCode.Contains("System.Math.Min(a, b)", StringComparison.Ordinal)
            || !result.CSharpCode.Contains("System.Math.Max(a, b)", StringComparison.Ordinal))
        {
            failures.Add($"BinaryMinMaxLowering: expected System.Math lowering not found\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyRuntimeAvoidsDynamicHelpers(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                int[] values = [1, 2, 3]
                += sum values
                += sumBy(values, x => x * x)
                """),
            new TranspilationOptions("Pscp.Generated", "RuntimeAvoidsDynamicHelpersProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"RuntimeAvoidsDynamicHelpers: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        if (result.CSharpCode.Contains("dynamic", StringComparison.Ordinal))
        {
            failures.Add($"RuntimeAvoidsDynamicHelpers: generated runtime still contains dynamic\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyDirectRangeBuilderLowering(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                int n = 4
                int[] squares = [0..<n -> i do i * i]
                += squares
                """),
            new TranspilationOptions("Pscp.Generated", "DirectRangeBuilderLoweringProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"DirectRangeBuilderLowering: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        string userCode = GetUserCodePortion(result.CSharpCode);
        if (!userCode.Contains("new int[", StringComparison.Ordinal)
            || !userCode.Contains("for (int", StringComparison.Ordinal)
            || userCode.Contains("__PscpSeq.rangeInt", StringComparison.Ordinal)
            || userCode.Contains("Enumerable.Select", StringComparison.Ordinal)
            || userCode.Contains("__PscpSeq.expr", StringComparison.Ordinal))
        {
            failures.Add($"DirectRangeBuilderLowering: expected direct array-fill lowering not found\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyDirectGeneratorAggregateLowering(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                int n = 5
                let total = sum (0..<n -> i do i * i)
                += total
                """),
            new TranspilationOptions("Pscp.Generated", "DirectGeneratorAggregateLoweringProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"DirectGeneratorAggregateLowering: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        string userCode = GetUserCodePortion(result.CSharpCode);
        if (!userCode.Contains("for (int", StringComparison.Ordinal)
            || userCode.Contains("__PscpSeq.rangeInt", StringComparison.Ordinal)
            || userCode.Contains("Enumerable.Select", StringComparison.Ordinal)
            || userCode.Contains("Enumerable.Sum", StringComparison.Ordinal))
        {
            failures.Add($"DirectGeneratorAggregateLowering: expected fused loop lowering not found\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyDirectRangeCollectionLowering(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                int n = 4
                int[] values = [0..<n]
                += values
                """),
            new TranspilationOptions("Pscp.Generated", "DirectRangeCollectionLoweringProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"DirectRangeCollectionLowering: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        string userCode = GetUserCodePortion(result.CSharpCode);
        if (!userCode.Contains("new int[", StringComparison.Ordinal)
            || !userCode.Contains("for (int", StringComparison.Ordinal)
            || userCode.Contains("__PscpSeq.rangeInt", StringComparison.Ordinal)
            || userCode.Contains("__PscpSeq.toArray", StringComparison.Ordinal))
        {
            failures.Add($"DirectRangeCollectionLowering: expected direct range materialization not found\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyDirectFastForLowering(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                int n = 4
                0..<n -> i {
                    += i
                }
                """),
            new TranspilationOptions("Pscp.Generated", "DirectFastForLoweringProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"DirectFastForLowering: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        string userCode = GetUserCodePortion(result.CSharpCode);
        if (!userCode.Contains("for (int", StringComparison.Ordinal)
            || userCode.Contains("foreach (var i in", StringComparison.Ordinal)
            || userCode.Contains("__PscpSeq.rangeInt", StringComparison.Ordinal))
        {
            failures.Add($"DirectFastForLowering: expected direct range loop not found\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyInterpolatedStringLowering(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                int x = 3
                += $"value={x}"
                """),
            new TranspilationOptions("Pscp.Generated", "InterpolatedStringLoweringProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"InterpolatedStringLowering: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        string userCode = GetUserCodePortion(result.CSharpCode);
        if (!userCode.Contains("$\"value={x}\"", StringComparison.Ordinal)
            || userCode.Contains("__PscpRender.format", StringComparison.Ordinal))
        {
            failures.Add($"InterpolatedStringLowering: expected direct C# interpolation not found\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyDotNetPassThroughLowering(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                int[] values = [9, 4, 16]
                Array.Sort(values)
                += Math.Sqrt(values[0])
                """),
            new TranspilationOptions("Pscp.Generated", "DotNetPassThroughLoweringProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"DotNetPassThroughLowering: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        string userCode = GetUserCodePortion(result.CSharpCode);
        if (!userCode.Contains("Array.Sort(values);", StringComparison.Ordinal)
            || !userCode.Contains("Math.Sqrt(values[0])", StringComparison.Ordinal)
            || userCode.Contains("__PscpSeq", StringComparison.Ordinal))
        {
            failures.Add($"DotNetPassThroughLowering: expected direct .NET API lowering not found\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyStructObjectInitializerLowering(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                struct PointData {
                    int X, Y
                }

                PointData point = new(1, 2)
                += point.X + point.Y
                """),
            new TranspilationOptions("Pscp.Generated", "StructObjectInitializerLoweringProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"StructObjectInitializerLowering: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        string userCode = GetUserCodePortion(result.CSharpCode);
        if (!userCode.Contains("new PointData { X = 1, Y = 2 }", StringComparison.Ordinal))
        {
            failures.Add($"StructObjectInitializerLowering: expected object-initializer lowering not found\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyImplicitFieldAccessibilityLowering(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                class Worker {
                    record struct Job(int Id)
                    List<Job> jobs
                }

                struct PointData {
                    int X, Y
                }
                """),
            new TranspilationOptions("Pscp.Generated", "ImplicitFieldAccessibilityLoweringProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"ImplicitFieldAccessibilityLowering: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        string userCode = GetUserCodePortion(result.CSharpCode);
        if (userCode.Contains("public List<Job> jobs", StringComparison.Ordinal)
            || !userCode.Contains("List<Job> jobs = new();", StringComparison.Ordinal)
            || !userCode.Contains("public int X;", StringComparison.Ordinal)
            || !userCode.Contains("public int Y;", StringComparison.Ordinal))
        {
            failures.Add($"ImplicitFieldAccessibilityLowering: expected private class fields and public value-type fields\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyAutoConstructArrayDeclarationLowering(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                int n = 4
                List<int>[] graph = new![n]
                
                int solve() {
                    graph.Length
                }
                += solve()
                """),
            new TranspilationOptions("Pscp.Generated", "AutoConstructArrayDeclarationLoweringProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"AutoConstructArrayDeclarationLowering: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        string userCode = GetUserCodePortion(result.CSharpCode);
        if (!(userCode.Contains("List<int>[] graph = new List<int>[", StringComparison.Ordinal)
                || userCode.Contains("graph = new List<int>[", StringComparison.Ordinal))
            || !userCode.Contains("graph[", StringComparison.Ordinal)
            || !userCode.Contains("= new List<int>();", StringComparison.Ordinal)
            || userCode.Contains("__PscpSeq.expr", StringComparison.Ordinal))
        {
            failures.Add($"AutoConstructArrayDeclarationLowering: expected direct array auto-construction lowering\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyReverseCompareToLowering(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                record struct Item(int Value) {
                    operator<=>(left, right) => left.Value <=> right.Value
                }

                PriorityQueue<Item, Item> pq = new(-Item.CompareTo)
                pq += (new(1), new(1))
                pq += (new(3), new(3))
                += (--pq).Value
                """),
            new TranspilationOptions("Pscp.Generated", "ReverseCompareToLoweringProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"ReverseCompareToLowering: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        string userCode = GetUserCodePortion(result.CSharpCode);
        if (!userCode.Contains("Comparer<Item>.Create((__left, __right) => Item.CompareTo(__right, __left))", StringComparison.Ordinal))
        {
            failures.Add($"ReverseCompareToLowering: expected reversed comparer lowering not found\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyPostfixStatementLowering(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                mut int x = 3
                x--
                x++
                += x
                """),
            new TranspilationOptions("Pscp.Generated", "PostfixStatementLoweringProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"PostfixStatementLowering: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        string userCode = GetUserCodePortion(result.CSharpCode);
        if (!userCode.Contains("x--;", StringComparison.Ordinal)
            || !userCode.Contains("x++;", StringComparison.Ordinal)
            || userCode.Contains("(x--);", StringComparison.Ordinal)
            || userCode.Contains("(x++);", StringComparison.Ordinal))
        {
            failures.Add($"PostfixStatementLowering: expected direct statement lowering not found\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifySemanticDiagnostics(List<string> failures)
    {
        ExpectDiagnostic(
            failures,
            "UndefinedName",
            """
            int x = missing + 1
            += x
            """,
            "Undefined name `missing`.");

        ExpectDiagnostic(
            failures,
            "UnknownIntrinsicMember",
            """
            += stdin.nope()
            """,
            "Unknown intrinsic member `stdin.nope`.");

        ExpectDiagnostic(
            failures,
            "UnknownUserMember",
            """
            class Box {
                int value;
            }

            Box box = new()
            += box.nope()
            """,
            "Type `Box` does not contain a member named `nope`.");

        ExpectDiagnostic(
            failures,
            "InvalidNewBang",
            """
            int[] arr = new![3]
            += arr.Length
            """,
            "`new![n]` requires a known auto-constructible collection element type.");

        ExpectDiagnostic(
            failures,
            "RecursiveWithoutRec",
            """
            int fact(int n) {
                if n <= 1 then 1 else n * fact(n - 1)
            }
            += fact(5)
            """,
            "Recursive self-reference to `fact` requires the `rec` modifier.");

        ExpectDiagnostic(
            failures,
            "InvalidComparatorSugar",
            """
            int x = 1
            let bad = x.asc
            += bad
            """,
            "comparator sugar requires a type receiver");

        ExpectDiagnostic(
            failures,
            "InvalidTupleProjection",
            """
            let pair = (1, 2)
            += pair.3
            """,
            "Invalid tuple projection `.3`.");

        ExpectDiagnostic(
            failures,
            "InvalidSliceTarget",
            """
            int x = 1
            += x[..]
            """,
            "Slicing is supported only on strings and arrays.");

        ExpectDiagnostic(
            failures,
            "InvalidImplicitReturn",
            """
            int bad() {
                stdout.write(1)
            }
            += bad()
            """,
            "Final expression in `bad` returns `void`, but `int` is required.");

        ExpectDiagnostic(
            failures,
            "ArrayAddCall",
            """
            int[] values = [1, 2, 3]
            values.Add(4)
            """,
            "Arrays do not contain an `Add` method.");

        ExpectDiagnostic(
            failures,
            "PriorityQueueTryDequeueShape",
            """
            PriorityQueue<int, int> pq = new()
            pq.TryDequeue(out int x)
            += x
            """,
            "requires two `out` arguments");
    }

    private static void ExpectDiagnostic(List<string> failures, string name, string source, string expectedMessage)
    {
        TranspilationResult result = PscpTranspiler.Transpile(NormalizeSource(source));
        if (!result.Diagnostics.Any(diagnostic => diagnostic.Message.Contains(expectedMessage, StringComparison.Ordinal)))
        {
            failures.Add($"{name}: expected diagnostic '{expectedMessage}' not found\nActual:\n{FormatDiagnostics(result.Diagnostics)}\nGenerated:\n{result.CSharpCode}");
        }
    }
    private static string FindWorkspaceRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Pscp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the workspace root.");
    }

    private static string NormalizeSource(string source)
        => source.Replace("\r\n", "\n").Trim() + "\n";

    private static string NormalizeOutput(string text)
        => text.Replace("\r\n", "\n");

    private static string Escape(string text)
        => text.Replace("\n", "\\n");

    private static string FormatDiagnostics(IReadOnlyList<Diagnostic> diagnostics)
        => string.Join("\n", diagnostics.Select(diagnostic => $"{diagnostic.Message} at {diagnostic.Span.Start}"));

    private static string GetUserCodePortion(string generatedCode)
    {
        const string runtimeMarker = "public static class __PscpArray";
        int index = generatedCode.IndexOf(runtimeMarker, StringComparison.Ordinal);
        return index >= 0 ? generatedCode[..index] : generatedCode;
    }

    private static string CreateProjectFile()
        => """
           <Project Sdk="Microsoft.NET.Sdk">
             <PropertyGroup>
               <OutputType>Exe</OutputType>
               <TargetFramework>net10.0</TargetFramework>
               <ImplicitUsings>enable</ImplicitUsings>
               <Nullable>enable</Nullable>
             </PropertyGroup>
           </Project>
           """;

    private static async Task<ProcessResult> RunDotnetAsync(string arguments, string workingDirectory, string stdIn)
    {
        ProcessStartInfo startInfo = new("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        string dotnetHome = Path.Combine(FindWorkspaceRoot(), ".dotnet");
        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.Environment["DOTNET_CLI_HOME"] = dotnetHome;
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet process.");

        if (!string.IsNullOrEmpty(stdIn))
        {
            await process.StandardInput.WriteAsync(stdIn);
        }

        process.StandardInput.Close();

        Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stdErrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ProcessResult(process.ExitCode, await stdOutTask, await stdErrTask);
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}


