using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Api;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api.Tests;

public class NotificationPublisherTests
{
    [Fact]
    public void FindNewlyInstalled_returns_empty_for_the_first_snapshot()
    {
        var current = new[] { Software("Google Chrome", "120.0"), Software("Widget", "1.0") };

        Assert.Empty(NotificationPublisher.FindNewlyInstalled([], current));
    }

    [Fact]
    public void FindNewlyInstalled_returns_software_that_was_not_on_the_previous_snapshot()
    {
        var previous = new[] { Software("Google Chrome", "120.0") };
        var current = new[] { Software("Google Chrome", "120.0"), Software("Widget", "1.2") };

        var added = Assert.Single(NotificationPublisher.FindNewlyInstalled(previous, current));
        Assert.Equal("Widget", added.Name);
        Assert.Equal("1.2", added.Version);
    }

    [Fact]
    public void FindNewlyInstalled_treats_a_new_version_as_newly_installed()
    {
        var previous = new[] { Software("Widget", "1.0") };
        var current = new[] { Software("Widget", "1.1") };

        var added = Assert.Single(NotificationPublisher.FindNewlyInstalled(previous, current));
        Assert.Equal("1.1", added.Version);
    }

    [Fact]
    public void FindNewlyInstalled_ignores_case_whitespace_and_duplicates_in_the_current_snapshot()
    {
        var previous = new[] { Software("  widget  ", "1.0") };
        var current = new[]
        {
            Software("WIDGET", "1.0"),
            Software("Widget", "1.0"),
            Software("Other", "2.0"),
            Software("other", "2.0")
        };

        var added = Assert.Single(NotificationPublisher.FindNewlyInstalled(previous, current));
        Assert.Equal("Other", added.Name);
    }

    [Fact]
    public void EnqueueNewSoftwareIfNeeded_does_not_notify_on_the_first_applied_snapshot()
    {
        var snapshot = Snapshot(Software("Widget", "1.0"));
        var publisher = CreatePublisher(webhookEnabled: true, newSoftware: true);

        publisher.EnqueueNewSoftwareIfNeeded(snapshot, new SnapshotSaveResult(true, [], []));

        Assert.False(publisher.Reader.TryRead(out _));
    }

    [Fact]
    public void EnqueueNewSoftwareIfNeeded_enqueues_only_when_the_event_and_a_channel_are_enabled()
    {
        var snapshot = Snapshot(Software("Widget", "1.2"));
        var saveResult = new SnapshotSaveResult(true, [Software("Google Chrome", "120.0")], []);

        var enabled = CreatePublisher(webhookEnabled: true, newSoftware: true);
        enabled.EnqueueNewSoftwareIfNeeded(snapshot, saveResult);
        Assert.True(enabled.Reader.TryRead(out var message));
        Assert.Equal("신규 소프트웨어 설치 감지 — DESKTOP-FIN (PC-001)", message.Subject);
        Assert.Contains("PC DESKTOP-FIN (PC-001)", message.Body, StringComparison.Ordinal);
        Assert.Contains("- Widget 1.2", message.Body, StringComparison.Ordinal);

        var disabledEvent = CreatePublisher(webhookEnabled: true, newSoftware: false);
        disabledEvent.EnqueueNewSoftwareIfNeeded(snapshot, saveResult);
        Assert.False(disabledEvent.Reader.TryRead(out _));

        var noChannel = CreatePublisher(webhookEnabled: false, newSoftware: true);
        noChannel.EnqueueNewSoftwareIfNeeded(snapshot, saveResult);
        Assert.False(noChannel.Reader.TryRead(out _));
    }

    [Fact]
    public void EnqueueNewSoftwareIfNeeded_skips_unapplied_saves_and_snapshots_without_new_software()
    {
        var snapshot = Snapshot(Software("Widget", "1.2"));
        var publisher = CreatePublisher(webhookEnabled: true, newSoftware: true);

        publisher.EnqueueNewSoftwareIfNeeded(
            snapshot,
            new SnapshotSaveResult(false, [Software("Google Chrome", "120.0")], []));
        Assert.False(publisher.Reader.TryRead(out _));

        publisher.EnqueueNewSoftwareIfNeeded(
            snapshot,
            new SnapshotSaveResult(true, [Software("Widget", "1.2")], []));
        Assert.False(publisher.Reader.TryRead(out _));
    }

    [Fact]
    public void EnqueueNewSoftwareIfNeeded_summarizes_software_beyond_the_listing_limit()
    {
        var previous = new[] { Software("Anchor", "1.0") };
        var current = new List<InstalledSoftwareEntry> { Software("Anchor", "1.0") };
        current.AddRange(Enumerable.Range(1, 51).Select(i => Software($"App-{i:00}", "1.0")));
        var publisher = CreatePublisher(webhookEnabled: true, newSoftware: true);

        publisher.EnqueueNewSoftwareIfNeeded(Snapshot(current.ToArray()), new SnapshotSaveResult(true, previous, []));

        Assert.True(publisher.Reader.TryRead(out var message));
        Assert.Contains("- App-01 1.0", message.Body, StringComparison.Ordinal);
        Assert.Contains("- App-50 1.0", message.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("App-51", message.Body, StringComparison.Ordinal);
        Assert.Contains("- … 외 1개", message.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void EnqueueStaleHeartbeatsIfNeeded_uses_a_single_pc_subject_and_the_formatted_threshold()
    {
        var publisher = CreatePublisher(webhookEnabled: true, staleHeartbeat: true);
        var lastSeen = new DateTimeOffset(2026, 9, 1, 1, 0, 0, TimeSpan.Zero);

        publisher.EnqueueStaleHeartbeatsIfNeeded(
            [new StalePcHeartbeat("PC-001", "DESKTOP-FIN", lastSeen)],
            TimeSpan.FromHours(24));

        Assert.True(publisher.Reader.TryRead(out var message));
        Assert.Equal("PC 하트비트 두절 — DESKTOP-FIN (PC-001)", message.Subject);
        Assert.Contains("마지막 heartbeat가 24시간 이상 지난 PC가 있습니다.", message.Body, StringComparison.Ordinal);
        Assert.Contains("- DESKTOP-FIN (PC-001) — last: 2026-09-01 01:00:00Z", message.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void EnqueueStaleHeartbeatsIfNeeded_uses_a_count_subject_for_multiple_pcs()
    {
        var publisher = CreatePublisher(smtpEnabled: true, staleHeartbeat: true);
        var lastSeen = new DateTimeOffset(2026, 9, 1, 1, 0, 0, TimeSpan.Zero);

        publisher.EnqueueStaleHeartbeatsIfNeeded(
            [
                new StalePcHeartbeat("PC-001", "HOST-A", lastSeen),
                new StalePcHeartbeat("PC-002", "HOST-B", lastSeen)
            ],
            TimeSpan.FromMinutes(15));

        Assert.True(publisher.Reader.TryRead(out var message));
        Assert.Equal("PC 하트비트 두절 — 2대", message.Subject);
        Assert.Contains("마지막 heartbeat가 15분 이상 지난 PC가 있습니다.", message.Body, StringComparison.Ordinal);
        Assert.Contains("HOST-A (PC-001)", message.Body, StringComparison.Ordinal);
        Assert.Contains("HOST-B (PC-002)", message.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void EnqueueStaleHeartbeatsIfNeeded_skips_empty_lists_and_disabled_events()
    {
        var stale = new[] { new StalePcHeartbeat("PC-001", "HOST-A", DateTimeOffset.UtcNow) };

        var empty = CreatePublisher(webhookEnabled: true, staleHeartbeat: true);
        empty.EnqueueStaleHeartbeatsIfNeeded([], TimeSpan.FromHours(1));
        Assert.False(empty.Reader.TryRead(out _));

        var disabled = CreatePublisher(webhookEnabled: true, staleHeartbeat: false);
        disabled.EnqueueStaleHeartbeatsIfNeeded(stale, TimeSpan.FromHours(1));
        Assert.False(disabled.Reader.TryRead(out _));

        var noChannel = CreatePublisher(webhookEnabled: false, staleHeartbeat: true);
        noChannel.EnqueueStaleHeartbeatsIfNeeded(stale, TimeSpan.FromHours(1));
        Assert.False(noChannel.Reader.TryRead(out _));
    }

    private static NotificationPublisher CreatePublisher(
        bool webhookEnabled = false,
        bool smtpEnabled = false,
        bool newSoftware = false,
        bool staleHeartbeat = false)
    {
        var options = new NotificationOptions
        {
            Webhook =
            {
                Enabled = webhookEnabled,
                Url = "https://example.local/webhook"
            },
            Smtp =
            {
                Enabled = smtpEnabled
            },
            Events =
            {
                NewSoftware = newSoftware,
                StaleHeartbeat = staleHeartbeat
            }
        };
        return new NotificationPublisher(Options.Create(options), NullLogger<NotificationPublisher>.Instance);
    }

    private static InventoryIngestionRequest Snapshot(params InstalledSoftwareEntry[] software) =>
        new(
            new PcIdentity("PC-001", "DESKTOP-FIN", "CONTOSO", "Windows 11", "1.0.0"),
            software,
            DateTimeOffset.UtcNow);

    private static InstalledSoftwareEntry Software(string name, string? version) =>
        new(name, version, null, null, "HKLM", "Uninstall");
}
