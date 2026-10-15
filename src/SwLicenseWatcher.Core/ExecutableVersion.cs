using System.Diagnostics;

namespace SwLicenseWatcher.Core;

public static class ExecutableVersion
{
    public static string? Normalize(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var core = version.Trim();
        var metadata = core.IndexOf('+', StringComparison.Ordinal);
        if (metadata >= 0)
        {
            core = core[..metadata];
        }

        core = core.Trim();
        if (!Version.TryParse(core, out var parsed) || parsed is null)
        {
            return null;
        }

        if (parsed.Major == 0 && parsed.Minor == 0 && parsed.Build <= 0 && parsed.Revision <= 0)
        {
            return null;
        }

        if (parsed.Revision == 0)
        {
            return new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0)).ToString();
        }

        return parsed.ToString();
    }

    public static string? TryRead(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            return Normalize(info.ProductVersion) ?? Normalize(info.FileVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
