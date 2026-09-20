using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Agent.Worker;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Worker.Tests;

public class UserMessageCommandTests
{
    [Theory]
    [InlineData("""{"UserMessageCommand":{"Id":12,"Title":"점검","Body":"10분 후"}}""", 12, "점검", "10분 후")]
    [InlineData("""{"userMessageCommand":{"id":9,"title":"A","body":"B"}}""", 9, "A", "B")]
    public void TryReadUserMessageCommand_reads_pascal_or_camel_case(string json, long id, string title, string body)
    {
        Assert.True(AgentAssignmentStore.TryReadUserMessageCommand(json, out var command));
        Assert.Equal(id, command!.Id);
        Assert.Equal(title, command.Title);
        Assert.Equal(body, command.Body);
    }

    [Theory]
    [InlineData("""{"AssignedHostName":"pc-01"}""")]
    [InlineData("""{"userMessageCommand":null}""")]
    [InlineData("""{"userMessageCommand":{"id":0,"title":"A","body":"B"}}""")]
    [InlineData("")]
    public void TryReadUserMessageCommand_rejects_missing_or_invalid_payloads(string json)
    {
        Assert.False(AgentAssignmentStore.TryReadUserMessageCommand(json, out var command));
        Assert.Null(command);
    }
}

public class SseEventReaderTests
{
    [Fact]
    public async Task ReadAsync_parses_user_message_and_ignores_ping()
    {
        var payload = ": ping\n\nevent: user-message\ndata: {\"id\":4,\"title\":\"T\",\"body\":\"B\"}\n\n";
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload));
        var events = new List<SseEvent>();
        await foreach (var item in SseEventReader.ReadAsync(stream, CancellationToken.None))
        {
            events.Add(item);
        }

        Assert.Single(events);
        Assert.True(SseEventReader.TryParseUserMessage(events[0], out var command));
        Assert.Equal(4, command!.Id);
        Assert.Equal("T", command.Title);
        Assert.Equal("B", command.Body);
    }
}

public class UserToastLauncherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "slw-toast-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ResolveHelperPath_prefers_toast_subdirectory()
    {
        Directory.CreateDirectory(Path.Combine(_root, "toast"));
        File.WriteAllText(Path.Combine(_root, "toast", UserToastLauncher.HelperFileName), "helper");
        File.WriteAllText(Path.Combine(_root, UserToastLauncher.HelperFileName), "sibling");

        Assert.Equal(
            Path.Combine(_root, "toast", UserToastLauncher.HelperFileName),
            UserToastLauncher.ResolveHelperPath(_root));
    }

    [Fact]
    public async Task ShowAsync_returns_true_only_when_helper_exits_zero()
    {
        Directory.CreateDirectory(Path.Combine(_root, "queue"));
        File.WriteAllText(Path.Combine(_root, UserToastLauncher.HelperFileName), "helper");
        var starter = new RecordingStarter { ExitCode = 0 };
        var launcher = new UserToastLauncher(
            NullLogger<UserToastLauncher>.Instance,
            starter,
            Options.Create(new LocalStateStoreOptions { QueueDirectory = Path.Combine(_root, "queue") }));

        Assert.True(await launcher.ShowAsync(new AgentUserMessageCommand(8, "T", "B"), _root, CancellationToken.None));
        Assert.Equal(1, starter.Calls);
        Assert.NotNull(starter.PayloadPath);
        Assert.False(File.Exists(starter.PayloadPath));

        starter.ExitCode = 1;
        Assert.False(await launcher.ShowAsync(new AgentUserMessageCommand(9, "T", "B"), _root, CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private sealed class RecordingStarter : IToastHelperProcessStarter
    {
        public int? ExitCode { get; set; }
        public int Calls { get; private set; }
        public string? PayloadPath { get; private set; }

        public int? StartAndWait(string exePath, string payloadPath, TimeSpan timeout)
        {
            Calls++;
            PayloadPath = payloadPath;
            Assert.True(File.Exists(payloadPath));
            return ExitCode;
        }
    }
}
