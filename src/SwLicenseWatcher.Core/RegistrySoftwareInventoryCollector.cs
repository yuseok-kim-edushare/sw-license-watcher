using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Principal;

namespace SwLicenseWatcher.Core;

public sealed class RegistrySoftwareInventoryCollector(ILogger<RegistrySoftwareInventoryCollector> logger) : ISoftwareInventoryCollector
{
    public Task<IReadOnlyCollection<InstalledSoftwareEntry>> CollectAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<IReadOnlyCollection<InstalledSoftwareEntry>>([]);
        }

        return Task.FromResult<IReadOnlyCollection<InstalledSoftwareEntry>>(CollectWindowsEntries(cancellationToken));
    }

    [SupportedOSPlatform("windows")]
    private IReadOnlyCollection<InstalledSoftwareEntry> CollectWindowsEntries(CancellationToken cancellationToken)
    {
        var machineRegistryProbes = new[]
        {
            new RegistryProbe(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "Machine"),
            new RegistryProbe(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "Machine")
        };

        var dedupe = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var software = new List<InstalledSoftwareEntry>();

        foreach (var probe in machineRegistryProbes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadEntries(probe.Hive, probe.View, probe.SubKeyPath, probe.Scope, software, dedupe, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        ReadCurrentUserEntries(software, dedupe, cancellationToken);
        ReadLoadedUserEntries(software, dedupe, cancellationToken);

        return software;
    }

    [SupportedOSPlatform("windows")]
    private void ReadCurrentUserEntries(List<InstalledSoftwareEntry> software, Dictionary<string, int> dedupe, CancellationToken cancellationToken)
    {
        try
        {
            using var currentUserKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (currentUserKey is not null)
            {
                AppendEntries(currentUserKey, "User", software, dedupe, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            logger.LogDebug(ex, "Skipped the current-user uninstall registry key because access was denied.");
        }
    }

    [SupportedOSPlatform("windows")]
    private void ReadLoadedUserEntries(List<InstalledSoftwareEntry> software, Dictionary<string, int> dedupe, CancellationToken cancellationToken)
    {
        var currentSid = TryGetCurrentUserSid();
        try
        {
            using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default);
            foreach (var sid in users.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsSid(sid) || ShouldIgnoreLoadedUserSid(sid, currentSid))
                {
                    continue;
                }

                try
                {
                    using var uninstallKey = users.OpenSubKey($@"{sid}\Software\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstallKey is not null)
                    {
                        AppendEntries(uninstallKey, $"User:{sid}", software, dedupe, cancellationToken);
                    }
                }
                catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
                {
                    logger.LogDebug(ex, "Skipped uninstall registry keys for user SID {UserSid} because access was denied.", sid);
                }
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            logger.LogDebug(ex, "Skipped loaded HKEY_USERS uninstall registry keys because access was denied.");
        }
    }

    [SupportedOSPlatform("windows")]
    private string? TryGetCurrentUserSid()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            logger.LogDebug(ex, "Unable to resolve the current user SID for registry inventory collection.");
            return null;
        }
    }

    internal static InstalledSoftwareEntry? TryCreateEntry(
        string? displayName,
        string? version,
        string? publisher,
        string? installLocation,
        string scope,
        Dictionary<string, int> dedupe,
        List<InstalledSoftwareEntry> software,
        DateTimeOffset? installedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var dedupeKey = string.Join('|', displayName, version, publisher, installLocation, scope);
        if (dedupe.TryGetValue(dedupeKey, out var index))
        {
            if (installedAtUtc is not null && software[index].InstalledAtUtc is null)
            {
                software[index] = software[index] with { InstalledAtUtc = installedAtUtc };
            }

            return null;
        }

        var entry = new InstalledSoftwareEntry(
            displayName,
            string.IsNullOrWhiteSpace(version) ? null : version,
            string.IsNullOrWhiteSpace(publisher) ? null : publisher,
            string.IsNullOrWhiteSpace(installLocation) ? null : installLocation,
            scope,
            "Registry.Uninstall",
            InstalledAtUtc: installedAtUtc);
        dedupe.Add(dedupeKey, software.Count);
        software.Add(entry);
        return entry;
    }

    internal static DateTimeOffset? TryParseInstallDate(object? value) => value switch
    {
        string text => TryParseInstallDateText(text),
        int number => TryParseInstallDateNumber(number),
        long number => TryParseInstallDateNumber(number),
        uint number => TryParseInstallDateNumber(number),
        _ => null
    };

    private static DateTimeOffset? TryParseInstallDateText(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Length is 8 or 14 && text.All(char.IsAsciiDigit))
        {
            if (!TryParseCalendarDigits(text.AsSpan(0, 8), out var year, out var month, out var day))
            {
                return null;
            }

            var hour = 0;
            var minute = 0;
            var second = 0;
            if (text.Length == 14 && !TryParseClockDigits(text.AsSpan(8), out hour, out minute, out second))
            {
                return null;
            }

            return TryCreateTimestamp(year, month, day, hour, minute, second);
        }

        string[] formats =
        [
            "yyyy-MM-dd",
            "yyyy/MM/dd",
            "yyyy.MM.dd",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy/MM/dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ssZ"
        ];
        if (!DateTimeOffset.TryParseExact(
                text,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed) ||
            parsed.Year is < 1980 or > 2100)
        {
            return null;
        }

        return new DateTimeOffset(parsed.Year, parsed.Month, parsed.Day, parsed.Hour, parsed.Minute, parsed.Second, TimeSpan.Zero);
    }

    private static DateTimeOffset? TryParseInstallDateNumber(long number)
    {
        if (number is >= 19800101 and <= 21001231)
        {
            var year = (int)(number / 10000);
            var month = (int)(number / 100 % 100);
            var day = (int)(number % 100);
            return TryCreateTimestamp(year, month, day, 0, 0, 0);
        }

        if (number is >= 315532800 and <= 4133980799)
        {
            return DateTimeOffset.FromUnixTimeSeconds(number);
        }

        return null;
    }

    private static bool TryParseCalendarDigits(ReadOnlySpan<char> text, out int year, out int month, out int day)
    {
        year = 0;
        month = 0;
        day = 0;
        return text.Length == 8 &&
            int.TryParse(text[..4], NumberStyles.None, CultureInfo.InvariantCulture, out year) &&
            int.TryParse(text.Slice(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out month) &&
            int.TryParse(text.Slice(6, 2), NumberStyles.None, CultureInfo.InvariantCulture, out day);
    }

    private static bool TryParseClockDigits(ReadOnlySpan<char> text, out int hour, out int minute, out int second)
    {
        hour = 0;
        minute = 0;
        second = 0;
        return text.Length == 6 &&
            int.TryParse(text[..2], NumberStyles.None, CultureInfo.InvariantCulture, out hour) &&
            int.TryParse(text.Slice(2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out minute) &&
            int.TryParse(text.Slice(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out second);
    }

    private static DateTimeOffset? TryCreateTimestamp(int year, int month, int day, int hour, int minute, int second)
    {
        if (year is < 1980 or > 2100 ||
            month is < 1 or > 12 ||
            day < 1 ||
            day > DateTime.DaysInMonth(year, month) ||
            hour is < 0 or > 23 ||
            minute is < 0 or > 59 ||
            second is < 0 or > 59)
        {
            return null;
        }

        return new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero);
    }

    internal static bool ShouldIgnoreLoadedUserSid(string sid, string? currentSid) =>
        sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(sid, currentSid, StringComparison.OrdinalIgnoreCase);

    [SupportedOSPlatform("windows")]
    private static bool IsSid(string value)
    {
        try
        {
            _ = new SecurityIdentifier(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private void ReadEntries(
        RegistryHive hive,
        RegistryView view,
        string subKeyPath,
        string scope,
        List<InstalledSoftwareEntry> software,
        Dictionary<string, int> dedupe,
        CancellationToken cancellationToken)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstallKey = baseKey.OpenSubKey(subKeyPath);
            if (uninstallKey is not null)
            {
                AppendEntries(uninstallKey, scope, software, dedupe, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            logger.LogDebug(
                ex,
                "Skipped uninstall registry key {Hive}\\{View}\\{SubKeyPath} ({Scope}) because access was denied.",
                hive,
                view,
                subKeyPath,
                scope);
        }
    }

    [SupportedOSPlatform("windows")]
    private void AppendEntries(
        RegistryKey uninstallKey,
        string scope,
        List<InstalledSoftwareEntry> software,
        Dictionary<string, int> dedupe,
        CancellationToken cancellationToken)
    {
        string[] productKeyNames;
        try
        {
            productKeyNames = uninstallKey.GetSubKeyNames();
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            logger.LogDebug(ex, "Skipped enumerating uninstall subkeys for scope {Scope} because access was denied.", scope);
            return;
        }

        foreach (var productKeyName in productKeyNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var productKey = uninstallKey.OpenSubKey(productKeyName);
                if (productKey is null)
                {
                    continue;
                }

                TryCreateEntry(
                    productKey.GetValue("DisplayName") as string,
                    productKey.GetValue("DisplayVersion") as string,
                    productKey.GetValue("Publisher") as string,
                    productKey.GetValue("InstallLocation") as string,
                    scope,
                    dedupe,
                    software,
                    TryParseInstallDate(productKey.GetValue("InstallDate")));
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                logger.LogDebug(
                    ex,
                    "Skipped uninstall registry key {ProductKey} in scope {Scope} because access was denied.",
                    productKeyName,
                    scope);
            }
        }
    }

    private sealed record RegistryProbe(RegistryHive Hive, RegistryView View, string SubKeyPath, string Scope);
}
