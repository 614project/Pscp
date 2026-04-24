using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Pscp.Transpiler;

internal sealed record TestCase(string Name, string Source, string Input, string ExpectedOutput)
{
    public IReadOnlyList<string> ExpectedWarnings { get; init; } = Array.Empty<string>();
}

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
                "ReadIntCornerCases",
                """
                int a =
                int b =
                += a
                += b
                """,
                "+7 -2147483648",
                "7\n-2147483648\n"),
            new(
                "LineReaderAfterTokenInput",
                """
                int n =
                string text = stdin.line()
                += n
                += text
                """,
                "3\nabc\n",
                "3\nabc\n"),
            new(
                "CharGridAfterTokenInput",
                """
                int n =
                char[][] grid = stdin.charGrid(n)
                += grid[0]
                += grid[1]
                """,
                "2\nab\ncd\n",
                "a b\nc d\n"),
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
                "DefaultRangeStepSemantics",
                """
                let forward = [1..3]
                let descendingDefault = [3..1]
                let descendingStepped = [3..-1..1]
                += forward
                += descendingDefault.Length
                += descendingStepped
                """,
                "",
                "1 2 3\n0\n3 2 1\n"),
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
                "TopLevelFunctionCaptureAndValueAwareBlocks",
                """
                var bug = 0

                int solve() {
                    if bug == 0 {
                        bug++
                        bug
                    }

                    0..<1 -> _ {
                        bug--
                    }
                    bug
                }

                void touch() {
                    chmax(ref bug, 2)
                }

                += solve()
                touch()
                += solve()
                += bug
                """,
                "",
                "1\n1\n1\n"),
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
                "GeneratorBlockReturnSumInference",
                """
                int n, k =
                int[n] h =

                var low = 0
                var high = 1000000000
                var result = 0

                while low <= high {
                    let mid = low + (high - low) / 2

                    let cnt = sum (0..<n -> a {
                        let l = a > 0 and abs(h[a] - h[a - 1]) > mid
                        let r = a < n - 1 and abs(h[a] - h[a + 1]) > mid
                        return int(l or r)
                    })

                    if cnt <= k {
                        result = mid
                        high = mid - 1
                    } else {
                        low = mid + 1
                    }
                }

                += result
                """,
                "3 1 1 2 3",
                "1\n"),
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
                "1\n2\n5\n")
            {
                ExpectedWarnings =
                [
                    "removed in v0.6"
                ]
            },
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
                "MultilineBinaryOperators",
                """
                bool a = true
                bool b = true
                int x = 3
                int y = 3

                if a and
                   b then
                    += 1

                if x ==
                   y then
                    += 2
                """,
                "",
                "1\n2\n"),
            new(
                "MultilineCollectionLiteral",
                """
                let l = [
                1,
                2,
                3
                ]
                += l
                """,
                "",
                "1 2 3\n"),
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
                "V06IntrinsicShadowing",
                """
                rec int gcd(int a, int b) {
                    if b == 0 then a
                    else gcd(b, a % b)
                }

                int x = 6
                += gcd(10, x)
                """,
                "",
                "2\n"),
            new(
                "V06CanonicalStdinApi",
                """
                int n = stdin.readInt()
                int[] values = stdin.readArray<int>(n)
                string line = stdin.readLine()
                += values.sum()
                += line
                """,
                "3 1 2 3\nhello\n",
                "6\nhello\n"),
            new(
                "V06KnownDataStructureRewrites",
                """
                Dictionary<int, int> dict;
                SortedSet<int> sorted;
                HashSet<int> seen;

                += dict += (1, 10)
                += dict += (1, 20)
                += dict[1]
                += sorted += 5
                += sorted -= 5
                += seen += 7
                += seen += 7
                """,
                "",
                "True\nFalse\n10\nTrue\nTrue\nTrue\nFalse\n"),
            new(
                "V06CollectionDictionaryHelpers",
                """
                int[] arr = [3, 1, 3, 2]
                let counts = arr.groupCount()
                let aliases = arr.freq()
                let positions = arr.sort().distinct().index()
                += counts[3]
                += aliases[1]
                += positions[2]
                """,
                "",
                "2\n1\n1\n"),
            new(
                "ThisMemberAndTypedInput",
                """
                class Counter {
                    int value;

                    void add(int delta) {
                        this.value += delta
                    }

                    int total() => this.value
                }

                Counter c = new()
                int extra = stdin.readInt()
                c.add(extra)
                c.add(2)
                += c.total()
                """,
                "5",
                "7\n"),
            new(
                "TupleSortByInference",
                """
                int n = 3
                (int, int)[n] arr =
                let sorted = arr.sortBy(x => x.1)
                += sorted[0]
                += sorted[1]
                += sorted[2]
                """,
                "2 1 1 5 2 3",
                "1 5\n2 1\n2 3\n"),
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
            new(
                "MatchWhenAsIdentifiers",
                """
                int match = 1
                int when = 2
                += match + when
                """,
                "",
                "3\n"),
            new(
                "MathIntrinsicFamilyV06",
                """
                += clamp(10, 0, 7)
                += gcd(12, 18)
                += lcm(12, 18)
                double x = 3.5
                += floor(x)
                += ceil(x)
                += round(x)
                += pow(2, 10)
                += popcount(13)
                += bitLength(16)
                """,
                "",
                "7\n6\n36\n3\n4\n4\n1024\n3\n5\n"),
            new(
                "SwitchExpressionPassThrough",
                """
                int x = 2
                let y = x switch { 1 => 10, 2 => 20, _ => 0 }
                += y
                """,
                "",
                "20\n"),
        ];

        List<string> failures = [];

        foreach (TestCase testCase in testCases)
        {
            string safeName = string.Concat(testCase.Name.Where(char.IsLetterOrDigit));
            TranspilationResult result = PscpTranspiler.Transpile(
                NormalizeSource(testCase.Source),
                new TranspilationOptions("Pscp.Generated", safeName + "Program"));

            IReadOnlyList<Diagnostic> errors = result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            IReadOnlyList<Diagnostic> warnings = result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning).ToArray();

            if (errors.Count > 0)
            {
                failures.Add($"{testCase.Name}: transpiler diagnostics\n{FormatDiagnostics(result.Diagnostics)}\nGenerated:\n{result.CSharpCode}");
                continue;
            }

            if (testCase.ExpectedWarnings.Count == 0)
            {
                if (warnings.Count > 0)
                {
                    failures.Add($"{testCase.Name}: unexpected warnings\n{FormatDiagnostics(result.Diagnostics)}\nGenerated:\n{result.CSharpCode}");
                    continue;
                }
            }
            else if (testCase.ExpectedWarnings.Any(expected => warnings.All(diagnostic => !diagnostic.Message.Contains(expected, StringComparison.OrdinalIgnoreCase))))
            {
                failures.Add($"{testCase.Name}: missing expected warnings\nExpected: {string.Join(", ", testCase.ExpectedWarnings)}\nActual:\n{FormatDiagnostics(result.Diagnostics)}\nGenerated:\n{result.CSharpCode}");
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
        VerifyExplicitStdinArraySpecialization(failures);
        VerifyCompactAndVerboseHelperEmission(failures);
        VerifyCompactSequenceMemberPruning(failures);
        VerifyRangeLoopLoweringSemantics(failures);
        VerifyLineReaderAlignment(failures);
        VerifyCollectionSeparatorTolerance(failures);
        VerifyDiscardLoopLowering(failures);
        VerifyShorthandInputAvoidsGenericHelpers(failures);
        VerifySortByLowering(failures);
        VerifyCompactPrunesStdoutRender(failures);
        VerifyDictionaryIndexTypeAvoidsStdoutFallback(failures);
        VerifyEmptyMinMaxPolicy(failures);
        VerifyMathIntrinsicLowering(failures);
        VerifyV06MathLowering(failures);
        VerifySwitchExpressionLowering(failures);
        VerifyRunAndFlushShape(failures);
        VerifyExplainHeaderEmission(failures);
        VerifyWarningDiagnostics(failures);
        VerifyRemovedGenericReadSurface(failures);
        await VerifyLanguageServerDiagnosticsAndIntrinsicCompletionAsync(failures);
        await VerifyLanguageServerDotNetCompletionAsync(failures);
        await VerifyLanguageServerCollectionCompletionRenameAndFreshDiagnosticsAsync(failures);
        await VerifyLanguageServerLoopBlockAndIndexerDiagnosticsAsync(failures);

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
            || userCode.Contains("__start", StringComparison.Ordinal)
            || userCode.Contains("__end", StringComparison.Ordinal)
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
                let total = sum (0..<n -> i {
                    let value = i * i
                    return value
                })
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
            || userCode.Contains("Enumerable.Sum", StringComparison.Ordinal)
            || userCode.Contains("__PscpThunk.run", StringComparison.Ordinal)
            || userCode.Contains("object total = default", StringComparison.Ordinal))
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
            || userCode.Contains("__start", StringComparison.Ordinal)
            || userCode.Contains("__end", StringComparison.Ordinal)
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

    private static void VerifyExplicitStdinArraySpecialization(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                int n = 3
                let xs = stdin.array<int>(n)
                += xs
                """),
            new TranspilationOptions("Pscp.Generated", "ExplicitStdinArraySpecializationProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"ExplicitStdinArraySpecialization: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        string userCode = GetUserCodePortion(result.CSharpCode);
        if (!userCode.Contains("stdin.readArrayInt(n)", StringComparison.Ordinal)
            || userCode.Contains("stdin.array<int>(n)", StringComparison.Ordinal))
        {
            failures.Add($"ExplicitStdinArraySpecialization: expected direct stdin.readArrayInt lowering\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyCompactAndVerboseHelperEmission(List<string> failures)
    {
        const string source = """
            int x = 3
            += x
            """;

        TranspilationResult compact = PscpTranspiler.Transpile(
            NormalizeSource(source),
            new TranspilationOptions("Pscp.Generated", "CompactHelpersProgram", HelperEmissionMode.Compact));
        TranspilationResult verbose = PscpTranspiler.Transpile(
            NormalizeSource(source),
            new TranspilationOptions("Pscp.Generated", "VerboseHelpersProgram", HelperEmissionMode.Verbose));

        if (compact.Diagnostics.Count > 0 || verbose.Diagnostics.Count > 0)
        {
            failures.Add($"CompactAndVerboseHelperEmission: unexpected diagnostics\nCompact:\n{FormatDiagnostics(compact.Diagnostics)}\nVerbose:\n{FormatDiagnostics(verbose.Diagnostics)}");
            return;
        }

        if (compact.CSharpCode.Contains("public static class __PscpSeq", StringComparison.Ordinal)
            || !verbose.CSharpCode.Contains("public static class __PscpSeq", StringComparison.Ordinal))
        {
            failures.Add($"CompactAndVerboseHelperEmission: expected helper pruning difference\nCompact:\n{compact.CSharpCode}\nVerbose:\n{verbose.CSharpCode}");
        }
    }

    private static void VerifyCompactSequenceMemberPruning(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                int[] values = [1, 2, 3]
                let mapped = values.map(x => x + 1)
                += mapped
                """),
            new TranspilationOptions("Pscp.Generated", "CompactSequenceMemberPruningProgram", HelperEmissionMode.Compact));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"CompactSequenceMemberPruning: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        if (!result.CSharpCode.Contains("public static class __PscpSeq", StringComparison.Ordinal)
            || !result.CSharpCode.Contains("public static TResult[] map", StringComparison.Ordinal)
            || result.CSharpCode.Contains("public static T[] filter", StringComparison.Ordinal)
            || result.CSharpCode.Contains("public static int sum(", StringComparison.Ordinal)
            || result.CSharpCode.Contains("public static T[] sort", StringComparison.Ordinal))
        {
            failures.Add($"CompactSequenceMemberPruning: expected only referenced sequence members to be emitted\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyRangeLoopLoweringSemantics(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                int n = 3
                mut int total = 0
                for i in 3..1 {
                    total += i
                }
                for j in 1..n {
                    total += j
                }
                += total
                """),
            new TranspilationOptions("Pscp.Generated", "RangeLoopLoweringSemanticsProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"RangeLoopLoweringSemantics: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        string userCode = GetUserCodePortion(result.CSharpCode);
        if (userCode.Contains("__step", StringComparison.Ordinal)
            || userCode.Contains("Range step cannot be zero.", StringComparison.Ordinal)
            || userCode.Contains("? i <=", StringComparison.Ordinal)
            || userCode.Contains("? j <=", StringComparison.Ordinal)
            || userCode.Contains("__start", StringComparison.Ordinal)
            || userCode.Contains("__end", StringComparison.Ordinal))
        {
            failures.Add($"RangeLoopLoweringSemantics: expected direct default-step for-loops without step guards\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyLineReaderAlignment(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                int n =
                string text = stdin.line()
                char[][] grid = stdin.charGrid(2)
                += text
                += grid[0]
                """),
            new TranspilationOptions("Pscp.Generated", "LineReaderAlignmentProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"LineReaderAlignment: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        if (!result.CSharpCode.Contains("ConsumePendingLineBoundary", StringComparison.Ordinal))
        {
            failures.Add($"LineReaderAlignment: expected line-alignment helper not found\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyCollectionSeparatorTolerance(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                let l = [
                1,
                2,
                3
                ]
                += l
                """),
            new TranspilationOptions("Pscp.Generated", "CollectionSeparatorToleranceProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"CollectionSeparatorTolerance: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyDiscardLoopLowering(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                let values = [0..<4 -> _ do 1]
                += values
                """),
            new TranspilationOptions("Pscp.Generated", "DiscardLoopLoweringProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"DiscardLoopLowering: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        string userCode = GetUserCodePortion(result.CSharpCode);
        if (userCode.Contains("_ =", StringComparison.Ordinal))
        {
            failures.Add($"DiscardLoopLowering: expected synthetic loop names instead of per-iteration discard assignments\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyShorthandInputAvoidsGenericHelpers(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                int n =
                long[n] a =
                += a
                """),
            new TranspilationOptions("Pscp.Generated", "ShorthandInputAvoidsGenericHelpersProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"ShorthandInputAvoidsGenericHelpers: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        if (result.CSharpCode.Contains("stdin.read<", StringComparison.Ordinal)
            || result.CSharpCode.Contains("stdin.array<", StringComparison.Ordinal))
        {
            failures.Add($"ShorthandInputAvoidsGenericHelpers: expected direct typed scanner lowering only\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyCompactPrunesStdoutRender(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                int x = 3
                += x
                """),
            new TranspilationOptions("Pscp.Generated", "CompactPrunesStdoutRenderProgram", HelperEmissionMode.Compact));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"CompactPrunesStdoutRender: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        if (result.CSharpCode.Contains("__PscpRender", StringComparison.Ordinal)
            || result.CSharpCode.Contains("write<T>", StringComparison.Ordinal)
            || result.CSharpCode.Contains("writeln<T>", StringComparison.Ordinal))
        {
            failures.Add($"CompactPrunesStdoutRender: expected scalar-only output to avoid generic render helpers\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyDictionaryIndexTypeAvoidsStdoutFallback(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                int[] xs = [1, 2, 2]
                let counts = xs.groupCount()
                += counts[2]
                """),
            new TranspilationOptions("Pscp.Generated", "DictionaryIndexTypeAvoidsStdoutFallbackProgram", HelperEmissionMode.Compact));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"DictionaryIndexTypeAvoidsStdoutFallback: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        if (result.CSharpCode.Contains("write<T>", StringComparison.Ordinal)
            || result.CSharpCode.Contains("writeln<T>", StringComparison.Ordinal)
            || result.CSharpCode.Contains("__PscpRender", StringComparison.Ordinal))
        {
            failures.Add($"DictionaryIndexTypeAvoidsStdoutFallback: expected dictionary indexer output to stay on direct scalar stdout path\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifySortByLowering(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                (int, int)[] arr = [(2, 1), (1, 5), (2, 3)]
                let sorted = arr.sortBy(x => x.1)
                += sorted[0]
                """),
            new TranspilationOptions("Pscp.Generated", "SortByLoweringProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"SortByLowering: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        string userCode = GetUserCodePortion(result.CSharpCode);
        if (!userCode.Contains("Array.Sort(sorted", StringComparison.Ordinal)
            || userCode.Contains("__PscpSeq.sortBy", StringComparison.Ordinal)
            || userCode.Contains("__PscpThunk.run", StringComparison.Ordinal))
        {
            failures.Add($"SortByLowering: expected direct Array.Sort lowering\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyEmptyMinMaxPolicy(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                int[] values = []
                let answer = min(values)
                += answer
                """),
            new TranspilationOptions("Pscp.Generated", "EmptyMinMaxPolicyProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"EmptyMinMaxPolicy: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        if (!result.CSharpCode.Contains("throw new InvalidOperationException(\"Sequence contains no elements.\")", StringComparison.Ordinal)
            || result.CSharpCode.Contains("return default!", StringComparison.Ordinal))
        {
            failures.Add($"EmptyMinMaxPolicy: expected explicit empty-sequence failure instead of default!\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyMathIntrinsicLowering(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                double x = 9
                += sqrt(x)
                += abs(-3)
                """),
            new TranspilationOptions("Pscp.Generated", "MathIntrinsicLoweringProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"MathIntrinsicLowering: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        string userCode = GetUserCodePortion(result.CSharpCode);
        if (!userCode.Contains("System.Math.Sqrt(x)", StringComparison.Ordinal)
            || !userCode.Contains("System.Math.Abs((-3))", StringComparison.Ordinal))
        {
            failures.Add($"MathIntrinsicLowering: expected direct System.Math intrinsic lowering\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyV06MathLowering(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                int a = clamp(10, 0, 7)
                int g = gcd(12, 18)
                int l = lcm(12, 18)
                let bits = popcount(13)
                let len = bitLength(16)
                += (a, g, l, bits, len)
                """),
            new TranspilationOptions("Pscp.Generated", "V06MathLoweringProgram"));

        if (result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            failures.Add($"V06MathLowering: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        string userCode = GetUserCodePortion(result.CSharpCode);
        if (!userCode.Contains("System.Math.Clamp", StringComparison.Ordinal)
            || !userCode.Contains("__PscpSeq.gcd", StringComparison.Ordinal)
            || !userCode.Contains("__PscpSeq.lcm", StringComparison.Ordinal)
            || !userCode.Contains("__PscpSeq.popcount", StringComparison.Ordinal)
            || !userCode.Contains("__PscpSeq.bitLength", StringComparison.Ordinal))
        {
            failures.Add($"V06MathLowering: expected v0.6 math lowering not found\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifySwitchExpressionLowering(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                int x = 2
                let y = x switch { 1 => 10, 2 => 20, _ => 0 }
                += y
                """),
            new TranspilationOptions("Pscp.Generated", "SwitchExpressionLoweringProgram"));

        if (result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            failures.Add($"SwitchExpressionLowering: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        string userCode = GetUserCodePortion(result.CSharpCode);
        if (!userCode.Contains("x switch", StringComparison.Ordinal)
            || !userCode.Contains("1=>10", StringComparison.Ordinal)
            || !userCode.Contains("_=>0", StringComparison.Ordinal))
        {
            failures.Add($"SwitchExpressionLowering: expected pass-through switch expression not found\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyRunAndFlushShape(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                += 1
                """),
            new TranspilationOptions("Pscp.Generated", "RunAndFlushShapeProgram"));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"RunAndFlushShape: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        string userCode = GetUserCodePortion(result.CSharpCode);
        if (!userCode.Contains("public static void Main()", StringComparison.Ordinal)
            || !userCode.Contains("Run();", StringComparison.Ordinal)
            || !userCode.Contains("stdout.flush();", StringComparison.Ordinal)
            || !userCode.Contains("private static void Run()", StringComparison.Ordinal)
            || userCode.Contains("finally", StringComparison.Ordinal))
        {
            failures.Add($"RunAndFlushShape: expected Run()+trailing flush shape\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyExplainHeaderEmission(List<string> failures)
    {
        const string source = """
            // ignored
            int x = 3
            += x
            """;

        string normalized = NormalizeSource(source);
        TranspilationResult result = PscpTranspiler.Transpile(
            normalized,
            new TranspilationOptions("Pscp.Generated", "ExplainHeaderProgram", HelperEmissionMode.Compact, Explain: true, Older: false, ExplainSource: normalized));

        if (result.Diagnostics.Count > 0)
        {
            failures.Add($"ExplainHeaderEmission: unexpected diagnostics\n{FormatDiagnostics(result.Diagnostics)}");
            return;
        }

        if (!result.CSharpCode.Contains("PSCP source:", StringComparison.Ordinal)
            || !result.CSharpCode.Contains("int x = 3", StringComparison.Ordinal)
            || result.CSharpCode.Contains("// ignored", StringComparison.Ordinal))
        {
            failures.Add($"ExplainHeaderEmission: expected stripped PSCP comment header\nGenerated:\n{result.CSharpCode}");
        }
    }

    private static void VerifyWarningDiagnostics(List<string> failures)
    {
        TranspilationResult result = PscpTranspiler.Transpile(
            NormalizeSource(
                """
                string? maybe = null
                string text = maybe
                mut int base = 1
                += text
                """),
            new TranspilationOptions("Pscp.Generated", "WarningDiagnosticsProgram"));

        if (result.Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Warning))
        {
            failures.Add("WarningDiagnostics: expected warning diagnostics.");
            return;
        }

        if (!result.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("nullable", StringComparison.OrdinalIgnoreCase))
            || !result.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("reserved C# keyword", StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add($"WarningDiagnostics: expected nullable and reserved-keyword warnings\n{FormatDiagnostics(result.Diagnostics)}");
        }
    }

    private static void VerifyRemovedGenericReadSurface(List<string> failures)
    {
        ExpectDiagnostic(
            failures,
            "RemovedGenericReadSurface",
            """
            int x = stdin.read<int>()
            += x
            """,
            "Unknown intrinsic member `stdin.read`.");
    }

    private static async Task VerifyLanguageServerDiagnosticsAndIntrinsicCompletionAsync(List<string> failures)
    {
        const string source = """
            int n =
            stdin.
            """;

        await using LspProbeSession session = await LspProbeSession.StartAsync(FindWorkspaceRoot(), NormalizeSource(source));
        IReadOnlyList<string> diagnostics = await session.ReadDiagnosticsAsync();
        if (diagnostics.Count == 0)
        {
            failures.Add("LanguageServerDiagnosticsAndIntrinsicCompletion: expected diagnostics for incomplete input shorthand.");
            return;
        }

        IReadOnlyList<string> memberLabels = await session.RequestCompletionLabelsAsync(line: 1, character: 6);
        if (!memberLabels.Contains("readInt", StringComparer.Ordinal)
            || !memberLabels.Contains("readArray", StringComparer.Ordinal)
            || !memberLabels.Contains("readCharGrid", StringComparer.Ordinal))
        {
            failures.Add($"LanguageServerDiagnosticsAndIntrinsicCompletion: expected stdin members not found\nActual: {string.Join(", ", memberLabels)}");
        }
    }

    private static async Task VerifyLanguageServerDotNetCompletionAsync(List<string> failures)
    {
        const string source = """
            using System

            let value = Math.Sqrt(4.0)
            Math.
            """;

        await using LspProbeSession session = await LspProbeSession.StartAsync(FindWorkspaceRoot(), NormalizeSource(source));
        _ = await session.ReadDiagnosticsAsync();

        IReadOnlyList<string> rootLabels = await session.RequestCompletionLabelsAsync(line: 2, character: 11);
        if (!rootLabels.Contains("Math", StringComparer.Ordinal)
            || !rootLabels.Contains("Console", StringComparer.Ordinal)
            || !rootLabels.Contains("System", StringComparer.Ordinal))
        {
            failures.Add($"LanguageServerDotNetCompletion: expected root .NET completions not found\nActual: {string.Join(", ", rootLabels)}");
            return;
        }

        IReadOnlyList<string> memberLabels = await session.RequestCompletionLabelsAsync(line: 3, character: 5);
        if (!memberLabels.Contains("Sqrt", StringComparer.Ordinal)
            || !memberLabels.Contains("Pow", StringComparer.Ordinal)
            || memberLabels.Contains("asc", StringComparer.Ordinal))
        {
            failures.Add($"LanguageServerDotNetCompletion: expected Math members not found or comparator leak remained\nActual: {string.Join(", ", memberLabels)}");
        }
    }

    private static async Task VerifyLanguageServerCollectionCompletionRenameAndFreshDiagnosticsAsync(List<string> failures)
    {
        const string source = """
            let value = 1
            += value
            int[] arr = [1, 2, 3]
            arr.
            """;

        await using LspProbeSession session = await LspProbeSession.StartAsync(FindWorkspaceRoot(), NormalizeSource(source));
        _ = await session.ReadDiagnosticsAsync();

        IReadOnlyList<string> memberLabels = await session.RequestCompletionLabelsAsync(line: 3, character: 4);
        if (!memberLabels.Contains("map", StringComparer.Ordinal)
            || !memberLabels.Contains("sortBy", StringComparer.Ordinal)
            || !memberLabels.Contains("Length", StringComparer.Ordinal))
        {
            failures.Add($"LanguageServerCollectionCompletionRenameAndFreshDiagnostics: expected collection and .NET members not found\nActual: {string.Join(", ", memberLabels)}");
        }

        int renameEditCount = await session.RequestRenameEditCountAsync(line: 0, character: 4, newName: "answer");
        if (renameEditCount != 2)
        {
            failures.Add($"LanguageServerCollectionCompletionRenameAndFreshDiagnostics: expected 2 rename edits, got {renameEditCount}");
        }

        await session.SendDidChangeAsync("let ok = [\n1,\n2\n]\n", version: 2);
        IReadOnlyList<string> freshDiagnostics = await session.ReadDiagnosticsAsync();
        if (freshDiagnostics.Count != 0)
        {
            failures.Add($"LanguageServerCollectionCompletionRenameAndFreshDiagnostics: expected diagnostics to clear after a valid edit\nActual: {string.Join(" | ", freshDiagnostics)}");
        }
    }

    private static async Task VerifyLanguageServerLoopBlockAndIndexerDiagnosticsAsync(List<string> failures)
    {
        const string source = """
            int[2][2] grid;
            int[] xs = [0, 1]

            for y in 1..-1..0 {
                if grid[y][xs[0]] == 0 {
                    grid[y][xs[0]] = 1
                    break
                }
            }
            """;

        await using LspProbeSession session = await LspProbeSession.StartAsync(FindWorkspaceRoot(), NormalizeSource(source));
        IReadOnlyList<string> diagnostics = await session.ReadDiagnosticsAsync();
        if (diagnostics.Count != 0)
        {
            failures.Add($"LanguageServerLoopBlockAndIndexerDiagnostics: expected no diagnostics for loop-local break and indexed element assignment\nActual: {string.Join(" | ", diagnostics)}");
        }

        await session.SendDidChangeAsync("let x = 1\nx = 2\n", version: 2);
        IReadOnlyList<string> immutableDiagnostics = await session.ReadDiagnosticsAsync();
        if (!immutableDiagnostics.Any(message => message.Contains("Cannot assign to immutable binding `x`.", StringComparison.Ordinal)))
        {
            failures.Add($"LanguageServerLoopBlockAndIndexerDiagnostics: expected immutable simple assignment diagnostic\nActual: {string.Join(" | ", immutableDiagnostics)}");
        }
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
        string[] runtimeMarkers =
        [
            "public static class __PscpArray",
            "public static class __PscpThunk",
            "public static class __PscpSeq",
            "public sealed class __PscpStdin",
            "public sealed class __PscpStdout",
            "public static class __PscpRender",
        ];

        int index = runtimeMarkers
            .Select(marker => generatedCode.IndexOf(marker, StringComparison.Ordinal))
            .Where(position => position >= 0)
            .DefaultIfEmpty(-1)
            .Min();

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

    private sealed class LspProbeSession : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly StreamWriter _writer;
        private readonly StreamReader _reader;
        private readonly string _uri;
        private int _nextRequestId = 2;

        private LspProbeSession(Process process, StreamWriter writer, StreamReader reader, string uri)
        {
            _process = process;
            _writer = writer;
            _reader = reader;
            _uri = uri;
        }

        public static async Task<LspProbeSession> StartAsync(string workspaceRoot, string source)
        {
            string uri = new Uri(Path.Combine(workspaceRoot, ".generated-tests", "lsp-probe.pscp")).AbsoluteUri;
            string rootUri = new Uri(workspaceRoot + Path.DirectorySeparatorChar).AbsoluteUri;
            string escapedSource = JsonEncodedText.Encode(source).ToString();
            string serverProject = Path.Combine(workspaceRoot, "src", "Pscp.LanguageServer", "Pscp.LanguageServer.csproj");
            ProcessResult build = await RunDotnetAsync($"build \"{serverProject}\" -nologo -v q", workspaceRoot, string.Empty);
            if (build.ExitCode != 0)
            {
                throw new InvalidOperationException($"Failed to build PSCP language server.\n{build.StdOut}\n{build.StdErr}");
            }

            string serverDll = Path.Combine(workspaceRoot, "src", "Pscp.LanguageServer", "bin", "Debug", "net10.0", "Pscp.LanguageServer.dll");
            ProcessStartInfo startInfo = new("dotnet", $"\"{serverDll}\"")
            {
                WorkingDirectory = workspaceRoot,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            string dotnetHome = Path.Combine(workspaceRoot, ".dotnet");
            startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
            startInfo.Environment["DOTNET_CLI_HOME"] = dotnetHome;
            startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

            Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start PSCP language server process.");

            _ = Task.Run(async () =>
            {
                string stderr = await process.StandardError.ReadToEndAsync();
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    Console.Error.WriteLine(stderr);
                }
            });

            LspProbeSession session = new(process, process.StandardInput, process.StandardOutput, uri);
            await session.SendAsync($"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{{\"processId\":1234,\"clientInfo\":{{\"name\":\"pscp-tests\",\"version\":\"0.6.2\"}},\"rootUri\":\"{rootUri}\",\"capabilities\":{{}}}}}}");
            _ = await session.ReadMessageAsync();
            await session.SendAsync("""{"jsonrpc":"2.0","method":"initialized","params":{}}""");
            await session.SendAsync($"{{\"jsonrpc\":\"2.0\",\"method\":\"textDocument/didOpen\",\"params\":{{\"textDocument\":{{\"uri\":\"{uri}\",\"languageId\":\"pscp\",\"version\":1,\"text\":\"{escapedSource}\"}}}}}}");
            return session;
        }

        public async Task<IReadOnlyList<string>> ReadDiagnosticsAsync()
        {
            JsonDocument message = await ReadMessageAsync();
            JsonElement diagnostics = message.RootElement.GetProperty("params").GetProperty("diagnostics");
            List<string> results = [];
            foreach (JsonElement diagnostic in diagnostics.EnumerateArray())
            {
                results.Add(diagnostic.GetProperty("message").GetString() ?? string.Empty);
            }

            message.Dispose();
            return results;
        }

        public async Task<IReadOnlyList<string>> RequestCompletionLabelsAsync(int line, int character)
        {
            int id = _nextRequestId++;
            string request = "{\"jsonrpc\":\"2.0\",\"id\":" + id
                + ",\"method\":\"textDocument/completion\",\"params\":{\"textDocument\":{\"uri\":\"" + _uri
                + "\"},\"position\":{\"line\":" + line + ",\"character\":" + character + "}}}";
            await SendAsync(request);
            JsonDocument response = await ReadMessageAsync();
            List<string> labels = [];
            foreach (JsonElement item in response.RootElement.GetProperty("result").GetProperty("items").EnumerateArray())
            {
                labels.Add(item.GetProperty("label").GetString() ?? string.Empty);
            }

            response.Dispose();
            return labels;
        }

        public async Task<int> RequestRenameEditCountAsync(int line, int character, string newName)
        {
            int id = _nextRequestId++;
            string request = "{\"jsonrpc\":\"2.0\",\"id\":" + id
                + ",\"method\":\"textDocument/rename\",\"params\":{\"textDocument\":{\"uri\":\"" + _uri
                + "\"},\"position\":{\"line\":" + line + ",\"character\":" + character
                + "},\"newName\":\"" + JsonEncodedText.Encode(newName) + "\"}}";
            await SendAsync(request);
            JsonDocument response = await ReadMessageAsync();
            int count = 0;
            if (response.RootElement.TryGetProperty("result", out JsonElement result)
                && result.ValueKind == JsonValueKind.Object
                && result.TryGetProperty("changes", out JsonElement changes)
                && changes.TryGetProperty(_uri, out JsonElement edits))
            {
                count = edits.GetArrayLength();
            }

            response.Dispose();
            return count;
        }

        public async Task SendDidChangeAsync(string source, int version)
        {
            string escapedSource = JsonEncodedText.Encode(source).ToString();
            await SendAsync($"{{\"jsonrpc\":\"2.0\",\"method\":\"textDocument/didChange\",\"params\":{{\"textDocument\":{{\"uri\":\"{_uri}\",\"version\":{version}}},\"contentChanges\":[{{\"text\":\"{escapedSource}\"}}]}}}}");
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await SendAsync($"{{\"jsonrpc\":\"2.0\",\"id\":{_nextRequestId++},\"method\":\"shutdown\",\"params\":{{}}}}");
                JsonDocument response = await ReadMessageAsync();
                response.Dispose();
                await SendAsync("""{"jsonrpc":"2.0","method":"exit","params":{}}""");
            }
            catch
            {
                // Ignore probe shutdown failures so test cleanup does not hide useful assertions.
            }

            if (!_process.HasExited)
            {
                await _process.WaitForExitAsync();
            }

            _process.Dispose();
        }

        private async Task SendAsync(string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            await _writer.WriteAsync($"Content-Length: {body.Length}\r\n\r\n{json}");
            await _writer.FlushAsync();
        }

        private async Task<JsonDocument> ReadMessageAsync()
        {
            string? line;
            int contentLength = 0;
            while (!string.IsNullOrEmpty(line = await _reader.ReadLineAsync()))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    contentLength = int.Parse(line["Content-Length:".Length..].Trim());
                }
            }

            if (contentLength <= 0)
            {
                throw new InvalidOperationException("Missing LSP content length.");
            }

            char[] buffer = new char[contentLength];
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await _reader.ReadAsync(buffer, offset, buffer.Length - offset);
                if (read == 0)
                {
                    throw new EndOfStreamException("Unexpected end of LSP stream.");
                }

                offset += read;
            }

            return JsonDocument.Parse(new string(buffer));
        }
    }
}


