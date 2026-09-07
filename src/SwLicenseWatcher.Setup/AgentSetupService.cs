using System.Diagnostics;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Win32;
using SwLicenseWatcher.Setup.Core;

namespace SwLicenseWatcher.Setup;

internal static class AgentSetupService
{
    public static void Install(
        CompanySettings settings,
        string payloadDirectory,
        string deviceCode,
        string? sourceExePath)
    {
        var installRoot = SetupPaths.DefaultInstallRoot;
        var stateRoot = SetupPaths.DefaultStateRoot;
        var workerDir = SetupPaths.WorkerDirectory(installRoot);
        var watchdogDir = SetupPaths.WatchdogDirectory(installRoot);
        var queueDir = SetupPaths.QueueDirectory(stateRoot);
        var healthPath = SetupPaths.HealthFilePath(stateRoot);
        var stagingDir = SetupPaths.StagingDirectory(stateRoot);
        var backupDir = SetupPaths.BackupDirectory(stateRoot);
        var domainName = SetupPaths.ResolveDomainName();

        StopService(SetupPaths.WatchdogServiceName);
        StopService(SetupPaths.WorkerServiceName);

        CopyDirectory(PayloadLayout.GetWorkerDirectory(payloadDirectory), workerDir);
        CopyDirectory(PayloadLayout.GetWatchdogDirectory(payloadDirectory), watchdogDir);

        if (!File.Exists(SetupPaths.WorkerExe(installRoot)))
        {
            throw new InvalidOperationException("Worker executable was not copied.");
        }

        if (!File.Exists(SetupPaths.WatchdogExe(installRoot)))
        {
            throw new InvalidOperationException("Watchdog executable was not copied.");
        }

        Directory.CreateDirectory(queueDir);
        Directory.CreateDirectory(Path.GetDirectoryName(healthPath)!);
        Directory.CreateDirectory(stagingDir);
        Directory.CreateDirectory(backupDir);

        WriteWorkerSettings(workerDir, settings, deviceCode, domainName, healthPath, queueDir);
        WriteWatchdogSettings(watchdogDir, settings, deviceCode, workerDir, healthPath, stagingDir, backupDir);

        InstallOrUpdateService(
            SetupPaths.WorkerServiceName,
            "SW License Watcher Worker",
            "Collects installed software and sends inventory snapshots.",
            SetupPaths.WorkerExe(installRoot));
        InstallOrUpdateService(
            SetupPaths.WatchdogServiceName,
            "SW License Watcher Watchdog",
            "Downloads and applies signed Worker updates.",
            SetupPaths.WatchdogExe(installRoot));

        StartService(SetupPaths.WorkerServiceName);
        StartService(SetupPaths.WatchdogServiceName);

        var installedSetup = SetupPaths.InstalledSetupExe(installRoot);
        if (!string.IsNullOrWhiteSpace(sourceExePath) && File.Exists(sourceExePath))
        {
            CopySetupExe(sourceExePath, installedSetup);
        }

        RegisterArp(settings.Version, installedSetup);
    }

    public static void RemoveInstalledFiles(bool removeState)
    {
        var installRoot = SetupPaths.DefaultInstallRoot;
        var stateRoot = SetupPaths.DefaultStateRoot;
        StopService(SetupPaths.WatchdogServiceName);
        StopService(SetupPaths.WorkerServiceName);
        DeleteService(SetupPaths.WatchdogServiceName);
        DeleteService(SetupPaths.WorkerServiceName);

        foreach (var directory in new[]
                 {
                     SetupPaths.WatchdogDirectory(installRoot),
                     SetupPaths.WorkerDirectory(installRoot)
                 })
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        var setupExe = SetupPaths.InstalledSetupExe(installRoot);
        if (File.Exists(setupExe))
        {
            try
            {
                File.Delete(setupExe);
            }
            catch (IOException)
            {
                // The running installer copy may lock itself; ARP still gets removed.
            }
        }

        if (Directory.Exists(installRoot) && !Directory.EnumerateFileSystemEntries(installRoot).Any())
        {
            Directory.Delete(installRoot, recursive: false);
        }

        DeleteArp();

        if (removeState && Directory.Exists(stateRoot))
        {
            Directory.Delete(stateRoot, recursive: true);
        }
    }

    public static string ReadInstalledDeviceCode(string machineName)
    {
        var path = Path.Combine(SetupPaths.WorkerDirectory(SetupPaths.DefaultInstallRoot), "appsettings.json");
        if (!File.Exists(path))
        {
            return machineName;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("Agent", out var agent) &&
                agent.TryGetProperty("DeviceCode", out var code) &&
                code.ValueKind == JsonValueKind.String)
            {
                var value = code.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch (JsonException)
        {
        }

        return machineName;
    }

    private static void CopySetupExe(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (PathsEqual(source, destination))
        {
            return;
        }

        File.Copy(source, destination, overwrite: true);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
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
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void WriteWorkerSettings(
        string workerDir,
        CompanySettings settings,
        string deviceCode,
        string domainName,
        string healthPath,
        string queueDir)
    {
        var json =
            $$"""
            {
              "Logging": {
                "LogLevel": {
                  "Default": "Information",
                  "Microsoft.Hosting.Lifetime": "Information"
                }
              },
              "Agent": {
                "DeviceCode": {{Json(deviceCode)}},
                "DomainName": {{Json(domainName)}},
                "ServerBaseUrl": {{Json(settings.ServerBaseUrl)}},
                "SnapshotPath": "/api/inventory/snapshots",
                "HeartbeatPath": "/api/agents/heartbeats",
                "ApiToken": {{Json(settings.AgentToken)}},
                "PollInterval": "00:30:00",
                "MaxJitter": "00:15:00",
                "RunOnceForDiagnostics": false,
                "HealthFilePath": {{Json(healthPath)}}
              },
              "LocalState": {
                "InstanceName": "SwLicenseWatcher",
                "QueueDirectory": {{Json(queueDir)}},
                "DpapiScope": "LocalMachine",
                "MaxQueuedSnapshots": 48,
                "MaxQueueBytes": 67108864
              }
            }
            """;
        File.WriteAllText(Path.Combine(workerDir, "appsettings.json"), json + Environment.NewLine);
    }

    private static void WriteWatchdogSettings(
        string watchdogDir,
        CompanySettings settings,
        string deviceCode,
        string workerDir,
        string healthPath,
        string stagingDir,
        string backupDir)
    {
        var json =
            $$"""
            {
              "Logging": {
                "LogLevel": {
                  "Default": "Information",
                  "Microsoft.Hosting.Lifetime": "Information"
                }
              },
              "Watchdog": {
                "DeviceCode": {{Json(deviceCode)}},
                "ServerBaseUrl": {{Json(settings.ServerBaseUrl)}},
                "ManifestPath": "/api/updates/worker/manifest",
                "WorkerServiceName": {{Json(SetupPaths.WorkerServiceName)}},
                "WorkerInstallDirectory": {{Json(workerDir)}},
                "WorkerHealthFilePath": {{Json(healthPath)}},
                "ApiToken": {{Json(settings.AgentToken)}},
                "StagingDirectory": {{Json(stagingDir)}},
                "BackupDirectory": {{Json(backupDir)}},
                "CheckInterval": "04:00:00",
                "MaxJitter": "01:00:00",
                "MaxPackageBytes": 536870912,
                "MaxExtractedBytes": 1073741824,
                "RunOnceForDiagnostics": false
              }
            }
            """;
        File.WriteAllText(Path.Combine(watchdogDir, "appsettings.json"), json + Environment.NewLine);
    }

    private static string Json(string value) => JsonSerializer.Serialize(value);

    private static void InstallOrUpdateService(string name, string displayName, string description, string exePath)
    {
        var binPath = $"\"{exePath}\"";
        if (ServiceExists(name))
        {
            RunSc("config", name, "binPath=", binPath, "start=", "auto", "DisplayName=", displayName);
        }
        else
        {
            RunSc("create", name, "binPath=", binPath, "start=", "auto", "DisplayName=", displayName);
        }

        RunSc("description", name, description);
    }

    private static void StartService(string name)
    {
        using var service = new ServiceController(name);
        if (service.Status != ServiceControllerStatus.Running)
        {
            service.Start();
        }

        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
    }

    private static void StopService(string name)
    {
        if (!ServiceExists(name))
        {
            return;
        }

        using var service = new ServiceController(name);
        if (service.Status == ServiceControllerStatus.Stopped)
        {
            return;
        }

        service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromMinutes(1));
    }

    private static void DeleteService(string name)
    {
        if (!ServiceExists(name))
        {
            return;
        }

        StopService(name);
        RunSc("delete", name);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (!ServiceExists(name))
            {
                return;
            }

            Thread.Sleep(300);
        }

        throw new InvalidOperationException($"Service {name} was not deleted in time.");
    }

    private static bool ServiceExists(string name)
    {
        return ServiceController.GetServices().Any(service =>
            string.Equals(service.ServiceName, name, StringComparison.OrdinalIgnoreCase));
    }

    private static void RunSc(params string[] arguments)
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
        if (process.ExitCode != 0)
        {
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"sc.exe {arguments[0]} failed with exit code {process.ExitCode}. {output}".Trim());
        }
    }

    private static void RegisterArp(string version, string setupExe)
    {
        using var key = Registry.LocalMachine.CreateSubKey(SetupPaths.ArpRegistryPath, writable: true)
            ?? throw new InvalidOperationException("Could not write the uninstall registry key.");
        key.SetValue("DisplayName", SetupPaths.ProductName);
        key.SetValue("Publisher", SetupPaths.Publisher);
        key.SetValue("DisplayVersion", version);
        key.SetValue("UninstallString", $"\"{setupExe}\" /uninstall");
        key.SetValue("DisplayIcon", setupExe);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", 20 * 1024, RegistryValueKind.DWord);
    }

    private static void DeleteArp()
    {
        Registry.LocalMachine.DeleteSubKeyTree(SetupPaths.ArpRegistryPath, throwOnMissingSubKey: false);
    }
}
