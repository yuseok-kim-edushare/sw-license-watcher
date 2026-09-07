using System.IO.Compression;
using SwLicenseWatcher.Setup.Core;

namespace SwLicenseWatcher.Setup.Tests;

public class ReleaseLayoutTests
{
    [Fact]
    public void Finds_nested_agent_folders_and_reads_version()
    {
        using var dir = new TempDirectory();
        var root = Path.Combine(dir.Path, "SwLicenseWatcher-1.2.3");
        var worker = Path.Combine(root, "agent-worker", "win-x64");
        var watchdog = Path.Combine(root, "agent-watchdog", "win-x64");
        Directory.CreateDirectory(worker);
        Directory.CreateDirectory(watchdog);
        File.WriteAllText(Path.Combine(worker, PayloadLayout.WorkerExe), "w");
        File.WriteAllText(Path.Combine(worker, PayloadLayout.VersionFileName), "1.2.3");
        File.WriteAllText(Path.Combine(watchdog, PayloadLayout.WatchdogExe), "d");

        Assert.True(ReleaseLayout.TryFindAgents(dir.Path, out var foundWorker, out var foundWatchdog));
        Assert.Equal(worker, foundWorker);
        Assert.Equal(watchdog, foundWatchdog);
        Assert.Equal("1.2.3", ReleaseLayout.ReadVersion(foundWorker));
    }

    [Fact]
    public void Extracts_agents_from_a_release_zip()
    {
        using var dir = new TempDirectory();
        var zipPath = Path.Combine(dir.Path, "release.zip");
        using (var stream = File.Create(zipPath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            Write(archive, "SwLicenseWatcher-1.0.0/agent-worker/win-x64/SwLicenseWatcher.Agent.Worker.exe", "w");
            Write(archive, "SwLicenseWatcher-1.0.0/agent-worker/win-x64/.version", "1.0.0");
            Write(archive, "SwLicenseWatcher-1.0.0/agent-watchdog/win-x64/SwLicenseWatcher.Agent.Watchdog.exe", "d");
            Write(archive, "SwLicenseWatcher-1.0.0/api/iis/win-x64/ignored.dll", "no");
        }

        var extracted = Path.Combine(dir.Path, "out");
        ReleaseLayout.ExtractAgentsFromReleaseZip(zipPath, extracted);
        Assert.True(ReleaseLayout.TryFindAgents(extracted, out var worker, out _));
        Assert.Equal("1.0.0", ReleaseLayout.ReadVersion(worker));
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
