using System.Text.Json;

namespace SwLicenseWatcher.Setup.Core;

public sealed class AgentConfigurationWriter
{
    public void WriteWorker(
        string workerDirectory,
        CompanySettings settings,
        string deviceCode,
        string domainName,
        string healthPath,
        string queueDirectory)
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
                "QueueDirectory": {{Json(queueDirectory)}},
                "DpapiScope": "LocalMachine",
                "MaxQueuedSnapshots": 48,
                "MaxQueueBytes": 67108864
              }
            }
            """;
        File.WriteAllText(Path.Combine(workerDirectory, "appsettings.json"), json + Environment.NewLine);
    }

    public void WriteWatchdog(
        string watchdogDirectory,
        CompanySettings settings,
        string deviceCode,
        string workerDirectory,
        string healthPath,
        string stagingDirectory,
        string backupDirectory)
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
                "WorkerInstallDirectory": {{Json(workerDirectory)}},
                "WorkerHealthFilePath": {{Json(healthPath)}},
                "ApiToken": {{Json(settings.AgentToken)}},
                "StagingDirectory": {{Json(stagingDirectory)}},
                "BackupDirectory": {{Json(backupDirectory)}},
                "CheckInterval": "04:00:00",
                "MaxJitter": "01:00:00",
                "MaxPackageBytes": 536870912,
                "MaxExtractedBytes": 1073741824,
                "RunOnceForDiagnostics": false
              }
            }
            """;
        File.WriteAllText(Path.Combine(watchdogDirectory, "appsettings.json"), json + Environment.NewLine);
    }

    private static string Json(string value) =>
        JsonSerializer.Serialize(value, SetupJsonContext.Default.String);
}
