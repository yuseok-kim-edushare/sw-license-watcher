using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Api;
using SwLicenseWatcher.Core;

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

    [Fact]
    public async Task CheckOnceAsync_persists_notifications_so_a_restarted_monitor_does_not_duplicate()
    {
        var store = new InMemoryStaleHeartbeatStore();
        store.SetPc("PC-001", "HOST-A", DateTimeOffset.UtcNow.AddDays(-2));
        var (firstMonitor, firstPublisher) = CreateMonitor(store);

        await firstMonitor.CheckOnceAsync(CancellationToken.None);

        Assert.True(firstPublisher.Reader.TryRead(out var first));
        Assert.Contains("HOST-A", first.Subject, StringComparison.Ordinal);
        Assert.True(store.WasNotified("PC-001"));

        var (restartedMonitor, restartedPublisher) = CreateMonitor(store);
        await restartedMonitor.CheckOnceAsync(CancellationToken.None);

        Assert.False(restartedPublisher.Reader.TryRead(out _));
        Assert.True(store.WasNotified("PC-001"));
    }

    [Fact]
    public async Task CheckOnceAsync_notifies_again_after_a_heartbeat_recovery_and_later_relapse()
    {
        var store = new InMemoryStaleHeartbeatStore();
        store.SetPc("PC-001", "HOST-A", DateTimeOffset.UtcNow.AddDays(-2));
        var (monitor, publisher) = CreateMonitor(store, TimeSpan.FromHours(24));

        await monitor.CheckOnceAsync(CancellationToken.None);
        Assert.True(publisher.Reader.TryRead(out _));

        store.RecordHeartbeat("PC-001", "HOST-A", DateTimeOffset.UtcNow);
        await monitor.CheckOnceAsync(CancellationToken.None);
        Assert.False(publisher.Reader.TryRead(out _));
        Assert.False(store.WasNotified("PC-001"));

        store.SetPc("PC-001", "HOST-A", DateTimeOffset.UtcNow.AddDays(-2));
        await monitor.CheckOnceAsync(CancellationToken.None);

        Assert.True(publisher.Reader.TryRead(out var relapsed));
        Assert.Contains("HOST-A", relapsed.Subject, StringComparison.Ordinal);
        Assert.True(store.WasNotified("PC-001"));
    }

    [Fact]
    public async Task ClaimNewlyStaleHeartbeatsAsync_records_notified_at_and_survives_a_new_store_consumer()
    {
        var store = new InMemoryStaleHeartbeatStore();
        store.SetPc("PC-001", "HOST-A", Utc(2026, 8, 1));
        var cutoff = Utc(2026, 9, 1);
        var notifiedAt = Utc(2026, 9, 1, 12);

        var first = await store.ClaimNewlyStaleHeartbeatsAsync(cutoff, notifiedAt, CancellationToken.None);
        var pc = Assert.Single(first);
        Assert.Equal("PC-001", pc.DeviceCode);
        Assert.Equal(notifiedAt, store.NotifiedAt("PC-001"));

        var again = await store.ClaimNewlyStaleHeartbeatsAsync(cutoff, Utc(2026, 9, 1, 13), CancellationToken.None);
        Assert.Empty(again);
        Assert.Equal(notifiedAt, store.NotifiedAt("PC-001"));
    }

    private static (StaleHeartbeatMonitor Monitor, NotificationPublisher Publisher) CreateMonitor(
        InMemoryStaleHeartbeatStore store,
        TimeSpan? threshold = null)
    {
        var options = new NotificationOptions
        {
            Webhook =
            {
                Enabled = true,
                Url = "https://example.local/webhook"
            },
            Events =
            {
                StaleHeartbeat = true
            },
            StaleHeartbeatThreshold = threshold ?? TimeSpan.FromHours(24)
        };
        var publisher = new NotificationPublisher(Options.Create(options), NullLogger<NotificationPublisher>.Instance);
        var monitor = new StaleHeartbeatMonitor(
            store,
            publisher,
            Options.Create(options),
            NullLogger<StaleHeartbeatMonitor>.Instance);
        return (monitor, publisher);
    }

    private static ConcurrentDictionary<string, byte> NewNotifiedSet() =>
        new(StringComparer.OrdinalIgnoreCase);

    private static StalePcHeartbeat Pc(string deviceCode, string hostName) =>
        new(deviceCode, hostName, Utc(2026, 8, 31, 12));

    private static DateTimeOffset Utc(int year, int month, int day, int hour = 0) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    internal sealed class InMemoryStaleHeartbeatStore : IStaleHeartbeatNotificationStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, StalePcHeartbeat> _pcs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTimeOffset> _notifiedAt = new(StringComparer.OrdinalIgnoreCase);

        public void SetPc(string deviceCode, string hostName, DateTimeOffset lastHeartbeatUtc)
        {
            lock (_gate)
            {
                _pcs[deviceCode] = new StalePcHeartbeat(deviceCode, hostName, lastHeartbeatUtc);
            }
        }

        public void RecordHeartbeat(string deviceCode, string hostName, DateTimeOffset reportedAtUtc)
        {
            lock (_gate)
            {
                _pcs[deviceCode] = new StalePcHeartbeat(deviceCode, hostName, reportedAtUtc);
                _notifiedAt.Remove(deviceCode);
            }
        }

        public bool WasNotified(string deviceCode)
        {
            lock (_gate)
            {
                return _notifiedAt.ContainsKey(deviceCode);
            }
        }

        public DateTimeOffset? NotifiedAt(string deviceCode)
        {
            lock (_gate)
            {
                return _notifiedAt.TryGetValue(deviceCode, out var notifiedAt) ? notifiedAt : null;
            }
        }

        public Task<List<StalePcHeartbeat>> ClaimNewlyStaleHeartbeatsAsync(
            DateTimeOffset cutoff,
            DateTimeOffset notifiedAtUtc,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                var stale = _pcs.Values
                    .Where(pc => pc.LastHeartbeatUtc < cutoff)
                    .OrderBy(pc => pc.LastHeartbeatUtc)
                    .ThenBy(pc => pc.DeviceCode, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var notified = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
                foreach (var deviceCode in _notifiedAt.Keys)
                {
                    notified.TryAdd(deviceCode, 0);
                }

                var newlyStale = StaleHeartbeatMonitor.TakeNewlyStale(stale, notified);
                foreach (var deviceCode in _notifiedAt.Keys.ToList())
                {
                    if (!notified.ContainsKey(deviceCode))
                    {
                        _notifiedAt.Remove(deviceCode);
                    }
                }

                foreach (var pc in newlyStale)
                {
                    _notifiedAt[pc.DeviceCode] = notifiedAtUtc;
                }

                return Task.FromResult(newlyStale);
            }
        }
    }
}
