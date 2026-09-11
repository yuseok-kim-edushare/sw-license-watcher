using SwLicenseWatcher.Core;
using SwLicenseWatcher.Infrastructure.SqlServer;

namespace SwLicenseWatcher.Infrastructure.Tests;

[Collection(SqlLocalDbCollection.Name)]
[Trait("Category", "SqlIntegration")]
public sealed class SqlLocalDbInventoryTests(SqlLocalDbFixture fixture)
{
    [Fact]
    public async Task Probe_succeeds_against_applied_schema()
    {
        fixture.EnsureAvailable();
        await fixture.Context.ProbeAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Snapshot_round_trips_device_software_and_stale_rejection()
    {
        fixture.EnsureAvailable();
        var token = TestContext.Current.CancellationToken;
        var deviceCode = Unique("PC");
        var collected = new DateTimeOffset(2026, 9, 11, 4, 0, 0, TimeSpan.Zero);

        var first = await fixture.Context.SaveSnapshotAsync(
            Snapshot(deviceCode, collected, Software("Notepad++", "8.6")),
            token);
        Assert.True(first.Applied);
        Assert.Empty(first.PreviousSoftware);

        var devices = await fixture.Context.ListDevicesAsync(0, 10, deviceCode, staleAfterHours: null, token);
        Assert.Equal(1, devices.TotalCount);
        var summary = Assert.Single(devices.Items);
        Assert.Equal(deviceCode, summary.DeviceCode);
        Assert.Equal(collected, summary.LastInventoryUtc);

        var detail = await fixture.Context.GetDeviceAsync(deviceCode, classification: null, token);
        Assert.NotNull(detail);
        var installed = Assert.Single(detail.InstalledSoftware);
        Assert.Equal("Notepad++", installed.Name);
        Assert.Equal(SoftwarePolicyClassificationNames.Unclassified, installed.Classification);

        var aggregates = await fixture.Context.ListSoftwareAsync(0, 10, "Notepad", classification: null, token);
        Assert.Contains(aggregates.Items, item => item.Name == "Notepad++" && item.DeviceCount >= 1);

        var stale = await fixture.Context.SaveSnapshotAsync(
            Snapshot(deviceCode, collected.AddMinutes(-1), Software("Ignored", "1.0")),
            token);
        Assert.False(stale.Applied);

        var newer = await fixture.Context.SaveSnapshotAsync(
            Snapshot(deviceCode, collected.AddMinutes(1), Software("7-Zip", "24.08")),
            token);
        Assert.True(newer.Applied);
        Assert.Equal("Notepad++", Assert.Single(newer.PreviousSoftware).Name);

        var replaced = await fixture.Context.GetDeviceAsync(deviceCode, classification: null, token);
        Assert.Equal("7-Zip", Assert.Single(replaced!.InstalledSoftware).Name);
    }

    [Fact]
    public async Task Blacklist_policy_records_violation_and_managed_license_source()
    {
        fixture.EnsureAvailable();
        var token = TestContext.Current.CancellationToken;
        var deviceCode = Unique("PC");
        var torrent = Unique("uTorrent");
        var office = Unique("Microsoft 365");

        await fixture.Context.CreatePolicyAsync(
            new SoftwarePolicyWriteRequest(
                torrent + "*",
                Publisher: null,
                VersionPattern: null,
                SoftwarePolicyClassification.Blacklist,
                "P2P 금지"),
            token);
        await fixture.Context.CreatePolicyAsync(
            new SoftwarePolicyWriteRequest(
                office,
                Publisher: null,
                VersionPattern: null,
                SoftwarePolicyClassification.Managed,
                "회사 기본",
                DefaultLicenseSource: LicenseSourceNames.Company),
            token);

        var save = await fixture.Context.SaveSnapshotAsync(
            Snapshot(
                deviceCode,
                DateTimeOffset.UtcNow,
                Software(torrent + " 3.5", "3.5.5"),
                Software(office, "16.0")),
            token);
        Assert.True(save.Applied);
        Assert.Equal(torrent + " 3.5", Assert.Single(save.NewViolations).Software.Name);

        var violations = await fixture.Context.ListViolationsAsync(0, 10, deviceCode, since: null, token);
        Assert.Equal(1, violations.TotalCount);
        Assert.Equal(torrent + " 3.5", Assert.Single(violations.Items).SoftwareName);

        var detail = await fixture.Context.GetDeviceAsync(deviceCode, classification: null, token);
        Assert.NotNull(detail);
        Assert.Equal(
            SoftwarePolicyClassificationNames.Black,
            detail.InstalledSoftware.Single(entry => entry.Name.StartsWith(torrent, StringComparison.Ordinal)).Classification);
        var managed = detail.InstalledSoftware.Single(entry => entry.Name == office);
        Assert.Equal(SoftwarePolicyClassificationNames.Managed, managed.Classification);
        Assert.Equal(LicenseSourceNames.Company, managed.LicenseSource);

        Assert.True(await fixture.Context.SetDeviceSoftwareLicenseSourceAsync(
            deviceCode, office, LicenseSourceNames.Byo, token));
        var overridden = await fixture.Context.GetDeviceAsync(deviceCode, classification: null, token);
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
        fixture.EnsureAvailable();
        var token = TestContext.Current.CancellationToken;
        var deviceCode = Unique("PC");
        var heartbeatAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await fixture.Context.SaveHeartbeatAsync(
            new AgentHeartbeat(deviceCode, "host-" + deviceCode, "Worker", "1.0.0", heartbeatAt, "Healthy"),
            token);

        var devices = await fixture.Context.ListDevicesAsync(0, 10, deviceCode, staleAfterHours: null, token);
        Assert.Equal(heartbeatAt, Assert.Single(devices.Items).LastHeartbeatUtc);

        var cutoff = heartbeatAt.AddHours(24);
        var claimed = await fixture.Context.ClaimNewlyStaleHeartbeatsAsync(
            cutoff,
            heartbeatAt.AddHours(25),
            token);
        Assert.Contains(claimed, pc => pc.DeviceCode == deviceCode);

        var claimedAgain = await fixture.Context.ClaimNewlyStaleHeartbeatsAsync(
            cutoff,
            heartbeatAt.AddHours(26),
            token);
        Assert.DoesNotContain(claimedAgain, pc => pc.DeviceCode == deviceCode);

        var recoveredAt = heartbeatAt.AddHours(30);
        await fixture.Context.SaveHeartbeatAsync(
            new AgentHeartbeat(deviceCode, "host-" + deviceCode, "Worker", "1.0.1", recoveredAt, "Healthy"),
            token);
        var afterRecovery = await fixture.Context.ClaimNewlyStaleHeartbeatsAsync(
            recoveredAt.AddHours(-1),
            recoveredAt,
            token);
        Assert.DoesNotContain(afterRecovery, pc => pc.DeviceCode == deviceCode);
    }

    [Fact]
    public async Task Company_schema_tables_exist_after_apply()
    {
        fixture.EnsureAvailable();
        var token = TestContext.Current.CancellationToken;
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(fixture.Storage.ConnectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sys.tables
            WHERE SCHEMA_NAME(schema_id) = N'inventory'
              AND name IN (
                N'company_pc',
                N'company_pc_installed_sw',
                N'company_sw_policy',
                N'company_sw_violation',
                N'company_stale_heartbeat_notification',
                N'company_pc_uninstall_request',
                N'company_pc_sw_license',
                N'company_worker_update_pin');
            """;
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(token));
        Assert.Equal(8, count);
    }

    private static string Unique(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..12];

    private static InventoryIngestionRequest Snapshot(
        string deviceCode,
        DateTimeOffset collectedAt,
        params InstalledSoftwareEntry[] software) =>
        new(
            new PcIdentity(deviceCode, "host-" + deviceCode, "WORKGROUP", "Windows 11 Pro", "1.0.0"),
            software,
            collectedAt);

    private static InstalledSoftwareEntry Software(string name, string version) =>
        new(name, version, "Publisher", @"C:\Program Files\" + name, "HKLM Uninstall", "UninstallRegistry");
}
