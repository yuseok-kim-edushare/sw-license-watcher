using SwLicenseWatcher.Api;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api.Tests;

public class UserMessageEventHubTests
{
    [Fact]
    public async Task Publish_reaches_a_subscriber_for_the_device_alias()
    {
        var hub = new UserMessageEventHub();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = hub.Subscribe("PC-01", cts.Token).GetAsyncEnumerator(cts.Token);
        var wait = received.MoveNextAsync();
        hub.Publish(["pc-01", "ASSET-01"], new AgentUserMessageCommand(7, "제목", "본문"));

        Assert.True(await wait);
        Assert.Equal(7, received.Current.Id);
        Assert.Equal("제목", received.Current.Title);
        await received.DisposeAsync();
    }
}
