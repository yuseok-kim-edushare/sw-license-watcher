using System.Collections.Concurrent;
using SwLicenseWatcher.Api;

namespace SwLicenseWatcher.Api.Tests;

public class StaleHeartbeatMonitorTests
{
    [Fact]
    public void TakeNewlyStale_returns_every_pc_on_the_first_detection()
    {
        var notified = NewNotifiedSet();
        var stale = new[]
        {
            Pc("PC-001", "HOST-A"),
            Pc("PC-002", "HOST-B")
        };

        var newlyStale = StaleHeartbeatMonitor.TakeNewlyStale(stale, notified);

        Assert.Equal(["PC-001", "PC-002"], newlyStale.Select(pc => pc.DeviceCode));
        Assert.True(notified.ContainsKey("PC-001"));
        Assert.True(notified.ContainsKey("PC-002"));
    }

    [Fact]
    public void TakeNewlyStale_does_not_requeue_a_pc_that_is_still_stale()
    {
        var notified = NewNotifiedSet();
        var stale = new[] { Pc("PC-001", "HOST-A"), Pc("PC-002", "HOST-B") };
        StaleHeartbeatMonitor.TakeNewlyStale(stale, notified);

        var again = StaleHeartbeatMonitor.TakeNewlyStale(stale, notified);

        Assert.Empty(again);
        Assert.Equal(2, notified.Count);
    }

    [Fact]
    public void TakeNewlyStale_treats_device_codes_case_insensitively_when_suppressing_duplicates()
    {
        var notified = NewNotifiedSet();
        StaleHeartbeatMonitor.TakeNewlyStale([Pc("PC-001", "HOST-A")], notified);

        var again = StaleHeartbeatMonitor.TakeNewlyStale([Pc("pc-001", "HOST-A")], notified);

        Assert.Empty(again);
        Assert.True(notified.ContainsKey("PC-001"));
    }

    [Fact]
    public void TakeNewlyStale_forgets_recovered_pcs_and_can_notify_them_again()
    {
        var notified = NewNotifiedSet();
        StaleHeartbeatMonitor.TakeNewlyStale(
            [Pc("PC-001", "HOST-A"), Pc("PC-002", "HOST-B")],
            notified);

        var remaining = StaleHeartbeatMonitor.TakeNewlyStale([Pc("PC-002", "HOST-B")], notified);
        Assert.Empty(remaining);
        Assert.False(notified.ContainsKey("PC-001"));
        Assert.True(notified.ContainsKey("PC-002"));

        var relapsed = StaleHeartbeatMonitor.TakeNewlyStale(
            [Pc("PC-001", "HOST-A"), Pc("PC-002", "HOST-B")],
            notified);

        var pc = Assert.Single(relapsed);
        Assert.Equal("PC-001", pc.DeviceCode);
        Assert.True(notified.ContainsKey("PC-001"));
    }

    [Fact]
    public void TakeNewlyStale_returns_only_pcs_that_were_not_already_notified()
    {
        var notified = NewNotifiedSet();
        StaleHeartbeatMonitor.TakeNewlyStale([Pc("PC-001", "HOST-A")], notified);

        var newlyStale = StaleHeartbeatMonitor.TakeNewlyStale(
            [Pc("PC-001", "HOST-A"), Pc("PC-003", "HOST-C")],
            notified);

        var pc = Assert.Single(newlyStale);
        Assert.Equal("PC-003", pc.DeviceCode);
        Assert.Equal("HOST-C", pc.HostName);
    }

    [Fact]
    public void TakeNewlyStale_clears_the_notified_set_when_nothing_is_stale()
    {
        var notified = NewNotifiedSet();
        StaleHeartbeatMonitor.TakeNewlyStale([Pc("PC-001", "HOST-A")], notified);

        Assert.Empty(StaleHeartbeatMonitor.TakeNewlyStale([], notified));
        Assert.Empty(notified);
    }

    private static ConcurrentDictionary<string, byte> NewNotifiedSet() =>
        new(StringComparer.OrdinalIgnoreCase);

    private static StalePcHeartbeat Pc(string deviceCode, string hostName) =>
        new(deviceCode, hostName, new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero));
}
