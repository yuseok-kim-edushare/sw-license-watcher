namespace SwLicenseWatcher.Core;

public static class WorkerUpdatePackageNames
{
    public const string ChecksumAssetName = "SHA256SUMS.txt";

    public static string WorkerZipFileName(string version) =>
        $"SwLicenseWatcher.Agent.Worker-{NormalizeVersion(version)}.zip";

    public static string NormalizeVersion(string version)
    {
        var trimmed = version.Trim();
        if (trimmed.Length >= 2 && (trimmed[0] is 'v' or 'V') && char.IsAsciiDigit(trimmed[1]))
        {
            return trimmed[1..];
        }

        return trimmed;
    }

    public static bool IsSafeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 32)
        {
            return false;
        }

        if (version is "." or "..")
        {
            return false;
        }

        foreach (var c in version)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_')
            {
                return false;
            }
        }

        return true;
    }
}
