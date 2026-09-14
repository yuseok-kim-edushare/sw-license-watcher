using System.Runtime.Versioning;
using Microsoft.Win32;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Core.Tests;

[SupportedOSPlatform("windows")]
public class UninstallRegistryVersionWriterTests
{
    [Fact]
    public void UpdateExistingKeys_writes_display_version_when_the_uninstall_key_exists()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Uninstall registry keys are Windows-only.");

        var path = @"Software\SwLicenseWatcher.Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(path, writable: true))
            {
                Assert.NotNull(key);
                key.SetValue(UninstallRegistryVersionWriter.DisplayVersionValueName, "1.0.0");
            }

            var written = UninstallRegistryVersionWriter.UpdateExistingKeys(
                "2.3.4",
                RegistryHive.CurrentUser,
                path,
                [RegistryView.Default]);

            Assert.Equal(1, written);
            using var updated = Registry.CurrentUser.OpenSubKey(path);
            Assert.Equal("2.3.4", updated?.GetValue(UninstallRegistryVersionWriter.DisplayVersionValueName));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void UpdateExistingKeys_does_not_create_a_missing_uninstall_key()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Uninstall registry keys are Windows-only.");

        var path = @"Software\SwLicenseWatcher.Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            var written = UninstallRegistryVersionWriter.UpdateExistingKeys(
                "2.3.4",
                RegistryHive.CurrentUser,
                path,
                [RegistryView.Default]);

            Assert.Equal(0, written);
            Assert.Null(Registry.CurrentUser.OpenSubKey(path));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void UpdateExistingKeys_skips_when_display_version_already_matches()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Uninstall registry keys are Windows-only.");

        var path = @"Software\SwLicenseWatcher.Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(path, writable: true))
            {
                Assert.NotNull(key);
                key.SetValue(UninstallRegistryVersionWriter.DisplayVersionValueName, "2.3.4");
            }

            var written = UninstallRegistryVersionWriter.UpdateExistingKeys(
                "2.3.4",
                RegistryHive.CurrentUser,
                path,
                [RegistryView.Default]);

            Assert.Equal(0, written);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        }
    }
}
