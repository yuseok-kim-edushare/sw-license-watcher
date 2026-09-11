namespace SwLicenseWatcher.Setup.Core;

public static class DeviceCodeResolver
{
    public const int MaxLength = 128;

    public static bool TryResolve(string? assetCode, string machineName, out string deviceCode, out string error)
    {
        deviceCode = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(machineName))
        {
            error = "The computer name is empty.";
            return false;
        }

        var trimmedAsset = assetCode?.Trim() ?? string.Empty;
        var resolved = trimmedAsset.Length == 0 ? machineName.Trim() : trimmedAsset;
        if (resolved.Length == 0)
        {
            error = "DeviceCode is required.";
            return false;
        }

        if (resolved.Length > MaxLength)
        {
            error = $"DeviceCode must be at most {MaxLength} characters.";
            return false;
        }

        deviceCode = resolved;
        return true;
    }

    public static string Resolve(string? assetCode, string machineName)
    {
        if (!TryResolve(assetCode, machineName, out var deviceCode, out var error))
        {
            throw new ArgumentException(error);
        }

        return deviceCode;
    }

    public static bool TryResolveForInstall(
        string? assetCode,
        string machineName,
        string? existingDeviceCode,
        out string deviceCode,
        out string error)
    {
        if (!string.IsNullOrWhiteSpace(assetCode))
        {
            return TryResolve(assetCode, machineName, out deviceCode, out error);
        }

        if (!string.IsNullOrWhiteSpace(existingDeviceCode))
        {
            return TryResolve(existingDeviceCode, machineName, out deviceCode, out error);
        }

        return TryResolve(null, machineName, out deviceCode, out error);
    }
}
