using SwLicenseWatcher.Agent.Worker;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Worker.Tests;

public class BundledWatchdogUpdaterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "slw-watchdog-bundle-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Apply_does_nothing_when_the_bundle_is_absent()
    {
        var services = new RecordingServices();

        await BundledWatchdogUpdater.ApplyIfPresentAsync(
            WorkerDirectory(),
            WatchdogDirectory(),
            BackupDirectory(),
            services,
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.Empty(services.Calls);
    }

    [Fact]
    public async Task Apply_removes_the_bundle_when_the_watchdog_version_already_matches()
    {
        var bundle = CreateBundle("1.2.3", "new");
        Directory.CreateDirectory(WatchdogDirectory());
        await File.WriteAllTextAsync(Path.Combine(WatchdogDirectory(), ".version"), "1.2.3");
        var services = new RecordingServices();

        await BundledWatchdogUpdater.ApplyIfPresentAsync(
            WorkerDirectory(),
            WatchdogDirectory(),
            BackupDirectory(),
            services,
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.Empty(services.Calls);
        Assert.False(Directory.Exists(bundle));
    }

    [Fact]
    public async Task Apply_backs_up_the_watchdog_and_keeps_its_appsettings()
    {
        CreateBundle("1.2.3", "new-watchdog");
        await File.WriteAllTextAsync(Path.Combine(CreateBundle("1.2.3", "new-watchdog"), "appsettings.json"), "package");
        Directory.CreateDirectory(WatchdogDirectory());
        await File.WriteAllTextAsync(Path.Combine(WatchdogDirectory(), AgentUpdateLayout.WatchdogExeName), "old-watchdog");
        await File.WriteAllTextAsync(Path.Combine(WatchdogDirectory(), "appsettings.json"), "installed");
        await File.WriteAllTextAsync(Path.Combine(WatchdogDirectory(), "stale.dll"), "stale");
        await File.WriteAllTextAsync(Path.Combine(WorkerDirectory(), AgentUpdateLayout.UpdateCommittedFileName), "1.2.3");
        var services = new RecordingServices();

        await BundledWatchdogUpdater.ApplyIfPresentAsync(
            WorkerDirectory(),
            WatchdogDirectory(),
            BackupDirectory(),
            services,
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.Equal(["stop", "start"], services.Calls);
        Assert.Equal("new-watchdog", await File.ReadAllTextAsync(Path.Combine(WatchdogDirectory(), AgentUpdateLayout.WatchdogExeName)));
        Assert.Equal("installed", await File.ReadAllTextAsync(Path.Combine(WatchdogDirectory(), "appsettings.json")));
        Assert.Equal("1.2.3", (await File.ReadAllTextAsync(Path.Combine(WatchdogDirectory(), ".version"))).Trim());
        Assert.False(File.Exists(Path.Combine(WatchdogDirectory(), "stale.dll")));
        Assert.Equal("old-watchdog", await File.ReadAllTextAsync(Path.Combine(BackupDirectory(), AgentUpdateLayout.WatchdogExeName)));
        Assert.False(Directory.Exists(Path.Combine(WorkerDirectory(), AgentUpdateLayout.WatchdogPayloadDirectoryName)));
    }

    [Fact]
    public async Task Apply_restores_the_backup_when_starting_the_new_watchdog_fails()
    {
        CreateBundle("1.2.3", "new-watchdog");
        Directory.CreateDirectory(WatchdogDirectory());
        await File.WriteAllTextAsync(Path.Combine(WatchdogDirectory(), AgentUpdateLayout.WatchdogExeName), "old-watchdog");
        await File.WriteAllTextAsync(Path.Combine(WorkerDirectory(), AgentUpdateLayout.UpdateCommittedFileName), "1.2.3");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BundledWatchdogUpdater.ApplyIfPresentAsync(
                WorkerDirectory(),
                WatchdogDirectory(),
                BackupDirectory(),
                new RecordingServices { FailFirstStart = true },
                TimeSpan.Zero,
                CancellationToken.None));

        Assert.Equal("start failed", ex.Message);
        Assert.Equal("old-watchdog", await File.ReadAllTextAsync(Path.Combine(WatchdogDirectory(), AgentUpdateLayout.WatchdogExeName)));
        Assert.True(Directory.Exists(Path.Combine(WorkerDirectory(), AgentUpdateLayout.WatchdogPayloadDirectoryName)));
    }

    [Fact]
    public async Task Apply_waits_until_the_worker_deploy_is_committed()
    {
        CreateBundle("1.2.3", "new-watchdog");
        Directory.CreateDirectory(WatchdogDirectory());
        await File.WriteAllTextAsync(Path.Combine(WatchdogDirectory(), AgentUpdateLayout.WatchdogExeName), "old-watchdog");
        var started = DateTime.UtcNow;
        var write = Task.Run(async () =>
        {
            await Task.Delay(300);
            await File.WriteAllTextAsync(Path.Combine(WorkerDirectory(), AgentUpdateLayout.UpdateCommittedFileName), "1.2.3");
        });

        await BundledWatchdogUpdater.ApplyIfPresentAsync(
            WorkerDirectory(),
            WatchdogDirectory(),
            BackupDirectory(),
            new RecordingServices(),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);
        await write;

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
        Assert.Equal("new-watchdog", await File.ReadAllTextAsync(Path.Combine(WatchdogDirectory(), AgentUpdateLayout.WatchdogExeName)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private string WorkerDirectory() => Path.Combine(_root, "Agent.Worker");

    private string WatchdogDirectory() => Path.Combine(_root, "Agent.Watchdog");

    private string BackupDirectory() => Path.Combine(_root, "backup", "watchdog-previous");

    private string CreateBundle(string version, string executable)
    {
        var bundle = Path.Combine(WorkerDirectory(), AgentUpdateLayout.WatchdogPayloadDirectoryName);
        Directory.CreateDirectory(bundle);
        File.WriteAllText(Path.Combine(bundle, AgentUpdateLayout.WatchdogExeName), executable);
        File.WriteAllText(Path.Combine(bundle, ".version"), version);
        File.WriteAllText(Path.Combine(bundle, "appsettings.json"), "package");
        return bundle;
    }

    private sealed class RecordingServices : IWatchdogServiceControl
    {
        public List<string> Calls { get; } = [];

        public bool FailFirstStart { get; init; }

        public void StopForReplacement(string serviceName, string installDirectory) => Calls.Add("stop");

        public void Start(string serviceName)
        {
            Calls.Add("start");
            if (FailFirstStart && Calls.Count(call => call == "start") == 1)
            {
                throw new InvalidOperationException("start failed");
            }
        }
    }
}
