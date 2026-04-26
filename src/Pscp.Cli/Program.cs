using System.Diagnostics;
using System.Text;
using Pscp.Transpiler;

return await PscpCli.RunAsync(args);

static class PscpCli
{
    private const string DefaultNamespace = "Pscp.Generated";
    private const string DefaultSdkDirectoryName = ".pscp";
    private const string DefaultSdkProjectName = "Pscp.Generated.csproj";

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        string[] tail = args.Skip(1).ToArray();

        try
        {
            return command switch
            {
                "init" => await InitAsync(tail),
                "new" => await InitAsync(tail),
                "check" => await CheckAsync(tail),
                "transpile" => await TranspileAsync(tail),
                "build" => await BuildAsync(tail),
                "run" => await RunProgramAsync(tail),
                "lsp" => await RunLanguageServerAsync(tail),
                "version" => ShowVersion(),
                _ => UnknownCommand(command),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> InitAsync(string[] args)
    {
        string rootDirectory = Directory.GetCurrentDirectory();
        bool force = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--force":
                    force = true;
                    break;
                default:
                    if (args[i].StartsWith("-", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"Unknown option: {args[i]}");
                    }

                    if (rootDirectory != Directory.GetCurrentDirectory())
                    {
                        throw new InvalidOperationException("Usage: pscp init [directory] [--force]");
                    }

                    rootDirectory = Path.GetFullPath(args[i]);
                    break;
            }
        }

        Directory.CreateDirectory(rootDirectory);
        string sourcePath = Path.Combine(rootDirectory, "main.pscp");
        SdkLayout layout = CreateSdkLayout(sourcePath);

        if (!force && (File.Exists(sourcePath) || File.Exists(layout.ProjectPath)))
        {
            throw new InvalidOperationException("A PSCP project already exists here. Use `pscp init --force` to overwrite the starter files.");
        }

        await EnsureSdkLayoutAsync(layout, overwriteExisting: force, older: false);
        if (force || !File.Exists(sourcePath))
        {
            await File.WriteAllTextAsync(sourcePath, CreateStarterProgram(), Encoding.UTF8);
        }

        Console.WriteLine($"Initialized PSCP project in {rootDirectory}");
        Console.WriteLine($"Source: {sourcePath}");
        Console.WriteLine($"SDK project: {layout.ProjectPath}");
        return 0;
    }

    private static async Task<int> CheckAsync(string[] args)
    {
        SourceResolution source = ResolveSourceArgument(args, "pscp check [file.pscp]");
        string text = await File.ReadAllTextAsync(source.SourcePath, Encoding.UTF8);
        TranspilationResult result = PscpTranspiler.Transpile(text);

        if (result.Diagnostics.Count == 0)
        {
            Console.WriteLine("No diagnostics.");
            return 0;
        }

        foreach (string line in FormatDiagnostics(result.Diagnostics, text))
        {
            Console.WriteLine(line);
        }

        return HasErrors(result.Diagnostics) ? 1 : 0;
    }

    private static async Task<int> TranspileAsync(string[] args)
    {
        SourceResolution source = ResolveSourceArgument(args, "pscp transpile [file.pscp] [-o output.cs] [--print]");
        BackendOptions options = ParseBackendOptions(args, source.OptionStart, allowOutput: true, allowStdinFile: false);

        string sourceText = await File.ReadAllTextAsync(source.SourcePath, Encoding.UTF8);
        TranspilationResult result = PscpTranspiler.Transpile(
            sourceText,
            CreateTranspilationOptions(source.SourcePath, sourceText, options, suffix: null));

        foreach (string line in FormatDiagnostics(result.Diagnostics, sourceText))
        {
            Console.WriteLine(line);
        }

        if (HasErrors(result.Diagnostics))
        {
            return 1;
        }

        string outputPath = options.OutputPath ?? Path.ChangeExtension(source.SourcePath, ".g.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, result.CSharpCode, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (options.Print)
        {
            Console.WriteLine(result.CSharpCode);
        }
        else
        {
            Console.WriteLine($"Wrote C# to {outputPath}");
        }

        return 0;
    }

    private static Task<int> BuildAsync(string[] args)
        => ExecuteSdkCommandAsync(args, runAfterBuild: false);

    private static Task<int> RunProgramAsync(string[] args)
        => ExecuteSdkCommandAsync(args, runAfterBuild: true);

    private static async Task<int> ExecuteSdkCommandAsync(string[] args, bool runAfterBuild)
    {
        SourceResolution source = ResolveSourceArgument(args, runAfterBuild ? "pscp run [file.pscp]" : "pscp build [file.pscp]");
        BackendOptions options = ParseBackendOptions(args, source.OptionStart, allowOutput: false, allowStdinFile: true);

        string sourceText = await File.ReadAllTextAsync(source.SourcePath, Encoding.UTF8);
        SdkLayout layout = await EnsureSdkLayoutAsync(CreateSdkLayout(source.SourcePath), overwriteExisting: false, older: options.Older);
        TranspilationResult result = PscpTranspiler.Transpile(
            sourceText,
            CreateTranspilationOptions(source.SourcePath, sourceText, options, suffix: "Program"));

        foreach (string line in FormatDiagnostics(result.Diagnostics, sourceText))
        {
            Console.WriteLine(line);
        }

        if (HasErrors(result.Diagnostics))
        {
            return 1;
        }

        await File.WriteAllTextAsync(layout.GeneratedProgramPath, result.CSharpCode, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"Generated {layout.GeneratedProgramPath}");

        if (!runAfterBuild)
        {
            return await RunDotnetProcessAsync(
                $"build \"{layout.ProjectPath}\" -nologo -c {options.Configuration}",
                layout.RootDirectory,
                stdIn: null);
        }

        string? stdIn = options.StdinFile is not null
            ? await File.ReadAllTextAsync(options.StdinFile, Encoding.UTF8)
            : (Console.IsInputRedirected ? await Console.In.ReadToEndAsync() : null);

        return await RunDotnetProcessAsync(
            $"run --project \"{layout.ProjectPath}\" -nologo -c {options.Configuration}",
            layout.RootDirectory,
            stdIn);
    }

    private static Task<int> RunLanguageServerAsync(string[] args)
    {
        if (args.Length > 0)
        {
            throw new InvalidOperationException("Usage: pscp lsp");
        }

        ToolInvocation tool = ResolveLanguageServerTool();
        using Process process = Process.Start(new ProcessStartInfo(tool.FileName, tool.Arguments)
        {
            WorkingDirectory = tool.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        }) ?? throw new InvalidOperationException("Failed to start the PSCP language server.");

        process.WaitForExit();
        return Task.FromResult(process.ExitCode);
    }

    private static async Task<SdkLayout> EnsureSdkLayoutAsync(SdkLayout layout, bool overwriteExisting, bool older)
    {
        Directory.CreateDirectory(layout.RootDirectory);
        Directory.CreateDirectory(layout.SdkDirectory);

        string gitIgnorePath = Path.Combine(layout.SdkDirectory, ".gitignore");
        if (overwriteExisting || !File.Exists(gitIgnorePath))
        {
            await File.WriteAllTextAsync(gitIgnorePath, "bin/\nobj/\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        string desiredProject = CreateSdkProjectFile(layout, older);
        if (overwriteExisting || !File.Exists(layout.ProjectPath) || !string.Equals(await File.ReadAllTextAsync(layout.ProjectPath, Encoding.UTF8), desiredProject, StringComparison.Ordinal))
        {
            await File.WriteAllTextAsync(layout.ProjectPath, desiredProject, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        if (overwriteExisting || !File.Exists(layout.GeneratedProgramPath))
        {
            await File.WriteAllTextAsync(layout.GeneratedProgramPath, CreateGeneratedPlaceholder(layout), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return layout;
    }

    private static SourceResolution ResolveSourceArgument(string[] args, string usage)
    {
        if (args.Length == 0 || args[0].StartsWith("-", StringComparison.Ordinal))
        {
            string implicitMain = Path.GetFullPath("main.pscp");
            if (File.Exists(implicitMain))
            {
                return new SourceResolution(implicitMain, 0);
            }

            throw new InvalidOperationException($"Missing input file. Usage: {usage}");
        }

        string sourcePath = Path.GetFullPath(args[0]);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Could not find file '{sourcePath}'.");
        }

        return new SourceResolution(sourcePath, 1);
    }

    private static SdkLayout CreateSdkLayout(string sourcePath)
    {
        string rootDirectory = Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();
        string sdkDirectory = Path.Combine(rootDirectory, DefaultSdkDirectoryName);
        string projectPath = Path.Combine(sdkDirectory, DefaultSdkProjectName);
        string generatedProgramPath = Path.Combine(sdkDirectory, "Program.cs");
        string assemblyName = MakeSafeClassName(Path.GetFileName(rootDirectory));
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            assemblyName = "PscpApp";
        }

        return new SdkLayout(rootDirectory, sourcePath, sdkDirectory, projectPath, generatedProgramPath, assemblyName);
    }

    private static ToolInvocation ResolveLanguageServerTool()
    {
        string baseDirectory = AppContext.BaseDirectory;
        List<string> candidates =
        [
            Path.Combine(baseDirectory, "Pscp.LanguageServer.exe"),
            Path.Combine(baseDirectory, "Pscp.LanguageServer.dll"),
        ];

        string? repositoryRoot = TryFindRepositoryRoot();
        if (repositoryRoot is not null)
        {
            candidates.AddRange(
            [
                Path.Combine(repositoryRoot, "src", "Pscp.LanguageServer", "bin", "Debug", "net10.0", "Pscp.LanguageServer.exe"),
                Path.Combine(repositoryRoot, "src", "Pscp.LanguageServer", "bin", "Debug", "net10.0", "Pscp.LanguageServer.dll"),
                Path.Combine(repositoryRoot, "src", "Pscp.LanguageServer", "bin", "Release", "net10.0", "Pscp.LanguageServer.exe"),
                Path.Combine(repositoryRoot, "src", "Pscp.LanguageServer", "bin", "Release", "net10.0", "Pscp.LanguageServer.dll"),
            ]);
        }

        foreach (string candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            if (string.Equals(Path.GetExtension(candidate), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                return new ToolInvocation(candidate, string.Empty, Path.GetDirectoryName(candidate)!);
            }

            return new ToolInvocation("dotnet", $"\"{candidate}\"", Path.GetDirectoryName(candidate)!);
        }

        if (repositoryRoot is not null)
        {
            string serverProject = Path.Combine(repositoryRoot, "src", "Pscp.LanguageServer", "Pscp.LanguageServer.csproj");
            if (File.Exists(serverProject))
            {
                return new ToolInvocation("dotnet", $"run --project \"{serverProject}\" --", repositoryRoot);
            }
        }

        throw new InvalidOperationException("PSCP language server was not found. Build or install the language server first.");
    }

    private static async Task<int> RunDotnetProcessAsync(string arguments, string workingDirectory, string? stdIn)
    {
        ProcessStartInfo startInfo = new("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = stdIn is not null,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        startInfo.Environment["DOTNET_CLI_HOME"] = ResolveDotnetCliHome();
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet.");

        if (stdIn is not null)
        {
            await process.StandardInput.WriteAsync(stdIn);
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static IEnumerable<string> FormatDiagnostics(IReadOnlyList<Diagnostic> diagnostics, string source)
    {
        int[] lineStarts = BuildLineStarts(source);
        foreach (Diagnostic diagnostic in diagnostics)
        {
            (int line, int column) = GetLineColumn(lineStarts, diagnostic.Span.Start);
            string severity = diagnostic.Severity == DiagnosticSeverity.Error ? "error" : "warning";
            yield return $"{line + 1}:{column + 1}: {severity}: {diagnostic.Message}";
        }
    }

    private static int[] BuildLineStarts(string text)
    {
        List<int> starts = [0];
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        return starts.ToArray();
    }

    private static (int line, int column) GetLineColumn(int[] lineStarts, int offset)
    {
        int line = Array.BinarySearch(lineStarts, offset);
        if (line < 0)
        {
            line = ~line - 1;
        }

        int column = offset - lineStarts[Math.Max(line, 0)];
        return (Math.Max(line, 0), Math.Max(column, 0));
    }

    private static string CreateSdkProjectFile(SdkLayout layout, bool older)
        => $$"""
           <Project Sdk="Microsoft.NET.Sdk">
             <PropertyGroup>
               <OutputType>Exe</OutputType>
               <TargetFramework>{{(older ? "net6.0" : "net10.0")}}</TargetFramework>
               {{(older ? "<LangVersion>10.0</LangVersion>" : string.Empty)}}
               <ImplicitUsings>enable</ImplicitUsings>
               <Nullable>enable</Nullable>
               <AssemblyName>{{layout.AssemblyName}}</AssemblyName>
               <RootNamespace>{{DefaultNamespace}}</RootNamespace>
             </PropertyGroup>
           </Project>
           """;

    private static string CreateGeneratedPlaceholder(SdkLayout layout)
        => $$"""
           // Generated file placeholder for {{Path.GetFileName(layout.SourcePath)}}.
           // `pscp build` or `pscp run` will overwrite this file.
           using System;

           namespace {{DefaultNamespace}};

           internal static class Placeholder
           {
               private static void Main()
               {
                   Console.WriteLine("Run `pscp build` or `pscp run` to generate this project.");
               }
           }
           """;

    private static string CreateStarterProgram()
        => """
           // PSCP starter program
           int n =
           int[] values = stdin.readArray<int>(n)
           var total = 0
           for value in values do total += value
           += total
           """;

    private static string MakeSafeClassName(string name)
    {
        string cleaned = new(name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return "GeneratedProgram";
        }

        if (!char.IsLetter(cleaned[0]) && cleaned[0] != '_')
        {
            cleaned = "_" + cleaned;
        }

        return cleaned;
    }

    private static string ResolveDotnetCliHome()
    {
        string? repositoryRoot = TryFindRepositoryRoot();
        if (repositoryRoot is not null)
        {
            return Path.Combine(repositoryRoot, ".dotnet");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pscp", ".dotnet");
    }

    private static string? TryFindRepositoryRoot()
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

        return null;
    }

    private static int ShowVersion()
    {
        Console.WriteLine($"pscp CLI {PscpVersionInfo.ToolVersion} (language {PscpVersionInfo.LanguageVersion})");
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintHelp();
        return 1;
    }

    private static bool IsHelp(string arg)
        => arg is "help" or "--help" or "-h";

    private static void PrintHelp()
    {
        Console.WriteLine("pscp CLI");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  pscp init [directory] [--force]");
        Console.WriteLine("  pscp check [file.pscp]");
        Console.WriteLine("  pscp transpile [file.pscp] [-o output.cs] [--print] [--namespace N] [--class-name C] [--compact|--verbose] [--pretty] [--explain] [--older]");
        Console.WriteLine("  pscp build [file.pscp] [-c Debug|Release] [--release] [--debug] [--namespace N] [--class-name C] [--compact|--verbose] [--pretty] [--explain] [--older]");
        Console.WriteLine("  pscp run [file.pscp] [--stdin-file input.txt] [-c Debug|Release] [--release] [--debug] [--namespace N] [--class-name C] [--compact|--verbose] [--pretty] [--explain] [--older]");
        Console.WriteLine("  pscp lsp");
        Console.WriteLine("  pscp version");
        Console.WriteLine();
        Console.WriteLine("When `file.pscp` is omitted, `main.pscp` in the current directory is used if present.");
        Console.WriteLine("`--compact` is the default. Use `--verbose` to keep the full helper surface in generated C#.");
    }

    private sealed record SourceResolution(string SourcePath, int OptionStart);
    private sealed record SdkLayout(string RootDirectory, string SourcePath, string SdkDirectory, string ProjectPath, string GeneratedProgramPath, string AssemblyName);
    private sealed record ToolInvocation(string FileName, string Arguments, string WorkingDirectory);
    private sealed record BackendOptions(
        string? OutputPath,
        bool Print,
        string? Namespace,
        string? ClassName,
        string Configuration,
        string? StdinFile,
        HelperEmissionMode HelperEmission,
        bool Explain,
        bool Older,
        bool Pretty);

    private static BackendOptions ParseBackendOptions(string[] args, int startIndex, bool allowOutput, bool allowStdinFile)
    {
        string? outputPath = null;
        bool print = false;
        string? ns = null;
        string? className = null;
        string configuration = "Debug";
        string? stdinFile = null;
        HelperEmissionMode helperEmission = HelperEmissionMode.Compact;
        bool explain = false;
        bool older = false;
        bool pretty = false;

        for (int i = startIndex; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o":
                case "--output":
                    if (!allowOutput)
                    {
                        throw new InvalidOperationException($"Unknown option: {args[i]}");
                    }

                    outputPath = Path.GetFullPath(args[++i]);
                    break;
                case "--print":
                    if (!allowOutput)
                    {
                        throw new InvalidOperationException($"Unknown option: {args[i]}");
                    }

                    print = true;
                    break;
                case "--stdin-file":
                    if (!allowStdinFile)
                    {
                        throw new InvalidOperationException($"Unknown option: {args[i]}");
                    }

                    stdinFile = Path.GetFullPath(args[++i]);
                    break;
                case "-c":
                case "--configuration":
                    configuration = args[++i];
                    break;
                case "--release":
                    configuration = "Release";
                    break;
                case "--debug":
                    configuration = "Debug";
                    break;
                case "--namespace":
                    ns = args[++i];
                    break;
                case "--class-name":
                    className = args[++i];
                    break;
                case "--compact":
                    helperEmission = HelperEmissionMode.Compact;
                    break;
                case "--verbose":
                    helperEmission = HelperEmissionMode.Verbose;
                    break;
                case "--explain":
                    explain = true;
                    break;
                case "--pretty":
                    pretty = true;
                    break;
                case "--older":
                    older = true;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown option: {args[i]}");
            }
        }

        return new BackendOptions(outputPath, print, ns, className, configuration, stdinFile, helperEmission, explain, older, pretty);
    }

    private static TranspilationOptions CreateTranspilationOptions(string sourcePath, string sourceText, BackendOptions options, string? suffix)
        => new(
            options.Namespace ?? DefaultNamespace,
            (options.ClassName ?? MakeSafeClassName(Path.GetFileNameWithoutExtension(sourcePath))) + (suffix ?? string.Empty),
            options.HelperEmission,
            options.Explain,
            options.Older,
            options.Explain ? sourceText : null,
            options.Pretty);

    private static bool HasErrors(IReadOnlyList<Diagnostic> diagnostics)
        => diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
