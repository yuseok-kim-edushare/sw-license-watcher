using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Principal;

namespace SwLicenseWatcher.Core;

public interface ISoftwareInventoryCollector
{
    Task<IReadOnlyCollection<InstalledSoftwareEntry>> CollectAsync(CancellationToken cancellationToken);
}

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

        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
    private void ReadCurrentUserEntries(List<InstalledSoftwareEntry> software, HashSet<string> dedupe, CancellationToken cancellationToken)
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
    private void ReadLoadedUserEntries(List<InstalledSoftwareEntry> software, HashSet<string> dedupe, CancellationToken cancellationToken)
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
        HashSet<string> dedupe)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var dedupeKey = string.Join('|', displayName, version, publisher, installLocation, scope);
        if (!dedupe.Add(dedupeKey))
        {
            return null;
        }

        return new InstalledSoftwareEntry(
            displayName,
            string.IsNullOrWhiteSpace(version) ? null : version,
            string.IsNullOrWhiteSpace(publisher) ? null : publisher,
            string.IsNullOrWhiteSpace(installLocation) ? null : installLocation,
            scope,
            "Registry.Uninstall");
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
        HashSet<string> dedupe,
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
        HashSet<string> dedupe,
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

                var entry = TryCreateEntry(
                    productKey.GetValue("DisplayName") as string,
                    productKey.GetValue("DisplayVersion") as string,
                    productKey.GetValue("Publisher") as string,
                    productKey.GetValue("InstallLocation") as string,
                    scope,
                    dedupe);
                if (entry is not null)
                {
                    software.Add(entry);
                }
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
