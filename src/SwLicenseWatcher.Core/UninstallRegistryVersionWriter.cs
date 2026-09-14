using System.Runtime.Versioning;
using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace SwLicenseWatcher.Core;

public interface IUninstallRegistryVersionWriter
{
    void TryUpdateDisplayVersion(string version);
}

public sealed class UninstallRegistryVersionWriter(ILogger<UninstallRegistryVersionWriter> logger)
    : IUninstallRegistryVersionWriter
{
    public const string SubKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\SwLicenseWatcher";
    public const string DisplayVersionValueName = "DisplayVersion";

    public void TryUpdateDisplayVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version) || !OperatingSystem.IsWindows())
        {
            return;
        }

        UpdateOnWindows(version.Trim());
    }

    [SupportedOSPlatform("windows")]
    private void UpdateOnWindows(string version)
    {
        try
        {
            var written = UpdateExistingKeys(
                version,
                RegistryHive.LocalMachine,
                SubKeyPath,
                [RegistryView.Registry64, RegistryView.Registry32]);
            if (written > 0)
            {
                logger.LogInformation(
                    "Updated the uninstall registry DisplayVersion to {Version}.",
                    version);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            logger.LogWarning(ex, "Unable to update the uninstall registry DisplayVersion to {Version}.", version);
        }
    }

    [SupportedOSPlatform("windows")]
    internal static int UpdateExistingKeys(
        string version,
        RegistryHive hive,
        string subKeyPath,
        IReadOnlyList<RegistryView> views)
    {
        var written = 0;
        foreach (var view in views)
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(subKeyPath, writable: true);
            if (key is null)
            {
                continue;
            }

            var current = key.GetValue(DisplayVersionValueName) as string;
            if (string.Equals(current, version, StringComparison.Ordinal))
            {
                continue;
            }

            key.SetValue(DisplayVersionValueName, version, RegistryValueKind.String);
            written++;
        }

        return written;
    }
}
