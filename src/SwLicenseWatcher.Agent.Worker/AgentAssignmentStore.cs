using System.Text;
using System.Text.Json;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Worker;

public sealed class AgentAssignmentStore(string filePath)
{
    private readonly object _gate = new();
    private string? _assignedHostName = Read(filePath);

    public string FilePath { get; } = filePath;

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

    public string ResolveHostName(string machineName) =>
        AssignedHostName ?? machineName;

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

        await WriteAsync(normalized, cancellationToken);
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

    private async Task WriteAsync(string? assignedHostName, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(
            new StoredAgentAssignment(assignedHostName),
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

    private static string? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            var stored = JsonSerializer.Deserialize(json, InventoryJsonSerializerContext.Default.StoredAgentAssignment);
            return string.IsNullOrWhiteSpace(stored?.AssignedHostName) ? null : stored.AssignedHostName.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
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
    string? AssignedHostName);
