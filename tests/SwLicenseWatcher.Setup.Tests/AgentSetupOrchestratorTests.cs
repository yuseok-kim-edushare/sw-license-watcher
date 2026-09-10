using System.Text.Json;
using SwLicenseWatcher.Setup.Core;

namespace SwLicenseWatcher.Setup.Tests;

public class AgentSetupOrchestratorTests
{
    [Fact]
    public void Install_copies_payload_writes_settings_and_coordinates_machine_changes()
    {
        using var directory = new TempDirectory();
        var payload = Path.Combine(directory.Path, "payload");
        var installRoot = Path.Combine(directory.Path, "install");
        var stateRoot = Path.Combine(directory.Path, "state");
        var worker = PayloadLayout.GetWorkerDirectory(payload);
        var watchdog = PayloadLayout.GetWatchdogDirectory(payload);
        Directory.CreateDirectory(worker);
        Directory.CreateDirectory(watchdog);
        File.WriteAllText(Path.Combine(worker, PayloadLayout.WorkerExe), "worker");
        File.WriteAllText(Path.Combine(worker, "other.dll"), "worker dependency");
        File.WriteAllText(Path.Combine(watchdog, PayloadLayout.WatchdogExe), "watchdog");
        var sourceSetup = Path.Combine(directory.Path, "source-setup.exe");
        File.WriteAllText(sourceSetup, "setup");
        var machine = new RecordingMachineIntegration();
        var sut = new AgentSetupOrchestrator(machine, installRoot: installRoot, stateRoot: stateRoot);
        var settings = ValidSettings();

        sut.Install(settings, payload, "ASSET-42", sourceSetup);

        Assert.Equal("worker dependency", File.ReadAllText(Path.Combine(
            SetupPaths.WorkerDirectory(installRoot),
            "other.dll")));
        Assert.Equal("setup", File.ReadAllText(SetupPaths.InstalledSetupExe(installRoot)));
        using var workerSettings = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(SetupPaths.WorkerDirectory(installRoot), "appsettings.json")));
        Assert.Equal("ASSET-42", workerSettings.RootElement.GetProperty("Agent").GetProperty("DeviceCode").GetString());
        Assert.Equal(settings.AgentToken, workerSettings.RootElement.GetProperty("Agent").GetProperty("ApiToken").GetString());
        Assert.Equal(
            [
                $"stop:{SetupPaths.WatchdogServiceName}",
                $"stop:{SetupPaths.WorkerServiceName}",
                $"install:{SetupPaths.WorkerServiceName}",
                $"install:{SetupPaths.WatchdogServiceName}",
                $"start:{SetupPaths.WorkerServiceName}",
                $"start:{SetupPaths.WatchdogServiceName}",
                $"arp:{settings.Version}"
            ],
            machine.Events);
    }

    [Fact]
    public void Uninstall_removes_program_files_but_preserves_state_when_requested()
    {
        using var directory = new TempDirectory();
        var installRoot = Path.Combine(directory.Path, "install");
        var stateRoot = Path.Combine(directory.Path, "state");
        Directory.CreateDirectory(SetupPaths.WorkerDirectory(installRoot));
        Directory.CreateDirectory(SetupPaths.WatchdogDirectory(installRoot));
        Directory.CreateDirectory(stateRoot);
        File.WriteAllText(Path.Combine(stateRoot, "queued.json"), "state");
        var machine = new RecordingMachineIntegration();
        var sut = new AgentSetupOrchestrator(machine, installRoot: installRoot, stateRoot: stateRoot);

        sut.Uninstall(removeState: false);

        Assert.False(Directory.Exists(installRoot));
        Assert.True(File.Exists(Path.Combine(stateRoot, "queued.json")));
        Assert.Equal(
            [
                $"stop:{SetupPaths.WatchdogServiceName}",
                $"stop:{SetupPaths.WorkerServiceName}",
                $"delete:{SetupPaths.WatchdogServiceName}",
                $"delete:{SetupPaths.WorkerServiceName}",
                "delete-arp"
            ],
            machine.Events);
    }

    private static CompanySettings ValidSettings() =>
        new()
        {
            ServerBaseUrl = "https://license.example.local",
            AgentToken = new string('t', 32),
            Version = "1.2.3"
        };

    private sealed class RecordingMachineIntegration : IAgentMachineIntegration
    {
        public List<string> Events { get; } = [];

        public void StopService(string name) => Events.Add($"stop:{name}");

        public void InstallOrUpdateService(string name, string displayName, string description, string exePath) =>
            Events.Add($"install:{name}");

        public void StartService(string name) => Events.Add($"start:{name}");

        public void DeleteService(string name) => Events.Add($"delete:{name}");

        public void RegisterArp(string version, string setupExe) => Events.Add($"arp:{version}");

        public void DeleteArp() => Events.Add("delete-arp");
    }
}
