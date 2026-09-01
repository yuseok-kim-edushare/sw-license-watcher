using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Api;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api.Tests;

public class SoftwareViolationNotificationTests
{
    [Fact]
    public void CollectCurrentViolations_keeps_blacklisted_matches_and_ignores_others()
    {
        var torrent = Software("uTorrent", "3.5.5");
        var chrome = Software("Google Chrome", "120.0");
        var managed = Software("Visual Studio", "17.8");
        var matches = new[]
        {
            new SoftwarePolicyMatch(torrent, Blacklist("*Torrent*")),
            new SoftwarePolicyMatch(chrome, null),
            new SoftwarePolicyMatch(managed, Policy("Visual Studio*", SoftwarePolicyClassification.Managed))
        };

        var current = SqlServerInventoryRepository.CollectCurrentViolations(matches);

        var violation = Assert.Single(current);
        Assert.Equal("uTorrent", violation.Key);
        Assert.Equal("uTorrent", violation.Value.Software.Name);
        Assert.Equal("*Torrent*", violation.Value.Policy.ProductName);
    }

    [Fact]
    public void CollectCurrentViolations_deduplicates_software_names_case_insensitively()
    {
        var matches = new[]
        {
            new SoftwarePolicyMatch(Software("uTorrent", "3.5"), Blacklist("*Torrent*", id: 1)),
            new SoftwarePolicyMatch(Software("UTORRENT", "3.6"), Blacklist("uTorrent", id: 2))
        };

        var current = SqlServerInventoryRepository.CollectCurrentViolations(matches);

        var violation = Assert.Single(current);
        Assert.Equal("3.5", violation.Value.Software.Version);
        Assert.Equal(1, violation.Value.Policy.Id);
    }

    [Fact]
    public void FindNewlyDetectedViolations_returns_all_current_when_none_existed()
    {
        var current = SqlServerInventoryRepository.CollectCurrentViolations(
        [
            new SoftwarePolicyMatch(Software("uTorrent", "3.5"), Blacklist("*Torrent*")),
            new SoftwarePolicyMatch(Software("BadApp", "1.0"), Blacklist("BadApp"))
        ]);

        var added = SqlServerInventoryRepository.FindNewlyDetectedViolations(current, []);

        Assert.Equal(["uTorrent", "BadApp"], added.Select(item => item.Software.Name));
    }

    [Fact]
    public void FindNewlyDetectedViolations_excludes_software_already_recorded_for_the_pc()
    {
        var current = SqlServerInventoryRepository.CollectCurrentViolations(
        [
            new SoftwarePolicyMatch(Software("uTorrent", "3.6"), Blacklist("*Torrent*")),
            new SoftwarePolicyMatch(Software("BadApp", "1.0"), Blacklist("BadApp"))
        ]);

        var added = SqlServerInventoryRepository.FindNewlyDetectedViolations(current, ["UTORRENT"]);

        var violation = Assert.Single(added);
        Assert.Equal("BadApp", violation.Software.Name);
        Assert.Equal("1.0", violation.Software.Version);
        Assert.Equal("BadApp", violation.Policy.ProductName);
    }

    [Fact]
    public void FindNewlyDetectedViolations_returns_empty_when_every_current_violation_already_exists()
    {
        var current = SqlServerInventoryRepository.CollectCurrentViolations(
        [
            new SoftwarePolicyMatch(Software("uTorrent", "3.5"), Blacklist("*Torrent*"))
        ]);

        Assert.Empty(SqlServerInventoryRepository.FindNewlyDetectedViolations(current, ["uTorrent"]));
    }

    [Fact]
    public void ForBlacklistViolations_includes_pc_software_version_and_policy_pattern()
    {
        var pc = new PcIdentity("PC-001", "DESKTOP-FIN", "CONTOSO", "Windows 11", "1.0.0");
        var violations = new[]
        {
            new NewBlacklistViolation(
                Software("uTorrent", "3.5.5"),
                Blacklist("*Torrent*", publisher: "BitTorrent*", versionPattern: ">=3.0"))
        };

        var message = NotificationPublisher.ForBlacklistViolations(pc, violations);

        Assert.Equal("블랙리스트 정책 위반 감지 — DESKTOP-FIN (PC-001)", message.Subject);
        Assert.Contains("PC DESKTOP-FIN (PC-001)", message.Body, StringComparison.Ordinal);
        Assert.Contains("- uTorrent 3.5.5 — 정책: *Torrent* / BitTorrent* / >=3.0", message.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatPolicyPattern_omits_blank_publisher_and_version()
    {
        Assert.Equal("*Torrent*", NotificationPublisher.FormatPolicyPattern(Blacklist("*Torrent*")));
        Assert.Equal(
            "BadApp / Acme",
            NotificationPublisher.FormatPolicyPattern(Blacklist("BadApp", publisher: "Acme")));
    }

    [Fact]
    public void EnqueueBlacklistViolationsIfNeeded_enqueues_only_when_the_event_and_a_channel_are_enabled()
    {
        var snapshot = new InventoryIngestionRequest(
            new PcIdentity("PC-001", "DESKTOP-FIN", "CONTOSO", "Windows 11", "1.0.0"),
            [Software("uTorrent", "3.5")],
            DateTimeOffset.UtcNow);
        var saveResult = new SnapshotSaveResult(true, [],
        [
            new NewBlacklistViolation(Software("uTorrent", "3.5"), Blacklist("*Torrent*"))
        ]);

        var enabled = CreatePublisher(webhookEnabled: true, blacklistViolation: true);
        enabled.EnqueueBlacklistViolationsIfNeeded(snapshot, saveResult);
        Assert.True(enabled.Reader.TryRead(out var message));
        Assert.Contains("uTorrent", message.Body, StringComparison.Ordinal);

        var disabledEvent = CreatePublisher(webhookEnabled: true, blacklistViolation: false);
        disabledEvent.EnqueueBlacklistViolationsIfNeeded(snapshot, saveResult);
        Assert.False(disabledEvent.Reader.TryRead(out _));

        var noChannel = CreatePublisher(webhookEnabled: false, blacklistViolation: true);
        noChannel.EnqueueBlacklistViolationsIfNeeded(snapshot, saveResult);
        Assert.False(noChannel.Reader.TryRead(out _));
    }

    [Fact]
    public void EnqueueBlacklistViolationsIfNeeded_skips_unapplied_saves_and_existing_only_results()
    {
        var snapshot = new InventoryIngestionRequest(
            new PcIdentity("PC-001", "DESKTOP-FIN", "CONTOSO", "Windows 11", "1.0.0"),
            [Software("uTorrent", "3.5")],
            DateTimeOffset.UtcNow);
        var publisher = CreatePublisher(webhookEnabled: true, blacklistViolation: true);

        publisher.EnqueueBlacklistViolationsIfNeeded(
            snapshot,
            new SnapshotSaveResult(false, [], [new NewBlacklistViolation(Software("uTorrent", "3.5"), Blacklist("*Torrent*"))]));
        Assert.False(publisher.Reader.TryRead(out _));

        publisher.EnqueueBlacklistViolationsIfNeeded(snapshot, new SnapshotSaveResult(true, [], []));
        Assert.False(publisher.Reader.TryRead(out _));
    }

    private static NotificationPublisher CreatePublisher(bool webhookEnabled, bool blacklistViolation)
    {
        var options = new NotificationOptions
        {
            Webhook =
            {
                Enabled = webhookEnabled,
                Url = "https://example.local/webhook"
            },
            Events =
            {
                BlacklistViolation = blacklistViolation
            }
        };
        return new NotificationPublisher(Options.Create(options), NullLogger<NotificationPublisher>.Instance);
    }

    private static InstalledSoftwareEntry Software(string name, string? version) =>
        new(name, version, null, null, "HKLM", "Uninstall");

    private static SoftwarePolicyEntry Blacklist(
        string productName,
        string? publisher = null,
        string? versionPattern = null,
        long id = 1) =>
        Policy(productName, SoftwarePolicyClassification.Blacklist, publisher, versionPattern, id);

    private static SoftwarePolicyEntry Policy(
        string productName,
        SoftwarePolicyClassification classification,
        string? publisher = null,
        string? versionPattern = null,
        long id = 1) =>
        new(id, productName, publisher, versionPattern, classification, null, true, DateTimeOffset.UtcNow);
}
