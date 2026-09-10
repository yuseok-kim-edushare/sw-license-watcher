using SwLicenseWatcher.Setup.Core;

namespace SwLicenseWatcher.Setup.Tests;

public class PackagingWorkflowTests
{
    [Fact]
    public void Build_discovers_release_assets_and_delegates_package_creation()
    {
        using var directory = new TempDirectory();
        var release = Path.Combine(directory.Path, "release");
        var setupUiDirectory = Path.Combine(release, PayloadLayout.SetupUiDirectory);
        var worker = PayloadLayout.GetWorkerDirectory(release);
        var watchdog = PayloadLayout.GetWatchdogDirectory(release);
        Directory.CreateDirectory(setupUiDirectory);
        Directory.CreateDirectory(worker);
        Directory.CreateDirectory(watchdog);
        var launcher = Path.Combine(release, PayloadLayout.LauncherFileName);
        var setupUi = Path.Combine(setupUiDirectory, PayloadLayout.SetupUiFileName);
        File.WriteAllText(launcher, "launcher");
        File.WriteAllText(setupUi, "setup");
        File.WriteAllText(Path.Combine(worker, PayloadLayout.WorkerExe), "worker");
        File.WriteAllText(Path.Combine(watchdog, PayloadLayout.WatchdogExe), "watchdog");
        var output = Path.Combine(directory.Path, "company", "Setup.exe");
        var packager = new RecordingPackager();
        var sut = new PackagingWorkflow(packager);

        sut.Build(new PackagingRequest(
            "https://license.example.local",
            new string('t', 32),
            null,
            "  " + output + "  ",
            release,
            Path.Combine(directory.Path, "missing")));

        Assert.NotNull(packager.Request);
        Assert.Equal(launcher, packager.Request.Value.Launcher);
        Assert.Equal(setupUi, packager.Request.Value.SetupUi);
        Assert.Equal(worker, packager.Request.Value.Worker);
        Assert.Equal(watchdog, packager.Request.Value.Watchdog);
        Assert.Equal(output, packager.Request.Value.Output);
    }

    [Fact]
    public void Build_preserves_missing_launcher_error()
    {
        using var directory = new TempDirectory();
        var sut = new PackagingWorkflow(new RecordingPackager());

        var error = Assert.Throws<InvalidOperationException>(() => sut.Build(new PackagingRequest(
            "https://license.example.local",
            new string('t', 32),
            null,
            Path.Combine(directory.Path, "Setup.exe"),
            directory.Path,
            directory.Path)));

        Assert.Equal(
            "런처 뼈대(SwLicenseWatcher-Setup.exe)를 패키저 옆에서 찾지 못했습니다.",
            error.Message);
    }

    private sealed class RecordingPackager : ICompanyPackager
    {
        public (
            string Launcher,
            string SetupUi,
            string Worker,
            string Watchdog,
            string Output)? Request { get; private set; }

        public void Build(
            string serverBaseUrl,
            string agentToken,
            string launcherStubPath,
            string setupUiPath,
            string workerDirectory,
            string watchdogDirectory,
            string outputExePath)
        {
            Request = (launcherStubPath, setupUiPath, workerDirectory, watchdogDirectory, outputExePath);
        }
    }
}
