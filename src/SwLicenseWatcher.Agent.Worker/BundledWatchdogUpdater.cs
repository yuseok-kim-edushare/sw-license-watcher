using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Worker;

internal interface IWatchdogServiceControl
{
    void StopForReplacement(string serviceName, string installDirectory);

    void Start(string serviceName);
}

internal static class BundledWatchdogUpdater
{
    public static readonly TimeSpan DefaultCommitWait = TimeSpan.FromSeconds(45);

    public static Task ApplyFromInstallAsync(string healthFilePath, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.CompletedTask;
        }

        return ApplyOnWindowsAsync(healthFilePath, cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static Task ApplyOnWindowsAsync(string healthFilePath, CancellationToken cancellationToken) =>
        ApplyIfPresentAsync(
            AppContext.BaseDirectory,
            AgentUpdateLayout.WatchdogInstallDirectory(AppContext.BaseDirectory),
            AgentUpdateLayout.WatchdogBackupDirectory(healthFilePath),
            new WindowsWatchdogServiceControl(),
            DefaultCommitWait,
            cancellationToken);

    internal static async Task ApplyIfPresentAsync(
        string workerDirectory,
        string watchdogDirectory,
        string backupDirectory,
        IWatchdogServiceControl services,
        TimeSpan commitWait,
        CancellationToken cancellationToken)
    {
        var bundle = Path.Combine(workerDirectory, AgentUpdateLayout.WatchdogPayloadDirectoryName);
        if (!File.Exists(Path.Combine(bundle, AgentUpdateLayout.WatchdogExeName)))
        {
            return;
        }

        var version = ReadVersion(Path.Combine(bundle, ".version"));
        if (version.Length == 0)
        {
            version = ReadVersion(Path.Combine(workerDirectory, ".version"));
        }

        if (version.Length > 0 &&
            string.Equals(ReadVersion(Path.Combine(watchdogDirectory, ".version")), version, StringComparison.Ordinal))
        {
            TryDeleteDirectory(bundle);
            return;
        }

        await WaitForWorkerCommitAsync(workerDirectory, version, commitWait, cancellationToken);
        services.StopForReplacement(AgentUpdateLayout.WatchdogServiceName, watchdogDirectory);
        var installRemoved = false;
        try
        {
            Replace(bundle, watchdogDirectory, backupDirectory, version, ref installRemoved);
            services.Start(AgentUpdateLayout.WatchdogServiceName);
            TryDeleteDirectory(bundle);
        }
        catch
        {
            if (installRemoved)
            {
                Restore(backupDirectory, watchdogDirectory);
            }

            try
            {
                services.Start(AgentUpdateLayout.WatchdogServiceName);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.TimeoutException)
            {
            }

            throw;
        }
    }

    private static async Task WaitForWorkerCommitAsync(
        string workerDirectory,
        string version,
        TimeSpan commitWait,
        CancellationToken cancellationToken)
    {
        var marker = Path.Combine(workerDirectory, AgentUpdateLayout.UpdateCommittedFileName);
        var deadline = DateTime.UtcNow + commitWait;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (version.Length > 0 &&
                string.Equals(ReadVersion(marker), version, StringComparison.Ordinal))
            {
                return;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }

    private static void Replace(
        string source,
        string destination,
        string backupDirectory,
        string version,
        ref bool installRemoved)
    {
        var preserved = Path.Combine(Path.GetTempPath(), "slw-watchdog-config-" + Guid.NewGuid().ToString("N"));
        try
        {
            TryDeleteDirectory(backupDirectory);
            if (Directory.Exists(destination))
            {
                CopyDirectory(destination, backupDirectory);
            }

            CopyProtectedConfiguration(destination, preserved);
            TryDeleteDirectory(destination);
            installRemoved = true;
            CopyDirectory(source, destination);
            CopyProtectedConfiguration(preserved, destination);
            File.WriteAllText(
                Path.Combine(destination, ".version"),
                version,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        finally
        {
            TryDeleteDirectory(preserved);
        }
    }

    private static void Restore(string backupDirectory, string destination)
    {
        if (!Directory.Exists(backupDirectory))
        {
            return;
        }

        TryDeleteDirectory(destination);
        CopyDirectory(backupDirectory, destination);
    }

    private static void CopyProtectedConfiguration(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase) &&
                fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(file, Path.Combine(destinationDirectory, fileName), true);
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), true);
        }
    }

    private static string ReadVersion(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    [SupportedOSPlatform("windows")]
    private sealed class WindowsWatchdogServiceControl : IWatchdogServiceControl
    {
        public void StopForReplacement(string serviceName, string installDirectory)
        {
            RunSc(["failureflag", serviceName, "0"]);
            using var service = new ServiceController(serviceName);
            var deadline = DateTime.UtcNow.AddMinutes(2);
            while (DateTime.UtcNow < deadline)
            {
                service.Refresh();
                if (service.Status == ServiceControllerStatus.StopPending ||
                    service.Status == ServiceControllerStatus.Running)
                {
                    if (service.Status == ServiceControllerStatus.Running)
                    {
                        service.Stop();
                    }
                }
                else if (service.Status == ServiceControllerStatus.Stopped && ExecutableIsFree(installDirectory))
                {
                    return;
                }

                Thread.Sleep(300);
            }

            throw new System.TimeoutException($"Watchdog service {serviceName} did not stop before the file replacement.");
        }

        public void Start(string serviceName)
        {
            RunSc(["start", serviceName]);
            RunSc(["failureflag", serviceName, "1"]);
        }

        private static bool ExecutableIsFree(string installDirectory)
        {
            var executable = Path.Combine(installDirectory, AgentUpdateLayout.WatchdogExeName);
            if (!File.Exists(executable))
            {
                return true;
            }

            try
            {
                using var stream = new FileStream(executable, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void RunSc(IReadOnlyList<string> arguments)
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

            using var process = Process.Start(start) ?? throw new InvalidOperationException("sc.exe did not start.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            var output = stdout + stderr;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"sc.exe {string.Join(' ', arguments)} failed: {output.Trim()}");
            }
        }
    }
}
