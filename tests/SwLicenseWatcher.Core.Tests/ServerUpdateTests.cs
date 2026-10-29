using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Core.Tests;

public class ServerUpdateTests
{
    [Fact]
    public void Detects_windows_service_iis_and_linux()
    {
        Assert.True(ServerUpdateHosts.TryDetect(true, false, null, null, out var windows));
        Assert.Equal(ServerUpdateHostKind.WindowsService, windows);
        Assert.True(ServerUpdateHosts.TryDetect(true, false, "SwLicenseWatcher", null, out var iis));
        Assert.Equal(ServerUpdateHostKind.Iis, iis);
        Assert.True(ServerUpdateHosts.TryDetect(true, false, null, @"C:\inetpub\sw", out var iisPath));
        Assert.Equal(ServerUpdateHostKind.Iis, iisPath);
        Assert.True(ServerUpdateHosts.TryDetect(false, true, null, null, out var linux));
        Assert.Equal(ServerUpdateHostKind.Linux, linux);
        Assert.False(ServerUpdateHosts.TryDetect(false, false, null, null, out _));
    }

    [Fact]
    public void Names_a_different_package_for_each_host()
    {
        Assert.Equal("SwLicenseWatcher.Api.win-x64-1.2.3.zip", ServerUpdatePackageNames.ZipFileName(ServerUpdateHostKind.WindowsService, "v1.2.3"));
        Assert.Equal("SwLicenseWatcher.Api.iis-win-x64-1.2.3.zip", ServerUpdatePackageNames.ZipFileName(ServerUpdateHostKind.Iis, "1.2.3"));
        Assert.Equal("SwLicenseWatcher.Api.linux-x64-1.2.3.zip", ServerUpdatePackageNames.ZipFileName(ServerUpdateHostKind.Linux, "1.2.3"));
    }

    [Fact]
    public void Preserves_settings_and_the_device_ca_key()
    {
        Assert.True(ServerUpdateFiles.IsPreserved("appsettings.json"));
        Assert.True(ServerUpdateFiles.IsPreserved("appsettings.Production.json"));
        Assert.True(ServerUpdateFiles.IsPreserved("device-ca.mldsa87.json"));
        Assert.False(ServerUpdateFiles.IsPreserved("SwLicenseWatcher.Api.exe"));
        Assert.False(ServerUpdateFiles.IsPreserved(Path.Combine("wwwroot", "admin", "index.html")));
        Assert.True(ServerUpdateFiles.IsHelperPayload("update-helper"));
    }

    [Fact]
    public void Round_trips_a_helper_config_and_rejects_a_nested_staging_path()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "slw-server-update"));
        var config = new ServerUpdateHelperConfig
        {
            HostKind = ServerUpdateHostKind.WindowsService,
            InstallPath = Path.Combine(root, "Api"),
            ServiceName = "SwLicenseWatcher.Api",
            StagingPath = Path.Combine(root, "staging"),
            HealthUrl = "https://127.0.0.1/health"
        };

        Assert.True(ServerUpdateConfigFile.TryParse(ServerUpdateConfigFile.Format(config), out var parsed, out var error), error);
        Assert.NotNull(parsed);
        Assert.Equal(ServerUpdateHostKind.WindowsService, parsed.HostKind);
        Assert.True(ServerUpdateConfigFile.PathsEqual(config.InstallPath, parsed.InstallPath));
        Assert.Equal("https://127.0.0.1/health", parsed.HealthUrl);

        var nested = ServerUpdateConfigFile.Format(config).Replace(
            Path.Combine(root, "staging"),
            Path.Combine(root, "Api", "staging"),
            StringComparison.OrdinalIgnoreCase);
        Assert.False(ServerUpdateConfigFile.TryParse(nested, out _, out error));
        Assert.Contains("StagingPath", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Install_hint_names_the_manual_helper_for_the_host()
    {
        Assert.Contains("Install-ApiUpdateHelper.ps1", ServerUpdateConfigFile.InstallHint(ServerUpdateHostKind.WindowsService, @"C:\Api", null), StringComparison.Ordinal);
        Assert.Contains("-AppPool", ServerUpdateConfigFile.InstallHint(ServerUpdateHostKind.Iis, @"C:\inetpub\sw", "SwLicenseWatcher"), StringComparison.Ordinal);
        Assert.Contains("install-api-update-helper.sh", ServerUpdateConfigFile.InstallHint(ServerUpdateHostKind.Linux, "/opt/sw-license-watcher", null), StringComparison.Ordinal);
    }
}
