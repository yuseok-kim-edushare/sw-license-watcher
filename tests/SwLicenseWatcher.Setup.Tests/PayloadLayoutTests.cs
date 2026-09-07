using SwLicenseWatcher.Setup.Core;

namespace SwLicenseWatcher.Setup.Tests;

public class PayloadLayoutTests
{
    [Fact]
    public void Build_and_extract_roundtrip_includes_required_files()
    {
        using var dir = new TempDirectory();
        var ui = Path.Combine(dir.Path, "ui", PayloadLayout.SetupUiFileName);
        var worker = Path.Combine(dir.Path, "worker");
        var watchdog = Path.Combine(dir.Path, "watchdog");
        Directory.CreateDirectory(Path.GetDirectoryName(ui)!);
        Directory.CreateDirectory(worker);
        Directory.CreateDirectory(watchdog);
        File.WriteAllText(ui, "ui");
        File.WriteAllText(Path.Combine(worker, PayloadLayout.WorkerExe), "worker");
        File.WriteAllText(Path.Combine(worker, PayloadLayout.VersionFileName), "1.2.3");
        File.WriteAllText(Path.Combine(watchdog, PayloadLayout.WatchdogExe), "watchdog");

        var settings = new CompanySettings
        {
            ServerBaseUrl = "https://license-watcher.example.local",
            AgentToken = new string('a', 32),
            Version = "1.2.3"
        };

        var zip = PayloadZipBuilder.Build(settings, ui, worker, watchdog);
        var extracted = PayloadExtractor.ExtractZip(zip, Path.Combine(dir.Path, "extract"));

        PayloadZipBuilder.ValidateLayout(extracted);
        var loaded = CompanySettingsStore.Load(PayloadLayout.GetCompanyJsonPath(extracted));
        Assert.Equal(settings.ServerBaseUrl, loaded.ServerBaseUrl);
        Assert.Equal(settings.AgentToken, loaded.AgentToken);
        Assert.Equal("1.2.3", loaded.Version);
        Assert.True(File.Exists(PayloadLayout.GetSetupUiPath(extracted)));
        Assert.True(File.Exists(PayloadLayout.GetWorkerExePath(extracted)));
        Assert.True(File.Exists(PayloadLayout.GetWatchdogExePath(extracted)));
    }

    [Fact]
    public void Build_requires_agent_executables()
    {
        using var dir = new TempDirectory();
        var ui = Path.Combine(dir.Path, PayloadLayout.SetupUiFileName);
        File.WriteAllText(ui, "ui");
        var settings = new CompanySettings
        {
            ServerBaseUrl = "http://127.0.0.1:5080",
            AgentToken = new string('b', 32),
            Version = "0.0.1"
        };

        Assert.Throws<FileNotFoundException>(() =>
            PayloadZipBuilder.Build(settings, ui, dir.Path, dir.Path));
    }
}
