using System.Text.Json;

namespace SwLicenseWatcher.Setup.Core;

public sealed record ExistingAgentInstallation(
    bool WorkerServicePresent,
    bool WatchdogServicePresent,
    bool WorkerFilesPresent,
    string? DeviceCode,
    string? AssignedDeviceCode,
    string? DomainName,
    bool HasDeviceIdentity,
    string? InstalledVersion)
{
    public bool IsPresent => WorkerServicePresent || WatchdogServicePresent || WorkerFilesPresent;

    public string? ConfiguredDeviceCode =>
        string.IsNullOrWhiteSpace(DeviceCode) ? null : DeviceCode;

    public string? PreferredDeviceCode =>
        ConfiguredDeviceCode ?? (string.IsNullOrWhiteSpace(AssignedDeviceCode) ? null : AssignedDeviceCode);

    public bool RequiresServerKeyAuthorization =>
        InPlaceUpgradePolicy.RequiresServerKeyAuthorization(InstalledVersion);
}

public sealed class ExistingAgentInstallationReader
{
    private readonly IAgentMachineIntegration _machine;
    private readonly InstalledDeviceCodeReader _deviceCodeReader;
    private readonly string _installRoot;
    private readonly string _stateRoot;

    public ExistingAgentInstallationReader(
        IAgentMachineIntegration machine,
        string? installRoot = null,
        string? stateRoot = null,
        InstalledDeviceCodeReader? deviceCodeReader = null)
    {
        _machine = machine;
        _installRoot = installRoot ?? SetupPaths.DefaultInstallRoot;
        _stateRoot = stateRoot ?? SetupPaths.DefaultStateRoot;
        _deviceCodeReader = deviceCodeReader ?? new InstalledDeviceCodeReader(_installRoot);
    }

    public ExistingAgentInstallation Read()
    {
        _deviceCodeReader.TryRead(out var deviceCode);
        TryReadWorkerSetting("DomainName", out var domainName);
        return new ExistingAgentInstallation(
            _machine.ServiceExists(SetupPaths.WorkerServiceName),
            _machine.ServiceExists(SetupPaths.WatchdogServiceName),
            File.Exists(SetupPaths.WorkerExe(_installRoot)),
            string.IsNullOrWhiteSpace(deviceCode) ? null : deviceCode,
            TryReadAssignedDeviceCode(SetupPaths.AssignmentFilePath(_stateRoot)),
            string.IsNullOrWhiteSpace(domainName) ? null : domainName,
            File.Exists(SetupPaths.DeviceIdentityPath(_stateRoot)),
            ReadInstalledVersion());
    }

    private string? ReadInstalledVersion()
    {
        var workerDirectory = SetupPaths.WorkerDirectory(_installRoot);
        if (!Directory.Exists(workerDirectory))
        {
            return null;
        }

        var version = ReleaseLayout.ReadVersion(workerDirectory);
        return string.Equals(version, "0.0.0", StringComparison.Ordinal) &&
               !File.Exists(Path.Combine(workerDirectory, PayloadLayout.VersionFileName))
            ? null
            : version;
    }

    private bool TryReadWorkerSetting(string name, out string value)
    {
        value = string.Empty;
        var path = Path.Combine(SetupPaths.WorkerDirectory(_installRoot), "appsettings.json");
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("Agent", out var agent) &&
                agent.TryGetProperty(name, out var property) &&
                property.ValueKind == JsonValueKind.String)
            {
                var text = property.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    value = text.Trim();
                    return true;
                }
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private static string? TryReadAssignedDeviceCode(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if ((!root.TryGetProperty("AssignedDeviceCode", out var code) &&
                 !root.TryGetProperty("assignedDeviceCode", out code)) ||
                code.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = code.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
