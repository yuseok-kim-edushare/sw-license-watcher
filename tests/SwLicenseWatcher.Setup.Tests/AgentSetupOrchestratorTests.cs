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
        var sut = new AgentSetupOrchestrator(
            machine,
            installRoot: installRoot,
            stateRoot: stateRoot,
            privilege: UnrestrictedAdministratorPrivilege.Instance);
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
    public void Install_over_existing_services_is_in_place_upgrade_and_preserves_local_identity()
    {
        using var directory = new TempDirectory();
        var payload = Path.Combine(directory.Path, "payload");
        var installRoot = Path.Combine(directory.Path, "install");
        var stateRoot = Path.Combine(directory.Path, "state");
        var worker = PayloadLayout.GetWorkerDirectory(payload);
        var watchdog = PayloadLayout.GetWatchdogDirectory(payload);
        Directory.CreateDirectory(worker);
        Directory.CreateDirectory(watchdog);
        File.WriteAllText(Path.Combine(worker, PayloadLayout.WorkerExe), "worker-v2");
        File.WriteAllText(Path.Combine(watchdog, PayloadLayout.WatchdogExe), "watchdog-v2");
        Directory.CreateDirectory(SetupPaths.WorkerDirectory(installRoot));
        Directory.CreateDirectory(Path.Combine(stateRoot, "state"));
        File.WriteAllText(
            Path.Combine(SetupPaths.WorkerDirectory(installRoot), "appsettings.json"),
            """{"Agent":{"DeviceCode":"ASSET-42","DomainName":"CORP"}}""");
        File.WriteAllText(SetupPaths.DeviceIdentityPath(stateRoot), "mldsa-identity");
        File.WriteAllText(
            SetupPaths.AssignmentFilePath(stateRoot),
            """{"assignedDeviceCode":"HOST-9"}""");
        var machine = new RecordingMachineIntegration();
        machine.ExistingServices.Add(SetupPaths.WorkerServiceName);
        machine.ExistingServices.Add(SetupPaths.WatchdogServiceName);
        var sut = new AgentSetupOrchestrator(
            machine,
            installRoot: installRoot,
            stateRoot: stateRoot,
            privilege: UnrestrictedAdministratorPrivilege.Instance);

        var existing = sut.DetectExistingInstallation();
        Assert.True(existing.IsPresent);
        Assert.Equal("ASSET-42", existing.DeviceCode);
        Assert.Equal("HOST-9", existing.AssignedDeviceCode);
        Assert.Equal("CORP", existing.DomainName);
        Assert.True(existing.HasDeviceIdentity);

        sut.Install(ValidSettings(), payload, "ASSET-42", sourceExePath: null);

        Assert.Equal("worker-v2", File.ReadAllText(SetupPaths.WorkerExe(installRoot)));
        Assert.Equal("mldsa-identity", File.ReadAllText(SetupPaths.DeviceIdentityPath(stateRoot)));
        Assert.Equal(
            """{"assignedDeviceCode":"HOST-9"}""",
            File.ReadAllText(SetupPaths.AssignmentFilePath(stateRoot)));
        using var workerSettings = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(SetupPaths.WorkerDirectory(installRoot), "appsettings.json")));
        Assert.Equal("ASSET-42", workerSettings.RootElement.GetProperty("Agent").GetProperty("DeviceCode").GetString());
        Assert.Equal("CORP", workerSettings.RootElement.GetProperty("Agent").GetProperty("DomainName").GetString());
        Assert.Equal(
            ValidSettings().AgentToken,
            workerSettings.RootElement.GetProperty("Agent").GetProperty("ApiToken").GetString());
        Assert.DoesNotContain(machine.Events, item => item.StartsWith("delete:", StringComparison.Ordinal));
        Assert.DoesNotContain("delete-arp", machine.Events);
        Assert.Equal(
            [
                $"stop:{SetupPaths.WatchdogServiceName}",
                $"stop:{SetupPaths.WorkerServiceName}",
                $"install:{SetupPaths.WorkerServiceName}",
                $"install:{SetupPaths.WatchdogServiceName}",
                $"start:{SetupPaths.WorkerServiceName}",
                $"start:{SetupPaths.WatchdogServiceName}",
                $"arp:{ValidSettings().Version}"
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
        var sut = new AgentSetupOrchestrator(
            machine,
            installRoot: installRoot,
            stateRoot: stateRoot,
            privilege: UnrestrictedAdministratorPrivilege.Instance);

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

    [Fact]
    public void Install_refuses_without_administrator_privilege()
    {
        using var directory = new TempDirectory();
        var machine = new RecordingMachineIntegration();
        var sut = new AgentSetupOrchestrator(
            machine,
            installRoot: Path.Combine(directory.Path, "install"),
            stateRoot: Path.Combine(directory.Path, "state"),
            privilege: new DeniedPrivilege());

        var error = Assert.Throws<UnauthorizedAccessException>(
            () => sut.Install(ValidSettings(), directory.Path, "ASSET-1", sourceExePath: null));

        Assert.Equal(WindowsAdministratorPrivilege.RequiredMessage, error.Message);
        Assert.Empty(machine.Events);
    }

    [Fact]
    public void Uninstall_refuses_without_administrator_privilege()
    {
        using var directory = new TempDirectory();
        var installRoot = Path.Combine(directory.Path, "install");
        Directory.CreateDirectory(SetupPaths.WorkerDirectory(installRoot));
        var marker = Path.Combine(SetupPaths.WorkerDirectory(installRoot), "marker");
        File.WriteAllText(marker, "installed");
        var machine = new RecordingMachineIntegration();
        var sut = new AgentSetupOrchestrator(
            machine,
            installRoot: installRoot,
            stateRoot: Path.Combine(directory.Path, "state"),
            privilege: new DeniedPrivilege());

        var error = Assert.Throws<UnauthorizedAccessException>(() => sut.Uninstall(removeState: false));

        Assert.Equal(WindowsAdministratorPrivilege.RequiredMessage, error.Message);
        Assert.True(File.Exists(marker));
        Assert.Empty(machine.Events);
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

        public HashSet<string> ExistingServices { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool ServiceExists(string name) => ExistingServices.Contains(name);

        public void StopService(string name) => Events.Add($"stop:{name}");

        public void InstallOrUpdateService(string name, string displayName, string description, string exePath) =>
            Events.Add($"install:{name}");

        public void StartService(string name) => Events.Add($"start:{name}");

        public void DeleteService(string name) => Events.Add($"delete:{name}");

        public void RegisterArp(string version, string setupExe) => Events.Add($"arp:{version}");

        public void DeleteArp() => Events.Add("delete-arp");
    }

    private sealed class DeniedPrivilege : IAdministratorPrivilege
    {
        public bool IsElevated => false;
    }
}
