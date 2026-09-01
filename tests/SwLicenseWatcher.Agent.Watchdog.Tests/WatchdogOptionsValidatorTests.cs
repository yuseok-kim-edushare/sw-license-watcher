using SwLicenseWatcher.Agent.Watchdog;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Watchdog.Tests;

public class WatchdogOptionsValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "slw-watchdog-dirs-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void HasSafeDirectories_accepts_distinct_non_overlapping_paths()
    {
        var options = Directories(
            Path.Combine(_root, "staging"),
            Path.Combine(_root, "backup"),
            Path.Combine(_root, "install"));

        Assert.True(WatchdogOptionsValidator.HasSafeDirectories(options));
    }

    [Fact]
    public void HasSafeDirectories_accepts_sibling_names_that_share_a_prefix()
    {
        var options = Directories(
            Path.Combine(_root, "stage"),
            Path.Combine(_root, "staging"),
            Path.Combine(_root, "install"));

        Assert.True(WatchdogOptionsValidator.HasSafeDirectories(options));
    }

    [Fact]
    public void HasSafeDirectories_rejects_blank_directories()
    {
        Assert.False(WatchdogOptionsValidator.HasSafeDirectories(Directories("", Path.Combine(_root, "backup"), Path.Combine(_root, "install"))));
        Assert.False(WatchdogOptionsValidator.HasSafeDirectories(Directories(Path.Combine(_root, "staging"), " ", Path.Combine(_root, "install"))));
        Assert.False(WatchdogOptionsValidator.HasSafeDirectories(Directories(Path.Combine(_root, "staging"), Path.Combine(_root, "backup"), "\t")));
    }

    [Fact]
    public void HasSafeDirectories_rejects_duplicate_directories()
    {
        var shared = Path.Combine(_root, "shared");
        var options = Directories(shared, shared, Path.Combine(_root, "install"));

        Assert.False(WatchdogOptionsValidator.HasSafeDirectories(options));
    }

    [Fact]
    public void HasSafeDirectories_rejects_nested_directories()
    {
        var staging = Path.Combine(_root, "staging");
        var options = Directories(staging, Path.Combine(staging, "backup"), Path.Combine(_root, "install"));

        Assert.False(WatchdogOptionsValidator.HasSafeDirectories(options));
    }

    [Fact]
    public void HasSafeDirectories_rejects_a_drive_root()
    {
        var options = Directories(
            Path.Combine(_root, "staging"),
            Path.Combine(_root, "backup"),
            Path.GetPathRoot(Path.GetTempPath())!);

        Assert.False(WatchdogOptionsValidator.HasSafeDirectories(options));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static WatchdogOptions Directories(string staging, string backup, string install) =>
        new()
        {
            StagingDirectory = staging,
            BackupDirectory = backup,
            WorkerInstallDirectory = install
        };
}
