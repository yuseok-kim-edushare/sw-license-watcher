using System.Text;
using System.Text.Json;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Worker;

public sealed class AgentAssignmentStore
{
    private readonly object _gate = new();
    private string? _assignedHostName;
    private string? _assignedDeviceCode;

    public AgentAssignmentStore(string filePath)
    {
        FilePath = filePath;
        var stored = Read(filePath);
        _assignedHostName = stored.HostName;
        _assignedDeviceCode = stored.DeviceCode;
    }

    public string FilePath { get; }

    public string? AssignedHostName
    {
        get
        {
            lock (_gate)
            {
                return _assignedHostName;
            }
        }
    }

    public string? AssignedDeviceCode
    {
        get
        {
            lock (_gate)
            {
                return _assignedDeviceCode;
            }
        }
    }

    public string ResolveHostName(string machineName) =>
        AssignedHostName ?? machineName;

    public string ResolveDeviceCode(string configuredDeviceCode) =>
        AssignedDeviceCode ?? configuredDeviceCode;

    public async Task ApplyAsync(bool assignmentSpecified, string? assignedHostName, CancellationToken cancellationToken)
    {
        if (!assignmentSpecified)
        {
            return;
        }

        var normalized = string.IsNullOrWhiteSpace(assignedHostName) ? null : assignedHostName.Trim();
        lock (_gate)
        {
            if (string.Equals(_assignedHostName, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _assignedHostName = normalized;
        }

        await WriteAsync(cancellationToken);
    }

    public async Task ApplyLabelsAsync(
        bool hostSpecified,
        string? assignedHostName,
        bool deviceCodeSpecified,
        string? assignedDeviceCode,
        CancellationToken cancellationToken)
    {
        var changed = false;
        lock (_gate)
        {
            if (hostSpecified)
            {
                var host = string.IsNullOrWhiteSpace(assignedHostName) ? null : assignedHostName.Trim();
                if (!string.Equals(_assignedHostName, host, StringComparison.Ordinal))
                {
                    _assignedHostName = host;
                    changed = true;
                }
            }

            if (deviceCodeSpecified)
            {
                var code = string.IsNullOrWhiteSpace(assignedDeviceCode) ? null : assignedDeviceCode.Trim();
                if (!string.Equals(_assignedDeviceCode, code, StringComparison.Ordinal))
                {
                    _assignedDeviceCode = code;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            await WriteAsync(cancellationToken);
        }
    }

    public static bool TryReadAssignedHostName(string? json, out bool assignmentSpecified, out string? assignedHostName)
    {
        assignmentSpecified = false;
        assignedHostName = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!TryGetProperty(root, "AssignedHostName", out var value) &&
                !TryGetProperty(root, "assignedHostName", out value))
            {
                return false;
            }

            assignmentSpecified = true;
            assignedHostName = value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? null
                : value.GetString();
            if (string.IsNullOrWhiteSpace(assignedHostName))
            {
                assignedHostName = null;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryReadAssignment(
        string? json,
        out bool hostSpecified,
        out string? assignedHostName,
        out bool deviceCodeSpecified,
        out string? assignedDeviceCode,
        out string? deviceId,
        out string? deviceCertificate)
    {
        hostSpecified = TryReadAssignedHostName(json, out _, out assignedHostName);
        deviceCodeSpecified = false;
        assignedDeviceCode = null;
        deviceId = null;
        deviceCertificate = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return hostSpecified;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (TryGetProperty(root, "AssignedDeviceCode", out var code) ||
                TryGetProperty(root, "assignedDeviceCode", out code))
            {
                deviceCodeSpecified = true;
                assignedDeviceCode = code.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                    ? null
                    : code.GetString();
                if (string.IsNullOrWhiteSpace(assignedDeviceCode))
                {
                    assignedDeviceCode = null;
                }
            }

            if (TryGetProperty(root, "DeviceId", out var id) || TryGetProperty(root, "deviceId", out id))
            {
                deviceId = id.GetString();
            }

            if (TryGetProperty(root, "DeviceCertificate", out var cert) ||
                TryGetProperty(root, "deviceCertificate", out cert))
            {
                deviceCertificate = cert.GetString();
            }

            return hostSpecified || deviceCodeSpecified;
        }
        catch (JsonException)
        {
            return hostSpecified;
        }
    }

    private async Task WriteAsync(CancellationToken cancellationToken)
    {
        string? host;
        string? code;
        lock (_gate)
        {
            host = _assignedHostName;
            code = _assignedDeviceCode;
        }

        var json = JsonSerializer.Serialize(
            new StoredAgentAssignment(host, code),
            InventoryJsonSerializerContext.Default.StoredAgentAssignment);
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = FilePath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, json, Encoding.UTF8, cancellationToken);
        File.Move(temporaryPath, FilePath, true);
    }

    private static (string? HostName, string? DeviceCode) Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return (null, null);
            }

            var json = File.ReadAllText(path);
            var stored = JsonSerializer.Deserialize(json, InventoryJsonSerializerContext.Default.StoredAgentAssignment);
            return (
                string.IsNullOrWhiteSpace(stored?.AssignedHostName) ? null : stored.AssignedHostName.Trim(),
                string.IsNullOrWhiteSpace(stored?.AssignedDeviceCode) ? null : stored.AssignedDeviceCode.Trim());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return (null, null);
        }
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}

public readonly record struct AgentPublishOutcome(
    AgentPublishResult Result,
    bool AssignmentSpecified,
    string? AssignedHostName,
    bool DeviceCodeSpecified = false,
    string? AssignedDeviceCode = null,
    string? DeviceId = null,
    string? DeviceCertificate = null);
