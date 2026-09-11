using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Worker.Tests;

public class AgentUninstallCommandTests
{
    [Theory]
    [InlineData("""{"UninstallCommand":{"Id":12,"Code":"ABCD2345"}}""", 12, "ABCD2345")]
    [InlineData("""{"uninstallCommand":{"id":9,"code":"ZZYYXXWW"}}""", 9, "ZZYYXXWW")]
    public void TryReadUninstallCommand_reads_pascal_or_camel_case(string json, long id, string code)
    {
        Assert.True(AgentAssignmentStore.TryReadUninstallCommand(json, out var command));
        Assert.Equal(id, command!.Id);
        Assert.Equal(code, command.Code);
    }

    [Theory]
    [InlineData("""{"AssignedHostName":"pc-01"}""")]
    [InlineData("""{"uninstallCommand":null}""")]
    [InlineData("""{"uninstallCommand":{"id":0,"code":"ABCD2345"}}""")]
    [InlineData("")]
    public void TryReadUninstallCommand_rejects_missing_or_invalid_payloads(string json)
    {
        Assert.False(AgentAssignmentStore.TryReadUninstallCommand(json, out var command));
        Assert.Null(command);
    }
}

public class RemoteAgentUninstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "slw-uninst-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ApplyLocal_removes_agent_directories_without_touching_unrelated_files()
    {
        var watchdog = Path.Combine(_root, "Agent.Watchdog");
        var worker = Path.Combine(_root, "Agent.Worker");
        Directory.CreateDirectory(watchdog);
        Directory.CreateDirectory(worker);
        File.WriteAllText(Path.Combine(watchdog, "watchdog.txt"), "w");
        File.WriteAllText(Path.Combine(worker, "worker.txt"), "k");
        File.WriteAllText(Path.Combine(_root, "keep.txt"), "keep");

        var services = new RecordingServiceControl();
        RemoteAgentUninstaller.ApplyLocal(_root, services);

        Assert.Equal(
            [RemoteAgentUninstaller.WatchdogServiceName, RemoteAgentUninstaller.WorkerServiceName],
            services.Deleted);
        Assert.False(Directory.Exists(watchdog));
        Assert.False(Directory.Exists(worker));
        Assert.True(File.Exists(Path.Combine(_root, "keep.txt")));
    }

    [Fact]
    public void TryApplyFromArgs_ignores_unrelated_arguments()
    {
        Assert.False(RemoteAgentUninstaller.TryApplyFromArgs(["--Agent:RunOnceForDiagnostics=true"]));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private sealed class RecordingServiceControl : IAgentWindowsServiceControl
    {
        public List<string> Deleted { get; } = [];

        public void StopAndDelete(string serviceName) => Deleted.Add(serviceName);
    }
}
