using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace Pscp.Installer;

internal static class Program
{
    public static Task<int> Main(string[] args)
        => PscpInstaller.RunAsync(args);
}

internal static class PscpInstaller
{
    private const string AppName = "Pscp";
    private const string DisplayName = "PSCP SDK";
    private const string PayloadResourceName = "PscpInstallerPayloadZip";
    private const string UninstallRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Pscp";
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmRead = 0x0010;
    private const int ProcessCommandLineInformationClass = 60;

    private const int RestartManagerErrorMoreData = 234;
    private const int CchRmMaxAppName = 255;
    private const int CchRmMaxServiceName = 63;
    private static readonly HashSet<string> ManagedExecutableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "pscp.exe",
        "Pscp.LanguageServer.exe",
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("PSCP installer is only supported on Windows.");
            return 1;
        }

        InstallerOptions options = ParseOptions(args);
        bool showUiHere = ShouldShowUiInCurrentProcess(options);
        bool preferWorkerUi = options.Uninstall && !options.UninstallWorker && !options.NoUi && Environment.UserInteractive;

        if (showUiHere)
        {
            return await InstallerUiHost.RunAsync(GetWindowTitle(options), sink => ExecuteAsync(options, sink));
        }

        if (!options.UninstallWorker && !preferWorkerUi)
        {
            TryAttachParentConsole();
        }

        IInstallerStatusSink sink = options.UninstallWorker || preferWorkerUi
            ? NullInstallerStatusSink.Instance
            : new ConsoleInstallerStatusSink();

        try
        {
            return await ExecuteAsync(options, sink);
        }
        catch (Exception ex)
        {
            sink.Error(ex.Message);
            return 1;
        }
    }

    private static Task<int> ExecuteAsync(InstallerOptions options, IInstallerStatusSink sink)
        => options.UninstallWorker
            ? UninstallWorkerAsync(options.InstallDirectory, options.WaitForPid, options.StagingDirectory, sink)
            : options.Uninstall
                ? UninstallAsync(options.InstallDirectory, sink, showUiInWorker: !options.NoUi && Environment.UserInteractive)
                : InstallAsync(options.InstallDirectory, sink, options.NoIntegrate);

    private static InstallerOptions ParseOptions(string[] args)
    {
        bool uninstall = false;
        bool uninstallWorker = false;
        bool noUi = false;
        bool noIntegrate = false;
        bool showUi = false;
        string installDirectory = GetDefaultInstallDirectory();
        string? stagingDirectory = null;
        int waitForPid = 0;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--uninstall":
                    uninstall = true;
                    break;
                case "--uninstall-worker":
                    uninstallWorker = true;
                    break;
                case "--install-dir":
                    installDirectory = Path.GetFullPath(ReadRequiredValue(args, ref i, "--install-dir"));
                    break;
                case "--staging-dir":
                    stagingDirectory = Path.GetFullPath(ReadRequiredValue(args, ref i, "--staging-dir"));
                    break;
                case "--wait-pid":
                    waitForPid = int.Parse(ReadRequiredValue(args, ref i, "--wait-pid"), CultureInfo.InvariantCulture);
                    break;
                case "--no-ui":
                case "--quiet":
                    noUi = true;
                    break;
                case "--no-integrate":
                    noIntegrate = true;
                    break;
                case "--show-ui":
                    showUi = true;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown option: {args[i]}");
            }
        }

        return new InstallerOptions(uninstall, uninstallWorker, noUi, noIntegrate, showUi, installDirectory, stagingDirectory, waitForPid);
    }

    private static bool ShouldShowUiInCurrentProcess(InstallerOptions options)
        => !options.NoUi
            && Environment.UserInteractive
            && (!options.Uninstall || (options.UninstallWorker && options.ShowUi));

    private static string GetWindowTitle(InstallerOptions options)
        => options.Uninstall ? "PSCP SDK Removal" : "PSCP SDK Setup";

    private static async Task<int> InstallAsync(string installDirectory, IInstallerStatusSink sink, bool noIntegrate)
    {
        installDirectory = NormalizePath(installDirectory);
        string tempDirectory = Path.Combine(Path.GetTempPath(), "pscp-installer", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(tempDirectory);

        sink.Info($"Installing {DisplayName}");
        sink.Info($"Target directory: {installDirectory}");

        try
        {
            sink.Info("Extracting installation payload...");
            await ExtractPayloadAsync(tempDirectory);

            if (Directory.Exists(installDirectory))
            {
                await EnsureInstallDirectoryUnlockedAsync(installDirectory, sink, [Environment.ProcessId], throwOnFailure: true);
            }

            Directory.CreateDirectory(installDirectory);
            sink.Info("Copying installation files...");
            await CopyDirectoryWithRetriesAsync(tempDirectory, installDirectory, sink);

            string currentExe = Environment.ProcessPath
                ?? throw new InvalidOperationException("Could not resolve the installer executable path.");
            string uninstallExe = Path.Combine(installDirectory, "uninstall.exe");
            File.Copy(currentExe, uninstallExe, overwrite: true);
            CopyUninstallerSupportFiles(currentExe, installDirectory);

            WriteInstallManifest(installDirectory);

            bool pathUpdated = false;
            bool uninstallerRegistered = false;
            if (!noIntegrate)
            {
                pathUpdated = TryRunWindowsIntegration(sink, "add PSCP to PATH", () => AddToUserPath(installDirectory));
                uninstallerRegistered = TryRunWindowsIntegration(sink, "register PSCP for uninstall", () => WriteUninstallRegistration(installDirectory, uninstallExe));
                if (pathUpdated || uninstallerRegistered)
                {
                    BroadcastEnvironmentChange();
                }
            }

            sink.Info($"Installed {DisplayName} to {installDirectory}");
            if (pathUpdated)
            {
                sink.Info("You may need to reopen terminals for PATH changes to appear.");
            }
            else if (noIntegrate)
            {
                sink.Info("Skipped PATH and uninstall registration.");
            }

            return 0;
        }
        finally
        {
            _ = TryDeleteDirectory(tempDirectory, attempts: 5, delayMilliseconds: 100);
        }
    }

    private static async Task<int> UninstallAsync(string installDirectory, IInstallerStatusSink sink, bool showUiInWorker)
    {
        installDirectory = NormalizePath(installDirectory);
        sink.Info($"Preparing to remove {DisplayName}");
        sink.Info($"Install directory: {installDirectory}");

        bool pathRemoved = TryRunWindowsIntegration(sink, "remove PSCP from PATH", () => RemoveFromUserPath(installDirectory));
        bool registrationRemoved = TryRunWindowsIntegration(sink, "remove PSCP uninstall registration", DeleteUninstallRegistration);
        if (pathRemoved || registrationRemoved)
        {
            BroadcastEnvironmentChange();
        }

        if (!Directory.Exists(installDirectory))
        {
            sink.Info($"{DisplayName} is not installed in {installDirectory}.");
            return 0;
        }

        await EnsureInstallDirectoryUnlockedAsync(installDirectory, sink, [Environment.ProcessId], throwOnFailure: true);
        sink.Info("Starting cleanup worker...");
        StartUninstallWorker(installDirectory, showUiInWorker);
        sink.Info("Cleanup will continue in the background.");
        if (pathRemoved)
        {
            sink.Info("You may need to reopen terminals for PATH changes to disappear.");
        }

        return 0;
    }

    private static async Task<int> UninstallWorkerAsync(string installDirectory, int waitForPid, string? stagingDirectory, IInstallerStatusSink sink)
    {
        installDirectory = NormalizePath(installDirectory);

        if (waitForPid > 0)
        {
            sink.Info("Waiting for the active uninstaller to exit...");
            TryWaitForProcessExit(waitForPid);
        }

        if (!Directory.Exists(installDirectory))
        {
            if (!string.IsNullOrWhiteSpace(stagingDirectory) && Directory.Exists(stagingDirectory))
            {
                ScheduleDeleteDirectory(stagingDirectory);
            }

            sink.Info("Removal completed.");
            return 0;
        }

        sink.Info("Stopping running PSCP processes...");
        await EnsureInstallDirectoryUnlockedAsync(installDirectory, sink, [Environment.ProcessId], throwOnFailure: false);
        sink.Info("Removing installed files...");

        for (int attempt = 1; attempt <= 80; attempt++)
        {
            if (TryDeleteDirectory(installDirectory, attempts: 1, delayMilliseconds: 0))
            {
                if (!string.IsNullOrWhiteSpace(stagingDirectory) && Directory.Exists(stagingDirectory))
                {
                    ScheduleDeleteDirectory(stagingDirectory);
                }

                sink.Info("Removal completed.");
                return 0;
            }

            await EnsureInstallDirectoryUnlockedAsync(installDirectory, sink, [Environment.ProcessId], throwOnFailure: false);
            if (attempt == 1 || attempt % 10 == 0)
            {
                sink.Warning("Some files are still in use. Retrying removal...");
            }

            await Task.Delay(250);
        }

        sink.Error($"Could not fully remove {installDirectory}.");
        if (!string.IsNullOrWhiteSpace(stagingDirectory) && Directory.Exists(stagingDirectory))
        {
            ScheduleDeleteDirectory(stagingDirectory);
        }

        return 1;
    }

    private static async Task ExtractPayloadAsync(string destinationDirectory)
    {
        await using Stream payloadStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName)
            ?? throw new InvalidOperationException("Installer payload is missing. Build the installer with installer\\Build-Installer.ps1.");
        using ZipArchive archive = new(payloadStream, ZipArchiveMode.Read);
        archive.ExtractToDirectory(destinationDirectory, overwriteFiles: true);
    }

    private static async Task CopyDirectoryWithRetriesAsync(string sourceDirectory, string destinationDirectory, IInstallerStatusSink sink)
    {
        const int MaxAttempts = 6;
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                CopyDirectory(sourceDirectory, destinationDirectory);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < MaxAttempts)
            {
                sink.Warning($"Copy attempt {attempt} failed because files were busy. Retrying...");
                await EnsureInstallDirectoryUnlockedAsync(destinationDirectory, sink, [Environment.ProcessId], throwOnFailure: false);
                await Task.Delay(300);
            }
        }

        CopyDirectory(sourceDirectory, destinationDirectory);
    }

    private static async Task EnsureInstallDirectoryUnlockedAsync(string installDirectory, IInstallerStatusSink sink, IReadOnlyCollection<int> ignoredPids, bool throwOnFailure)
    {
        if (!Directory.Exists(installDirectory))
        {
            return;
        }

        List<ManagedProcessInfo> running = CollectLockingProcesses(installDirectory, ignoredPids);
        if (running.Count == 0)
        {
            return;
        }

        sink.Info($"Stopping {running.Count} running PSCP process(es)...");
        foreach (ManagedProcessInfo process in running)
        {
            await TryStopProcessAsync(process, sink);
        }

        List<ManagedProcessInfo> remaining = CollectLockingProcesses(installDirectory, ignoredPids);
        if (remaining.Count == 0)
        {
            return;
        }

        string message = "Could not stop these PSCP processes: "
            + string.Join(", ", remaining.Select(process => $"{process.FileName} (PID {process.ProcessId})"));
        if (throwOnFailure)
        {
            throw new InvalidOperationException(message);
        }

        sink.Warning(message);
    }


    private static List<ManagedProcessInfo> CollectLockingProcesses(string installDirectory, IReadOnlyCollection<int> ignoredPids)
    {
        Dictionary<int, ManagedProcessInfo> processes = new();
        foreach (ManagedProcessInfo process in FindManagedProcesses(installDirectory, ignoredPids))
        {
            processes[process.ProcessId] = process;
        }

        foreach (ManagedProcessInfo process in FindRestartManagerProcesses(installDirectory, ignoredPids))
        {
            processes[process.ProcessId] = process;
        }

        foreach (ManagedProcessInfo process in FindHostedDotnetProcesses(installDirectory, ignoredPids))
        {
            processes[process.ProcessId] = process;
        }

        return processes.Values.OrderBy(process => process.ProcessId).ToList();
    }

    private static IEnumerable<ManagedProcessInfo> FindManagedProcesses(string installDirectory, IReadOnlyCollection<int> ignoredPids)
    {
        string normalizedInstallDirectory = NormalizePath(installDirectory);
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                if (ignoredPids.Contains(process.Id))
                {
                    continue;
                }

                string? processPath = TryGetProcessPath(process);
                if (string.IsNullOrWhiteSpace(processPath))
                {
                    continue;
                }

                string fileName = Path.GetFileName(processPath);
                if (!ManagedExecutableNames.Contains(fileName))
                {
                    continue;
                }

                if (!IsPathInsideDirectory(processPath, normalizedInstallDirectory))
                {
                    continue;
                }

                yield return new ManagedProcessInfo(process.Id, fileName, NormalizePath(processPath));
            }
        }
    }



    private static IReadOnlyList<ManagedProcessInfo> FindHostedDotnetProcesses(string installDirectory, IReadOnlyCollection<int> ignoredPids)
    {
        string normalizedInstallDirectory = NormalizePath(installDirectory);
        List<ManagedProcessInfo> processes = [];

        foreach (Process process in Process.GetProcessesByName("dotnet"))
        {
            using (process)
            {
                if (ignoredPids.Contains(process.Id))
                {
                    continue;
                }

                string? commandLine = TryGetProcessCommandLine(process);
                if (string.IsNullOrWhiteSpace(commandLine)
                    || commandLine.IndexOf(normalizedInstallDirectory, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                string? processPath = TryGetProcessPath(process);
                string fullPath = string.IsNullOrWhiteSpace(processPath) ? "dotnet.exe" : NormalizePath(processPath);
                processes.Add(new ManagedProcessInfo(process.Id, "dotnet.exe", fullPath));
            }
        }

        return processes;
    }

    private static IReadOnlyList<ManagedProcessInfo> FindRestartManagerProcesses(string installDirectory, IReadOnlyCollection<int> ignoredPids)
    {
        if (!Directory.Exists(installDirectory))
        {
            return [];
        }

        string[] resources = GetRestartManagerResources(installDirectory);
        if (resources.Length == 0)
        {
            return [];
        }

        uint sessionHandle = 0;
        string sessionKey = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        if (RmStartSession(out sessionHandle, 0, sessionKey) != 0)
        {
            return [];
        }

        try
        {
            if (RmRegisterResources(sessionHandle, (uint)resources.Length, resources, 0, null, 0, null) != 0)
            {
                return [];
            }

            uint needed = 0;
            uint count = 0;
            uint rebootReasons = 0;
            int result = RmGetList(sessionHandle, out needed, ref count, null, ref rebootReasons);
            if (result == 0 || needed == 0)
            {
                return [];
            }

            if (result != RestartManagerErrorMoreData)
            {
                return [];
            }

            RmProcessInfo[] processInfos = new RmProcessInfo[needed];
            count = needed;
            if (RmGetList(sessionHandle, out needed, ref count, processInfos, ref rebootReasons) != 0)
            {
                return [];
            }

            List<ManagedProcessInfo> processes = [];
            for (int i = 0; i < count; i++)
            {
                int processId = processInfos[i].Process.dwProcessId;
                if (ignoredPids.Contains(processId))
                {
                    continue;
                }

                string? processPath = null;
                try
                {
                    using Process process = Process.GetProcessById(processId);
                    processPath = TryGetProcessPath(process);
                }
                catch (ArgumentException)
                {
                }

                string displayPath = string.IsNullOrWhiteSpace(processPath)
                    ? processInfos[i].strAppName
                    : NormalizePath(processPath);
                string fileName = string.IsNullOrWhiteSpace(processPath)
                    ? (string.IsNullOrWhiteSpace(processInfos[i].strAppName) ? $"PID {processId}" : processInfos[i].strAppName)
                    : Path.GetFileName(processPath);
                processes.Add(new ManagedProcessInfo(processId, fileName, displayPath));
            }

            return processes;
        }
        finally
        {
            _ = RmEndSession(sessionHandle);
        }
    }

    private static string[] GetRestartManagerResources(string installDirectory)
    {
        List<string> resources = [installDirectory];
        resources.AddRange(Directory.EnumerateFiles(installDirectory, "*", SearchOption.TopDirectoryOnly));
        return resources
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(256)
            .ToArray();
    }

    private static async Task TryStopProcessAsync(ManagedProcessInfo processInfo, IInstallerStatusSink sink)
    {
        try
        {
            using Process process = Process.GetProcessById(processInfo.ProcessId);
            if (process.HasExited)
            {
                return;
            }

            sink.Info($"Stopping {processInfo.FileName} (PID {processInfo.ProcessId})...");

            bool exited = false;
            try
            {
                if (process.CloseMainWindow())
                {
                    exited = await WaitForExitAsync(process, TimeSpan.FromSeconds(2));
                }
            }
            catch (InvalidOperationException)
            {
                exited = true;
            }
            catch (Win32Exception)
            {
            }

            if (!exited && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                exited = await WaitForExitAsync(process, TimeSpan.FromSeconds(5));
            }

            if (exited || process.HasExited)
            {
                sink.Info($"Stopped {processInfo.FileName}.");
            }
        }
        catch (ArgumentException)
        {
        }
        catch (Exception ex)
        {
            sink.Warning($"Failed to stop {processInfo.FileName} (PID {processInfo.ProcessId}): {ex.Message}");
        }
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        using CancellationTokenSource cts = new(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return process.HasExited;
        }
    }

    private static string? TryGetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
        }
        catch (NotSupportedException)
        {
            return null;
        }

        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, process.Id);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            StringBuilder builder = new(1024);
            int capacity = builder.Capacity;
            return QueryFullProcessImageName(handle, 0, builder, ref capacity)
                ? builder.ToString(0, capacity)
                : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static string? TryGetProcessCommandLine(Process process)
    {
        IntPtr handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, process.Id);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            int bufferLength = 0;
            _ = NtQueryInformationProcess(handle, ProcessCommandLineInformationClass, IntPtr.Zero, 0, out bufferLength);
            if (bufferLength <= 0)
            {
                return null;
            }

            IntPtr buffer = Marshal.AllocHGlobal(bufferLength);
            try
            {
                int status = NtQueryInformationProcess(handle, ProcessCommandLineInformationClass, buffer, bufferLength, out bufferLength);
                if (status < 0)
                {
                    return null;
                }

                UnicodeString commandLine = Marshal.PtrToStructure<UnicodeString>(buffer);
                if (commandLine.Length == 0 || commandLine.Buffer == IntPtr.Zero)
                {
                    return string.Empty;
                }

                return Marshal.PtrToStringUni(commandLine.Buffer, commandLine.Length / 2);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static bool IsPathInsideDirectory(string path, string directory)
    {
        string normalizedPath = NormalizePath(path);
        string normalizedDirectory = NormalizePath(directory);
        return normalizedPath.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteInstallManifest(string installDirectory)
    {
        string version = GetDisplayVersion();
        string manifestPath = Path.Combine(installDirectory, "install.json");
        string json = $$"""
            {
              "name": "{{DisplayName}}",
              "version": "{{version}}",
              "installedAtUtc": "{{DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)}}"
            }
            """;
        File.WriteAllText(manifestPath, json);
    }

    private static void WriteUninstallRegistration(string installDirectory, string uninstallExe)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallRegistryPath)
            ?? throw new InvalidOperationException("Failed to create the uninstall registry entry.");

        key.SetValue("DisplayName", DisplayName);
        key.SetValue("DisplayVersion", GetDisplayVersion());
        key.SetValue("Publisher", "PSCP");
        key.SetValue("InstallLocation", installDirectory);
        key.SetValue("DisplayIcon", Path.Combine(installDirectory, "pscp.exe"));
        key.SetValue("UninstallString", $"\"{uninstallExe}\" --uninstall --install-dir \"{installDirectory}\"");
        key.SetValue("QuietUninstallString", $"\"{uninstallExe}\" --uninstall --install-dir \"{installDirectory}\" --no-ui");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", Math.Max(1, ComputeInstalledSizeInKilobytes(installDirectory)), RegistryValueKind.DWord);
    }

    private static void DeleteUninstallRegistration()
        => Registry.CurrentUser.DeleteSubKeyTree(UninstallRegistryPath, throwOnMissingSubKey: false);

    private static void AddToUserPath(string installDirectory)
    {
        string current = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? string.Empty;
        List<string> entries = current.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (entries.Any(entry => string.Equals(NormalizePath(entry), NormalizePath(installDirectory), StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        entries.Add(installDirectory);
        Environment.SetEnvironmentVariable("Path", string.Join(';', entries), EnvironmentVariableTarget.User);
    }

    private static void RemoveFromUserPath(string installDirectory)
    {
        string current = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? string.Empty;
        List<string> entries = current
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(entry => !string.Equals(NormalizePath(entry), NormalizePath(installDirectory), StringComparison.OrdinalIgnoreCase))
            .ToList();
        Environment.SetEnvironmentVariable("Path", string.Join(';', entries), EnvironmentVariableTarget.User);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (string file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, file);
            string target = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void CopyUninstallerSupportFiles(string currentExe, string installDirectory)
    {
        string setupDirectory = Path.GetDirectoryName(currentExe)
            ?? throw new InvalidOperationException("Could not resolve the installer directory.");

        foreach (string fileName in new[] { "PscpSetup.dll", "PscpSetup.deps.json", "PscpSetup.runtimeconfig.json" })
        {
            string source = Path.Combine(setupDirectory, fileName);
            if (!File.Exists(source))
            {
                continue;
            }

            File.Copy(source, Path.Combine(installDirectory, fileName), overwrite: true);
        }
    }

    private static int ComputeInstalledSizeInKilobytes(string directory)
        => Directory.Exists(directory)
            ? (int)Math.Ceiling(Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length) / 1024d)
            : 0;

    private static bool TryDeleteDirectory(string directory, int attempts = 1, int delayMilliseconds = 0)
    {
        if (!Directory.Exists(directory))
        {
            return true;
        }

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                ClearReadOnlyAttributes(directory);
                Directory.Delete(directory, recursive: true);
                if (!Directory.Exists(directory))
                {
                    return true;
                }
            }
            catch when (attempt + 1 < attempts)
            {
            }

            if (delayMilliseconds > 0)
            {
                Thread.Sleep(delayMilliseconds);
            }
        }

        return !Directory.Exists(directory);
    }

    private static void ScheduleDeleteDirectory(string directory)
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), $"pscp-uninstall-{Guid.NewGuid():N}.cmd");
        string script = $"""
            @echo off
            set "TARGET={directory}"
            ping 127.0.0.1 -n 3 > nul
            :retry
            rmdir /s /q "%TARGET%"
            if exist "%TARGET%" (
                ping 127.0.0.1 -n 2 > nul
                goto retry
            )
            del /f /q "%~f0"
            """;
        File.WriteAllText(scriptPath, script, Encoding.ASCII);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    private static void StartUninstallWorker(string installDirectory, bool showUi)
    {
        string currentExe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not resolve the installer executable path.");
        string sourceDirectory = Path.GetDirectoryName(currentExe)
            ?? throw new InvalidOperationException("Could not resolve the installer directory.");
        string stagingDirectory = Path.Combine(Path.GetTempPath(), "pscp-uninstall", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(stagingDirectory);

        foreach (string fileName in EnumerateWorkerFiles(currentExe))
        {
            string source = Path.Combine(sourceDirectory, fileName);
            if (!File.Exists(source))
            {
                continue;
            }

            File.Copy(source, Path.Combine(stagingDirectory, fileName), overwrite: true);
        }

        string workerExe = Path.Combine(stagingDirectory, Path.GetFileName(currentExe));
        string uiArgument = showUi ? " --show-ui" : " --no-ui";
        using Process process = Process.Start(new ProcessStartInfo(workerExe, $"--uninstall-worker --install-dir \"{installDirectory}\" --wait-pid {Environment.ProcessId.ToString(CultureInfo.InvariantCulture)} --staging-dir \"{stagingDirectory}\"{uiArgument}")
        {
            WorkingDirectory = stagingDirectory,
            UseShellExecute = false,
            CreateNoWindow = !showUi,
            WindowStyle = showUi ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden,
        }) ?? throw new InvalidOperationException("Failed to start the uninstall worker.");
    }

    private static IEnumerable<string> EnumerateWorkerFiles(string currentExe)
    {
        HashSet<string> fileNames = new(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFileName(currentExe),
            "PscpSetup.dll",
            "PscpSetup.deps.json",
            "PscpSetup.runtimeconfig.json",
        };

        return fileNames;
    }

    private static void ClearReadOnlyAttributes(string directory)
    {
        try
        {
            File.SetAttributes(directory, FileAttributes.Normal);
        }
        catch
        {
        }

        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string path in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
            }
            catch
            {
            }
        }
    }

    private static bool TryRunWindowsIntegration(IInstallerStatusSink sink, string description, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            sink.Warning($"Could not {description}: {ex.Message}");
            return false;
        }
    }

    private static void TryWaitForProcessExit(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            process.WaitForExit();
        }
        catch (ArgumentException)
        {
        }
    }

    private static void TryAttachParentConsole()
    {
        if (GetConsoleWindow() != IntPtr.Zero)
        {
            return;
        }

        const int AttachParentProcess = -1;
        _ = AttachConsole(AttachParentProcess);
    }

    private static string ReadRequiredValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new InvalidOperationException($"Missing value for {optionName}.");
        }

        return args[++index];
    }

    private static string NormalizePath(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)));

    private static string GetDisplayVersion()
        => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "0.1.0";

    private static string GetDefaultInstallDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);

    private static void BroadcastEnvironmentChange()
    {
        const int HWND_BROADCAST = 0xffff;
        const int WM_SETTINGCHANGE = 0x001A;
        const int SMTO_ABORTIFHUNG = 0x0002;
        _ = SendMessageTimeout((IntPtr)HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "Environment", SMTO_ABORTIFHUNG, 5000, out _);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    private enum RmAppType
    {
        RmUnknownApp = 0,
        RmMainWindow = 1,
        RmOtherWindow = 2,
        RmService = 3,
        RmExplorer = 4,
        RmConsole = 5,
        RmCritical = 1000,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxAppName + 1)]
        public string strAppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxServiceName + 1)]
        public string strServiceShortName;

        public RmAppType ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        IntPtr processInformation,
        int processInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder text, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint sessionHandle, int sessionFlags, string sessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint sessionHandle,
        uint fileCount,
        string[] resources,
        uint applicationCount,
        RmUniqueProcess[]? applications,
        uint serviceCount,
        string[]? services);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmGetList(
        uint sessionHandle,
        out uint processInfoNeeded,
        ref uint processInfoCount,
        [In, Out] RmProcessInfo[]? processInfos,
        ref uint rebootReasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sessionHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        int msg,
        IntPtr wParam,
        string lParam,
        int fuFlags,
        int uTimeout,
        out IntPtr lpdwResult);

    private sealed record InstallerOptions(
        bool Uninstall,
        bool UninstallWorker,
        bool NoUi,
        bool NoIntegrate,
        bool ShowUi,
        string InstallDirectory,
        string? StagingDirectory,
        int WaitForPid);

    private sealed record ManagedProcessInfo(int ProcessId, string FileName, string FullPath);
}


