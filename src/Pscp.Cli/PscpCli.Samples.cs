using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Pscp.Transpiler;

// Building the generated SDK project once, running the built program, `pscp test` and JSON output.
static partial class PscpCli
{
    private const int DefaultSampleTimeoutMs = 10_000;

    private sealed record GeneratedProgram(SdkLayout Layout, string SourceText, IReadOnlyList<Diagnostic> Diagnostics);

    private sealed record ProcessResult(int ExitCode, string Output);

    private sealed record SampleCase(string Name, string InputPath, string? ExpectedPath);

    private sealed record SampleResult(
        SampleCase Sample,
        string Status,
        string Actual,
        string StandardError,
        int? ExitCode,
        long ElapsedMs);

    private static async Task<GeneratedProgram> GenerateProgramAsync(string sourcePath, BackendOptions options)
    {
        string sourceText = await File.ReadAllTextAsync(sourcePath, Encoding.UTF8);
        SdkLayout layout = await EnsureSdkLayoutAsync(CreateSdkLayout(sourcePath), overwriteExisting: false, older: options.Older);
        TranspilationResult result = PscpTranspiler.Transpile(
            sourceText,
            CreateTranspilationOptions(sourcePath, sourceText, options, suffix: "Program"));
        if (!HasErrors(result.Diagnostics))
        {
            await File.WriteAllTextAsync(layout.GeneratedProgramPath, result.CSharpCode, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return new GeneratedProgram(layout, sourceText, result.Diagnostics);
    }

    // The build output is kept only to be shown when the build fails, so standard output stays the program's.
    private static async Task<ProcessResult> BuildGeneratedProgramAsync(SdkLayout layout, string configuration)
    {
        ProcessStartInfo startInfo = CreateDotnetStartInfo(
            $"build \"{layout.ProjectPath}\" -nologo -v q -clp:NoSummary -c {configuration}",
            layout.RootDirectory);
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet.");
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await output + await error);
    }

    // Runs the built assembly directly (`dotnet run` would build again and print to standard output).
    private static ProcessStartInfo CreateBuiltProgramStartInfo(SdkLayout layout, BackendOptions options)
    {
        string framework = options.Older ? "net6.0" : "net10.0";
        string assemblyPath = Path.Combine(layout.SdkDirectory, "bin", options.Configuration, framework, layout.AssemblyName + ".dll");
        return File.Exists(assemblyPath)
            ? CreateDotnetStartInfo($"\"{assemblyPath}\"", layout.RootDirectory)
            : CreateDotnetStartInfo($"run --no-build --project \"{layout.ProjectPath}\" -nologo -c {options.Configuration}", layout.RootDirectory);
    }

    private static async Task<int> RunBuiltProgramAsync(SdkLayout layout, BackendOptions options, Stream? input)
    {
        ProcessStartInfo startInfo = CreateBuiltProgramStartInfo(layout, options);
        startInfo.RedirectStandardInput = input is not null;
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet.");
        if (input is not null)
        {
            await input.CopyToAsync(process.StandardInput.BaseStream);
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static ProcessStartInfo CreateDotnetStartInfo(string arguments, string workingDirectory)
    {
        ProcessStartInfo startInfo = new("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.Environment["DOTNET_CLI_HOME"] = ResolveDotnetCliHome();
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        return startInfo;
    }

    // `pscp test` (guide §14.5): build once, run every sample, compare. Exit code 0 when every sample with an
    // expected output passes, 1 when one fails, 2 when the program does not build.
    private static async Task<int> TestAsync(string[] args)
    {
        const string usage = "pscp test [file.pscp] [--json] [-c Debug|Release] [--timeout ms] [--float-tolerance eps]";
        SourceResolution source = ResolveSourceArgument(args, usage);
        bool json = false;
        int timeoutMs = DefaultSampleTimeoutMs;
        double? floatTolerance = null;
        List<string> backendArgs = [];
        for (int i = source.OptionStart; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json":
                    json = true;
                    break;
                case "--timeout":
                    timeoutMs = int.TryParse(ReadOptionValue(args, ref i), NumberStyles.None, CultureInfo.InvariantCulture, out int parsedTimeout) && parsedTimeout > 0
                        ? parsedTimeout
                        : throw new InvalidOperationException($"--timeout needs a positive number of milliseconds. Usage: {usage}");
                    break;
                case "--float-tolerance":
                    floatTolerance = double.TryParse(ReadOptionValue(args, ref i), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedTolerance) && parsedTolerance >= 0
                        ? parsedTolerance
                        : throw new InvalidOperationException($"--float-tolerance needs a non-negative number. Usage: {usage}");
                    break;
                default:
                    backendArgs.Add(args[i]);
                    break;
            }
        }

        BackendOptions options = ParseBackendOptions(backendArgs.ToArray(), 0, allowOutput: false, allowStdinFile: false);
        IReadOnlyList<SampleCase> samples = FindSamples(source.SourcePath);
        Stopwatch buildWatch = Stopwatch.StartNew();
        GeneratedProgram generated = await GenerateProgramAsync(source.SourcePath, options);
        IReadOnlyList<Diagnostic> reported = ReportedDiagnostics(generated.Diagnostics);
        ProcessResult? build = HasErrors(reported) ? null : await BuildGeneratedProgramAsync(generated.Layout, options.Configuration);
        buildWatch.Stop();
        bool buildOk = build is { ExitCode: 0 };

        List<SampleResult> results = [];
        if (buildOk)
        {
            foreach (SampleCase sample in samples)
            {
                SampleResult result = await RunSampleAsync(generated.Layout, options, sample, timeoutMs, floatTolerance);
                results.Add(result);
                if (!json)
                {
                    PrintSampleResult(result);
                }
            }
        }

        if (json)
        {
            Console.WriteLine(FormatTestJson(generated, reported, buildOk, build?.Output, buildWatch.ElapsedMilliseconds, results));
        }
        else if (!buildOk)
        {
            foreach (string line in FormatDiagnostics(reported, generated.SourceText))
            {
                Console.WriteLine(line);
            }

            if (build is not null)
            {
                Console.Write(build.Output);
            }

            Console.WriteLine("Build failed.");
        }
        else if (samples.Count == 0)
        {
            Console.WriteLine($"No samples found. Put `{Path.GetFileNameWithoutExtension(source.SourcePath)}.in` (or `.<k>.in`) with a matching `.out` next to the source.");
        }
        else
        {
            int passed = results.Count(result => result.Status == "passed");
            int failed = results.Count(result => result.Status is "failed" or "error" or "timeout");
            int unchecked_ = results.Count(result => result.Status == "noExpected");
            Console.WriteLine($"{passed} passed, {failed} failed" + (unchecked_ > 0 ? $", {unchecked_} without expected output" : string.Empty));
        }

        return !buildOk ? 2 : results.Any(result => result.Status is "failed" or "error" or "timeout") ? 1 : 0;
    }

    // `name.in` / `name.out` and `name.<k>.in` / `name.<k>.out`, where `<k>` has no dot (guide §14.5).
    private static IReadOnlyList<SampleCase> FindSamples(string sourcePath)
    {
        string directory = Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();
        string name = Path.GetFileNameWithoutExtension(sourcePath);
        List<SampleCase> samples = [];
        foreach (string inputPath in Directory.EnumerateFiles(directory, name + "*.in"))
        {
            string fileName = Path.GetFileName(inputPath);
            string stem = fileName[..^".in".Length];
            if (stem != name && !(stem.StartsWith(name + ".", StringComparison.Ordinal) && stem.Length > name.Length + 1 && !stem[(name.Length + 1)..].Contains('.')))
            {
                continue;
            }

            string expectedPath = Path.Combine(directory, stem + ".out");
            samples.Add(new SampleCase(stem, inputPath, File.Exists(expectedPath) ? expectedPath : null));
        }

        samples.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
        return samples;
    }

    private static async Task<SampleResult> RunSampleAsync(SdkLayout layout, BackendOptions options, SampleCase sample, int timeoutMs, double? floatTolerance)
    {
        ProcessStartInfo startInfo = CreateBuiltProgramStartInfo(layout, options);
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        startInfo.StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        Stopwatch watch = Stopwatch.StartNew();
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        try
        {
            await using (FileStream input = File.OpenRead(sample.InputPath))
            {
                await input.CopyToAsync(process.StandardInput.BaseStream);
            }

            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The program exited without reading all of its input.
        }

        using CancellationTokenSource timeout = new(timeoutMs);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            watch.Stop();
            return new SampleResult(sample, "timeout", await output, await error, null, watch.ElapsedMilliseconds);
        }

        watch.Stop();
        string actual = await output;
        string standardError = await error;
        string status = process.ExitCode != 0
            ? "error"
            : sample.ExpectedPath is null
                ? "noExpected"
                : OutputsMatch(await File.ReadAllTextAsync(sample.ExpectedPath, Encoding.UTF8), actual, floatTolerance) ? "passed" : "failed";
        return new SampleResult(sample, status, actual, standardError, process.ExitCode, watch.ElapsedMilliseconds);
    }

    // Comparison rules (guide §14.5): `\r\n` is `\n`, trailing spaces of a line and trailing blank lines are ignored,
    // and with a tolerance two tokens that both read as numbers may differ by at most that much.
    private static bool OutputsMatch(string expected, string actual, double? floatTolerance)
    {
        string[] expectedLines = NormalizeOutput(expected);
        string[] actualLines = NormalizeOutput(actual);
        if (expectedLines.Length != actualLines.Length)
        {
            return false;
        }

        for (int i = 0; i < expectedLines.Length; i++)
        {
            if (expectedLines[i] == actualLines[i])
            {
                continue;
            }

            if (floatTolerance is not double tolerance || !TokensMatch(expectedLines[i], actualLines[i], tolerance))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] NormalizeOutput(string text)
    {
        List<string> lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => line.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines.ToArray();
    }

    private static bool TokensMatch(string expectedLine, string actualLine, double tolerance)
    {
        string[] expectedTokens = expectedLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string[] actualTokens = actualLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (expectedTokens.Length != actualTokens.Length)
        {
            return false;
        }

        for (int i = 0; i < expectedTokens.Length; i++)
        {
            if (expectedTokens[i] == actualTokens[i])
            {
                continue;
            }

            if (!double.TryParse(expectedTokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double expectedValue)
                || !double.TryParse(actualTokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double actualValue))
            {
                return false;
            }

            double difference = Math.Abs(expectedValue - actualValue);
            if (difference > tolerance && difference > tolerance * Math.Abs(expectedValue))
            {
                return false;
            }
        }

        return true;
    }

    private static void PrintSampleResult(SampleResult result)
    {
        string label = result.Status switch
        {
            "passed" => "PASS",
            "failed" => "FAIL",
            "error" => "ERROR",
            "timeout" => "TIMEOUT",
            _ => "RAN",
        };
        Console.WriteLine($"{label} {result.Sample.Name} ({result.ElapsedMs} ms)");
        if (result.Status == "failed" && result.Sample.ExpectedPath is not null)
        {
            string[] expected = NormalizeOutput(File.ReadAllText(result.Sample.ExpectedPath, Encoding.UTF8));
            string[] actual = NormalizeOutput(result.Actual);
            int line = 0;
            while (line < expected.Length && line < actual.Length && expected[line] == actual[line])
            {
                line++;
            }

            Console.WriteLine($"  line {line + 1}: expected `{(line < expected.Length ? expected[line] : "<end of output>")}`, got `{(line < actual.Length ? actual[line] : "<end of output>")}`");
        }
        else if (result.Status == "noExpected")
        {
            Console.Write(result.Actual);
        }
        else if (result.Status == "error")
        {
            Console.WriteLine($"  exit code {result.ExitCode}");
            if (result.StandardError.Length > 0)
            {
                Console.Write(Indent(result.StandardError));
            }
        }
        else if (result.Status == "timeout")
        {
            Console.WriteLine("  the program did not finish in time");
        }
    }

    private static string Indent(string text)
        => string.Concat(text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Where(line => line.Length > 0).Select(line => "  " + line + "\n"));

    // LSP `Diagnostic` objects with zero-based ranges (guide §6.6).
    private static string FormatDiagnosticsJson(IReadOnlyList<Diagnostic> diagnostics, string source, string sourcePath)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
        {
            WriteDiagnosticsJson(writer, diagnostics, source, sourcePath);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteDiagnosticsJson(Utf8JsonWriter writer, IReadOnlyList<Diagnostic> diagnostics, string source, string sourcePath)
    {
        int[] lineStarts = BuildLineStarts(source);
        string uri = new Uri(sourcePath).AbsoluteUri;
        writer.WriteStartArray();
        foreach (Diagnostic diagnostic in diagnostics)
        {
            writer.WriteStartObject();
            WriteRangeJson(writer, lineStarts, diagnostic.Span);
            writer.WriteNumber("severity", diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => 1,
                DiagnosticSeverity.Warning => 2,
                _ => 3,
            });
            writer.WriteString("code", diagnostic.EffectiveCode);
            writer.WriteString("source", "pscp");
            writer.WriteString("message", diagnostic.Message);
            if (diagnostic.RelatedSpan is TextSpan related)
            {
                writer.WriteStartArray("relatedInformation");
                writer.WriteStartObject();
                writer.WriteStartObject("location");
                writer.WriteString("uri", uri);
                WriteRangeJson(writer, lineStarts, related);
                writer.WriteEndObject();
                writer.WriteString("message", "related location");
                writer.WriteEndObject();
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteRangeJson(Utf8JsonWriter writer, int[] lineStarts, TextSpan span)
    {
        (int startLine, int startColumn) = GetLineColumn(lineStarts, span.Start);
        (int endLine, int endColumn) = GetLineColumn(lineStarts, span.Start + span.Length);
        writer.WriteStartObject("range");
        writer.WriteStartObject("start");
        writer.WriteNumber("line", startLine);
        writer.WriteNumber("character", startColumn);
        writer.WriteEndObject();
        writer.WriteStartObject("end");
        writer.WriteNumber("line", endLine);
        writer.WriteNumber("character", endColumn);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static string FormatTestJson(
        GeneratedProgram generated,
        IReadOnlyList<Diagnostic> diagnostics,
        bool buildOk,
        string? buildOutput,
        long buildElapsedMs,
        IReadOnlyList<SampleResult> results)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("build");
            writer.WriteBoolean("ok", buildOk);
            writer.WritePropertyName("diagnostics");
            WriteDiagnosticsJson(writer, diagnostics, generated.SourceText, generated.Layout.SourcePath);
            if (!buildOk && !string.IsNullOrEmpty(buildOutput))
            {
                writer.WriteString("output", buildOutput);
            }

            writer.WriteNumber("elapsedMs", buildElapsedMs);
            writer.WriteEndObject();
            writer.WriteStartArray("samples");
            foreach (SampleResult result in results)
            {
                writer.WriteStartObject();
                writer.WriteString("name", result.Sample.Name);
                writer.WriteString("input", Path.GetFileName(result.Sample.InputPath));
                if (result.Sample.ExpectedPath is null)
                {
                    writer.WriteNull("expected");
                }
                else
                {
                    writer.WriteString("expected", Path.GetFileName(result.Sample.ExpectedPath));
                }

                writer.WriteString("status", result.Status);
                writer.WriteString("actual", result.Actual);
                writer.WriteString("stderr", result.StandardError);
                if (result.ExitCode is int exitCode)
                {
                    writer.WriteNumber("exitCode", exitCode);
                }
                else
                {
                    writer.WriteNull("exitCode");
                }

                writer.WriteNumber("elapsedMs", result.ElapsedMs);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
