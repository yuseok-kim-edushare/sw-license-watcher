using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace SwLicenseWatcher.Core;

public static class WindowsOsDescription
{
    internal const int MaxLength = 128;

    public static string Resolve(ILogger? logger = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return TruncateToLimit(RuntimeInformation.OSDescription);
        }

        try
        {
            return ReadWindowsDescription(logger);
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Unable to read the Windows OS description from the registry; using the runtime version string.");
            return TruncateToLimit(Environment.OSVersion.VersionString);
        }
    }

    [SupportedOSPlatform("windows")]
    private static string ReadWindowsDescription(ILogger? logger)
    {
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        if (key is null)
        {
            logger?.LogDebug("Windows NT CurrentVersion registry key was not found; using the runtime version string.");
            return TruncateToLimit(Environment.OSVersion.VersionString);
        }

        return Compose(
            ReadString(key, "ProductName"),
            ReadString(key, "DisplayVersion"),
            ReadString(key, "ReleaseId"),
            ReadString(key, "CurrentBuildNumber"),
            ReadInt32(key, "UBR"),
            Environment.OSVersion.Version,
            RuntimeInformation.OSArchitecture,
            Environment.OSVersion.VersionString);
    }

    internal static string Compose(
        string? productName,
        string? displayVersion,
        string? releaseId,
        string? buildNumber,
        int? ubr,
        Version? version,
        Architecture architecture,
        string fallback)
    {
        var displayName = NormalizeProductName(productName, buildNumber);
        var marketingVersion = FirstNonEmpty(displayVersion, releaseId);
        var kernel = TryFormatKernelVersion(version, buildNumber, ubr);

        var parts = new List<string>(2);
        if (!string.IsNullOrEmpty(displayName))
        {
            parts.Add(displayName);
        }

        if (!string.IsNullOrEmpty(marketingVersion))
        {
            parts.Add(marketingVersion);
        }

        var description = string.Join(' ', parts);
        if (kernel is not null)
        {
            description = string.IsNullOrEmpty(description)
                ? $"({kernel})"
                : $"{description} ({kernel})";
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return TruncateToLimit(fallback);
        }

        var architectureLabel = FormatArchitecture(architecture);
        if (!string.IsNullOrEmpty(architectureLabel))
        {
            var withArchitecture = $"{description} {architectureLabel}";
            if (withArchitecture.Length <= MaxLength)
            {
                return withArchitecture;
            }
        }

        return TruncateToLimit(description);
    }

    private static string? NormalizeProductName(string? productName, string? buildNumber)
    {
        var name = NullIfWhiteSpace(productName);
        if (name is null)
        {
            return null;
        }

        if (TryParseBuildNumber(buildNumber, out var build) &&
            build >= 22000 &&
            name.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows 11" + name["Windows 10".Length..];
        }

        return name;
    }

    private static string? TryFormatKernelVersion(Version? version, string? buildNumber, int? ubr)
    {
        if (version is null || !TryParseBuildNumber(buildNumber, out var build))
        {
            return null;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{version.Major}.{version.Minor}.{build}.{ubr ?? 0}");
    }

    private static bool TryParseBuildNumber(string? buildNumber, out int build) =>
        int.TryParse(NullIfWhiteSpace(buildNumber), NumberStyles.Integer, CultureInfo.InvariantCulture, out build);

    private static string FormatArchitecture(Architecture architecture) =>
        architecture.ToString().ToLowerInvariant();

    private static string? FirstNonEmpty(string? primary, string? fallback) =>
        NullIfWhiteSpace(primary) ?? NullIfWhiteSpace(fallback);

    private static string? NullIfWhiteSpace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static string TruncateToLimit(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= MaxLength ? value : value[..MaxLength];
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadString(RegistryKey key, string name) =>
        key.GetValue(name) switch
        {
            string text => text,
            int number => number.ToString(CultureInfo.InvariantCulture),
            long number => number.ToString(CultureInfo.InvariantCulture),
            _ => null
        };

    [SupportedOSPlatform("windows")]
    private static int? ReadInt32(RegistryKey key, string name) =>
        key.GetValue(name) switch
        {
            int number => number,
            long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
}
