using Microsoft.Win32;
using System.Runtime.Versioning;

namespace SwLicenseWatcher.Core;

public interface ISoftwareInventoryCollector
{
    Task<IReadOnlyCollection<InstalledSoftwareEntry>> CollectAsync(CancellationToken cancellationToken);
}

public sealed class RegistrySoftwareInventoryCollector : ISoftwareInventoryCollector
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
    private static IReadOnlyCollection<InstalledSoftwareEntry> CollectWindowsEntries(CancellationToken cancellationToken)
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

        return software;
    }

    [SupportedOSPlatform("windows")]
    private static void ReadCurrentUserEntries(List<InstalledSoftwareEntry> software, HashSet<string> dedupe, CancellationToken cancellationToken)
    {
        using var currentUserKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
        if (currentUserKey is null)
        {
            return;
        }

        AppendEntries(currentUserKey, "User", software, dedupe, cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static void ReadEntries(
        RegistryHive hive,
        RegistryView view,
        string subKeyPath,
        string scope,
        List<InstalledSoftwareEntry> software,
        HashSet<string> dedupe,
        CancellationToken cancellationToken)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var uninstallKey = baseKey.OpenSubKey(subKeyPath);
        if (uninstallKey is null)
        {
            return;
        }

        AppendEntries(uninstallKey, scope, software, dedupe, cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private static void AppendEntries(
        RegistryKey uninstallKey,
        string scope,
        List<InstalledSoftwareEntry> software,
        HashSet<string> dedupe,
        CancellationToken cancellationToken)
    {
        foreach (var productKeyName in uninstallKey.GetSubKeyNames())
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var productKey = uninstallKey.OpenSubKey(productKeyName);
            if (productKey is null)
            {
                continue;
            }

            var displayName = productKey.GetValue("DisplayName") as string;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                continue;
            }

            var version = productKey.GetValue("DisplayVersion") as string;
            var publisher = productKey.GetValue("Publisher") as string;
            var installLocation = productKey.GetValue("InstallLocation") as string;
            var dedupeKey = string.Join('|', displayName, version, publisher, installLocation, scope);
            if (!dedupe.Add(dedupeKey))
            {
                continue;
            }

            software.Add(new InstalledSoftwareEntry(
                displayName,
                string.IsNullOrWhiteSpace(version) ? null : version,
                string.IsNullOrWhiteSpace(publisher) ? null : publisher,
                string.IsNullOrWhiteSpace(installLocation) ? null : installLocation,
                scope,
                "Registry.Uninstall"));
        }
    }

    private sealed record RegistryProbe(RegistryHive Hive, RegistryView View, string SubKeyPath, string Scope);
}
