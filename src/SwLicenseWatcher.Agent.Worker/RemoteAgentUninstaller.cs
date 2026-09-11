using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Worker;

public interface IAgentWindowsServiceControl
{
    void StopAndDelete(string serviceName);
}

public interface IRemoteUninstallProcessStarter
{
    void StartDetached(string exePath, IReadOnlyList<string> arguments);
}

public sealed class ScAgentWindowsServiceControl : IAgentWindowsServiceControl
{
    public void StopAndDelete(string serviceName)
    {
        RunSc(["stop", serviceName], allowMissing: true);
        RunSc(["delete", serviceName], allowMissing: true);
    }

    private static void RunSc(IReadOnlyList<string> arguments, bool allowMissing)
    {
        var start = new ProcessStartInfo
        {
            FileName = "sc.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("sc.exe could not be started.");
        process.WaitForExit();
        if (process.ExitCode == 0 || allowMissing)
        {
            return;
        }

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        throw new InvalidOperationException(
            $"sc.exe {arguments[0]} failed with exit code {process.ExitCode}. {output}".Trim());
    }
}

public sealed class RemoteUninstallProcessStarter : IRemoteUninstallProcessStarter
{
    public void StartDetached(string exePath, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.SystemDirectory
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Process.Start(start)?.Dispose();
    }
}

public sealed class RemoteAgentUninstaller(
    AgentApiClient api,
    ILogger<RemoteAgentUninstaller> logger,
    IAgentWindowsServiceControl services,
    IRemoteUninstallProcessStarter processes)
{
    public const string ApplySwitch = "--apply-remote-uninstall";
    public const string InstallRootSwitch = "--install-root=";
    public const string WorkerServiceName = "SwLicenseWatcher.Agent.Worker";
    public const string WatchdogServiceName = "SwLicenseWatcher.Agent.Watchdog";

    private static readonly string ArpRegistryPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SwLicenseWatcher";

    public async Task<bool> ExecuteAsync(
        string deviceCode,
        AgentUninstallCommand command,
        CancellationToken cancellationToken)
    {
        if (!await api.ConsumeUninstallAsync(command.Id, deviceCode, command.Code, cancellationToken))
        {
            return false;
        }

        var installRoot = ResolveInstallRoot();
        logger.LogInformation(
            "Control-plane uninstall grant {Id} consumed for {DeviceCode}. Removing the agent from {InstallRoot}.",
            command.Id,
            deviceCode,
            installRoot);

        services.StopAndDelete(WatchdogServiceName);
        var helper = StageHelper();
        processes.StartDetached(helper, [ApplySwitch, InstallRootSwitch + installRoot]);
        return true;
    }

    public static bool TryApplyFromArgs(string[] args)
    {
        if (!args.Any(argument => string.Equals(argument, ApplySwitch, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var installRoot = args
            .Select(argument => argument.StartsWith(InstallRootSwitch, StringComparison.OrdinalIgnoreCase)
                ? argument[InstallRootSwitch.Length..]
                : null)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? ResolveInstallRoot();
        ApplyLocal(installRoot, new ScAgentWindowsServiceControl());
        return true;
    }

    public static void ApplyLocal(string installRoot, IAgentWindowsServiceControl services)
    {
        services.StopAndDelete(WatchdogServiceName);
        services.StopAndDelete(WorkerServiceName);
        DeleteDirectory(Path.Combine(installRoot, "Agent.Watchdog"));
        DeleteDirectory(Path.Combine(installRoot, "Agent.Worker"));
        TryDeleteFile(Path.Combine(installRoot, "SwLicenseWatcher-Setup.exe"));
        if (Directory.Exists(installRoot) && !Directory.EnumerateFileSystemEntries(installRoot).Any())
        {
            Directory.Delete(installRoot, recursive: false);
        }

        if (OperatingSystem.IsWindows())
        {
            DeleteArp();
        }
    }

    internal static string ResolveInstallRoot()
    {
        var workerDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Directory.GetParent(workerDirectory);
        return parent?.FullName
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SwLicenseWatcher");
    }

    private static string StageHelper()
    {
        var source = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "SwLicenseWatcher.Agent.Worker.exe");
        var destinationDirectory = Path.Combine(Path.GetTempPath(), "slw-uninstall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destinationDirectory);
        var destination = Path.Combine(destinationDirectory, Path.GetFileName(source));
        File.Copy(source, destination, overwrite: true);
        return destination;
    }

    private static void DeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The running helper or a locked setup copy may remain.
        }
    }

    [SupportedOSPlatform("windows")]
    private static void DeleteArp()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            Registry.LocalMachine.DeleteSubKeyTree(ArpRegistryPath, throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Best-effort; services and files are already removed.
        }
    }
}
