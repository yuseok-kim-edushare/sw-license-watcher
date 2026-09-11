using System.Text.Json;

namespace SwLicenseWatcher.Setup.Core;

public sealed class InstalledDeviceCodeReader
{
    private readonly string _installRoot;

    public InstalledDeviceCodeReader(string? installRoot = null)
    {
        _installRoot = installRoot ?? SetupPaths.DefaultInstallRoot;
    }

    public string Read(string machineName) =>
        TryRead(out var deviceCode) ? deviceCode : machineName;

    public bool TryRead(out string deviceCode)
    {
        deviceCode = string.Empty;
        var path = Path.Combine(SetupPaths.WorkerDirectory(_installRoot), "appsettings.json");
        if (!File.Exists(path))
        {
            return false;
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
                    deviceCode = value.Trim();
                    return true;
                }
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }
}
