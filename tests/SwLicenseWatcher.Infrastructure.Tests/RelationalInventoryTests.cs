using SwLicenseWatcher.Core;
using SwLicenseWatcher.Infrastructure.Relational;

namespace SwLicenseWatcher.Infrastructure.Tests;

public abstract class RelationalInventoryTests
{
    protected abstract RelationalDataContext Context { get; }

    protected abstract void EnsureAvailable();

    [Fact]
    public async Task Probe_succeeds_against_applied_schema()
    {
        EnsureAvailable();
        await Context.ProbeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Snapshot_round_trips_device_software_and_stale_rejection()
    {
        EnsureAvailable();
        var token = TestContext.Current.CancellationToken;
        var deviceCode = Unique("PC");
        var collected = new DateTimeOffset(2026, 9, 11, 4, 0, 0, TimeSpan.Zero);

        var first = await Context.SaveSnapshotAsync(
            Snapshot(deviceCode, collected, Software("Notepad++", "8.6")),
            token);
        Assert.True(first.Applied);
        Assert.Empty(first.PreviousSoftware);

        var devices = await Context.ListDevicesAsync(0, 10, deviceCode, staleAfterHours: null, token);
        Assert.Equal(1, devices.TotalCount);
        var summary = Assert.Single(devices.Items);
        Assert.Equal(deviceCode, summary.DeviceCode);
        Assert.Equal(collected, summary.LastInventoryUtc);

        var detail = await Context.GetDeviceAsync(deviceCode, classification: null, token);
        Assert.NotNull(detail);
        var installed = Assert.Single(detail.InstalledSoftware);
        Assert.Equal("Notepad++", installed.Name);
        Assert.Equal(SoftwarePolicyClassificationNames.Unclassified, installed.Classification);

        var aggregates = await Context.ListSoftwareAsync(0, 10, "Notepad", classification: null, token);
        Assert.Contains(aggregates.Items, item => item.Name == "Notepad++" && item.DeviceCount >= 1);

        var stale = await Context.SaveSnapshotAsync(
            Snapshot(deviceCode, collected.AddMinutes(-1), Software("Ignored", "1.0")),
            token);
        Assert.False(stale.Applied);

        var newer = await Context.SaveSnapshotAsync(
            Snapshot(deviceCode, collected.AddMinutes(1), Software("7-Zip", "24.08")),
            token);
        Assert.True(newer.Applied);
        Assert.Equal("Notepad++", Assert.Single(newer.PreviousSoftware).Name);

        var replaced = await Context.GetDeviceAsync(deviceCode, classification: null, token);
        Assert.Equal("7-Zip", Assert.Single(replaced!.InstalledSoftware).Name);
    }

    [Fact]
    public async Task Directed_uninstall_grant_is_returned_until_consumed_or_cancelled()
    {
        EnsureAvailable();
        var token = TestContext.Current.CancellationToken;
        var deviceCode = Unique("PC");
        await Context.SaveSnapshotAsync(
            Snapshot(deviceCode, new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.Zero), Software("7-Zip", "24")),
            token);

        var created = await Context.CreateDirectedUninstallRequestAsync(deviceCode, token);
        Assert.NotNull(created);
        Assert.Equal(UninstallGrant.Approved, created.Status);
        Assert.Equal(UninstallGrant.OriginAdmin, created.Origin);

        var command = await Context.GetDirectedUninstallCommandAsync(deviceCode, token);
        Assert.NotNull(command);
        Assert.Equal(created.Id, command.Id);
        Assert.False(string.IsNullOrWhiteSpace(command.Code));

        var listed = await Context.ListUninstallRequestsAsync(0, 10, deviceCode, token);
        var row = Assert.Single(listed.Items, item => item.Id == created.Id);
        Assert.Equal(UninstallGrant.OriginAdmin, row.Origin);
        Assert.Equal(UninstallGrant.Approved, row.Status);

        Assert.True(await Context.CancelDirectedUninstallRequestAsync(created.Id, token));
        Assert.Null(await Context.GetDirectedUninstallCommandAsync(deviceCode, token));
    }

    [Fact]
    public async Task Blacklist_policy_records_violation_and_managed_license_source()
    {
        EnsureAvailable();
        var token = TestContext.Current.CancellationToken;
        var deviceCode = Unique("PC");
        var torrent = Unique("uTorrent");
        var office = Unique("Microsoft 365");

        await Context.CreatePolicyAsync(
            new SoftwarePolicyWriteRequest(
                torrent + "*",
                Publisher: null,
                VersionPattern: null,
                SoftwarePolicyClassification.Blacklist,
                "P2P 금지"),
            token);
        await Context.CreatePolicyAsync(
            new SoftwarePolicyWriteRequest(
                office,
                Publisher: null,
                VersionPattern: null,
                SoftwarePolicyClassification.Managed,
                "회사 기본",
                DefaultLicenseSource: LicenseSourceNames.Company),
            token);

        var save = await Context.SaveSnapshotAsync(
            Snapshot(
                deviceCode,
                DateTimeOffset.UtcNow,
                Software(torrent + " 3.5", "3.5.5"),
                Software(office, "16.0")),
            token);
        Assert.True(save.Applied);
        Assert.Equal(torrent + " 3.5", Assert.Single(save.NewViolations).Software.Name);

        var violations = await Context.ListViolationsAsync(0, 10, deviceCode, since: null, token);
        Assert.Equal(1, violations.TotalCount);
        Assert.Equal(torrent + " 3.5", Assert.Single(violations.Items).SoftwareName);

        var detail = await Context.GetDeviceAsync(deviceCode, classification: null, token);
        Assert.NotNull(detail);
        Assert.Equal(
            SoftwarePolicyClassificationNames.Black,
            detail.InstalledSoftware.Single(entry => entry.Name.StartsWith(torrent, StringComparison.Ordinal)).Classification);
        var managed = detail.InstalledSoftware.Single(entry => entry.Name == office);
        Assert.Equal(SoftwarePolicyClassificationNames.Managed, managed.Classification);
        Assert.Equal(LicenseSourceNames.Company, managed.LicenseSource);

        Assert.True(await Context.SetDeviceSoftwareLicenseSourceAsync(
            deviceCode, office, LicenseSourceNames.Byo, token));
        var overridden = await Context.GetDeviceAsync(deviceCode, classification: null, token);
        Assert.Equal(
            LicenseSourceNames.Byo,
            overridden!.InstalledSoftware.Single(entry => entry.Name == office).LicenseSource);
        Assert.Equal(
            LicenseSourceNames.Byo,
            overridden.InstalledSoftware.Single(entry => entry.Name == office).LicenseSourceOverride);
    }

    [Fact]
    public async Task Heartbeat_updates_pc_and_clears_stale_notification_claim()
    {
        EnsureAvailable();
        var token = TestContext.Current.CancellationToken;
        var deviceCode = Unique("PC");
        var heartbeatAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await Context.SaveHeartbeatAsync(
            new AgentHeartbeat(deviceCode, "host-" + deviceCode, "Worker", "1.0.0", heartbeatAt, "Healthy"),
            token);

        var devices = await Context.ListDevicesAsync(0, 10, deviceCode, staleAfterHours: null, token);
        Assert.Equal(heartbeatAt, Assert.Single(devices.Items).LastHeartbeatUtc);

        var cutoff = heartbeatAt.AddHours(24);
        var claimed = await Context.ClaimNewlyStaleHeartbeatsAsync(
            cutoff,
            heartbeatAt.AddHours(25),
            token);
        Assert.Contains(claimed, pc => pc.DeviceCode == deviceCode);

        var claimedAgain = await Context.ClaimNewlyStaleHeartbeatsAsync(
            cutoff,
            heartbeatAt.AddHours(26),
            token);
        Assert.DoesNotContain(claimedAgain, pc => pc.DeviceCode == deviceCode);

        var recoveredAt = heartbeatAt.AddHours(30);
        await Context.SaveHeartbeatAsync(
            new AgentHeartbeat(deviceCode, "host-" + deviceCode, "Worker", "1.0.1", recoveredAt, "Healthy"),
            token);
        var afterRecovery = await Context.ClaimNewlyStaleHeartbeatsAsync(
            recoveredAt.AddHours(-1),
            recoveredAt,
            token);
        Assert.DoesNotContain(afterRecovery, pc => pc.DeviceCode == deviceCode);
    }

    [Fact]
    public async Task ListSoftwareWithAssets_groups_pages_and_associates_distinct_devices()
    {
        EnsureAvailable();
        var token = TestContext.Current.CancellationToken;
        var prefix = Unique("sw");
        var nameA = prefix + "-a";
        var nameB = prefix + "-b";
        var nullVersionPc = Unique("PC");
        var emptyVersionPc = Unique("PC");
        var duplicatePc = Unique("PC");
        var trailingSpacePc = Unique("PC");
        var assignedCode = Unique("ASSET");
        const string assignedHost = "asset-host";
        var collected = new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

        await Context.SaveSnapshotAsync(
            Snapshot(nullVersionPc, collected, Software(nameA, null)),
            token);
        await Context.SaveSnapshotAsync(
            Snapshot(emptyVersionPc, collected, Software(nameA, "")),
            token);
        await Context.SaveSnapshotAsync(
            Snapshot(duplicatePc, collected, Software(nameB, "1.0"), Software(nameB, "1.0")),
            token);
        await Context.SaveSnapshotAsync(
            Snapshot(trailingSpacePc, collected, Software(nameB, "1.0 ")),
            token);
        Assert.Equal(
            DeviceProfileUpdateResult.Updated,
            await Context.UpdateDeviceProfileAsync(
                duplicatePc,
                new DeviceProfileWriteRequest(assignedHost, AdminNotes: null, assignedCode),
                token));

        var all = await Context.ListSoftwareWithAssetsAsync(0, 10, prefix, classification: null, token);
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(3, all.Items.Count);

        var nullGroup = all.Items[0];
        Assert.Equal(nameA, nullGroup.Software.Name);
        Assert.Null(nullGroup.Software.Version);
        Assert.Equal(SoftwarePolicyClassificationNames.Unclassified, nullGroup.Software.Classification);
        Assert.Equal(1, nullGroup.Software.DeviceCount);
        Assert.Equal([new SoftwareAssetDevice(nullVersionPc, "host-" + nullVersionPc)], nullGroup.Devices);

        var emptyGroup = all.Items[1];
        Assert.Equal(nameA, emptyGroup.Software.Name);
        Assert.Equal(string.Empty, emptyGroup.Software.Version);
        Assert.Equal(1, emptyGroup.Software.DeviceCount);
        Assert.Equal([new SoftwareAssetDevice(emptyVersionPc, "host-" + emptyVersionPc)], emptyGroup.Devices);

        var grouped = all.Items[2];
        Assert.Equal(nameB, grouped.Software.Name);
        Assert.Equal("1.0", grouped.Software.Version?.TrimEnd());
        Assert.Equal(2, grouped.Software.DeviceCount);
        Assert.Equal(
            [
                new SoftwareAssetDevice(assignedCode, assignedHost),
                new SoftwareAssetDevice(trailingSpacePc, "host-" + trailingSpacePc)
            ],
            grouped.Devices);

        var page = await Context.ListSoftwareWithAssetsAsync(1, 1, prefix, classification: null, token);
        Assert.Equal(3, page.TotalCount);
        var only = Assert.Single(page.Items);
        Assert.Equal(nameA, only.Software.Name);
        Assert.Equal(string.Empty, only.Software.Version);
        Assert.Equal(1, only.Software.DeviceCount);
        Assert.Equal([new SoftwareAssetDevice(emptyVersionPc, "host-" + emptyVersionPc)], only.Devices);
    }

    [Fact]
    public async Task Worker_update_pin_upserts_and_seeds_once()
    {
        EnsureAvailable();
        var token = TestContext.Current.CancellationToken;
        var serviceName = Unique("svc");
        var pin = new UpdateManifest(serviceName, "1.2.3", "https://updates.example/worker.zip", new string('a', 64), true, 10);
        Assert.Null(await Context.GetWorkerUpdatePinAsync(serviceName, token));
        Assert.True(await Context.SeedWorkerUpdatePinIfEmptyAsync(pin, token));
        Assert.False(await Context.SeedWorkerUpdatePinIfEmptyAsync(pin, token));

        var updated = pin with { Version = "1.2.4", RequireAuthenticode = false };
        var stored = await Context.UpsertWorkerUpdatePinAsync(updated, token);
        Assert.Equal("1.2.4", stored.Version);
        Assert.False(stored.RequireAuthenticode);
        Assert.Equal("1.2.4", (await Context.GetWorkerUpdatePinAsync(serviceName, token))!.Version);
    }

    [Fact]
    public async Task User_message_enqueue_broadcast_and_consume()
    {
        EnsureAvailable();
        var token = TestContext.Current.CancellationToken;
        var deviceCode = Unique("PC");
        await Context.SaveSnapshotAsync(
            Snapshot(deviceCode, DateTimeOffset.UtcNow, Software("Notepad", "1")),
            token);

        var enqueued = await Context.EnqueueUserMessageAsync(deviceCode, "Hello", "Body", token);
        Assert.NotNull(enqueued);
        var pending = await Context.ListPendingUserMessagesAsync(deviceCode, 5, token);
        Assert.Contains(pending, message => message.Id == enqueued.Command.Id);
        Assert.True(await Context.ConsumeUserMessageAsync(enqueued.Command.Id, deviceCode, token));

        var broadcast = await Context.BroadcastUserMessagesAsync("All", "Notice", token);
        Assert.Contains(broadcast, item => item.DeviceAliases.Contains(deviceCode, StringComparer.OrdinalIgnoreCase));
    }

    protected static string Unique(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..12];

    protected static InventoryIngestionRequest Snapshot(
        string deviceCode,
        DateTimeOffset collectedAt,
        params InstalledSoftwareEntry[] software) =>
        new(
            new PcIdentity(deviceCode, "host-" + deviceCode, "WORKGROUP", "Windows 11 Pro", "1.0.0"),
            software,
            collectedAt);

    protected static InstalledSoftwareEntry Software(string name, string? version) =>
        new(name, version, "Publisher", @"C:\Program Files\" + name, "HKLM Uninstall", "UninstallRegistry");
}
