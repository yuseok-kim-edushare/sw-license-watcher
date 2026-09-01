using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Core.Tests;

public class InventorySnapshotValidatorTests
{
    [Fact]
    public void TryValidate_snapshot_accepts_a_complete_payload()
    {
        var snapshot = CreateSnapshot();

        var valid = InventorySnapshotValidator.TryValidate(snapshot, out var error);

        Assert.True(valid);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryValidate_snapshot_accepts_empty_domain_and_operating_system()
    {
        var snapshot = CreateSnapshot() with
        {
            Pc = CreateSnapshot().Pc with { DomainName = string.Empty, OperatingSystem = string.Empty }
        };

        Assert.True(InventorySnapshotValidator.TryValidate(snapshot, out var error));
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryValidate_snapshot_rejects_null_payload()
    {
        var valid = InventorySnapshotValidator.TryValidate((InventoryIngestionRequest?)null, out var error);

        Assert.False(valid);
        Assert.Equal("The snapshot payload is required.", error);
    }

    [Theory]
    [MemberData(nameof(InvalidSnapshotCases))]
    public void TryValidate_snapshot_rejects_missing_identity_fields(InventoryIngestionRequest snapshot)
    {
        var valid = InventorySnapshotValidator.TryValidate(snapshot, out var error);

        Assert.False(valid);
        Assert.Equal("The snapshot is missing required identity fields.", error);
    }

    [Fact]
    public void TryValidate_snapshot_rejects_null_software_collection()
    {
        var snapshot = new InventoryIngestionRequest(
            CreateSnapshot().Pc,
            null!,
            DateTimeOffset.UtcNow);

        var valid = InventorySnapshotValidator.TryValidate(snapshot, out var error);

        Assert.False(valid);
        Assert.Equal("The snapshot is missing the installed software collection.", error);
    }

    [Theory]
    [MemberData(nameof(InvalidSoftwareCases))]
    public void TryValidate_snapshot_rejects_invalid_software_entries(InstalledSoftwareEntry? entry)
    {
        var snapshot = CreateSnapshot() with { InstalledSoftware = [entry!] };

        var valid = InventorySnapshotValidator.TryValidate(snapshot, out var error);

        Assert.False(valid);
        Assert.Equal("The snapshot contains an invalid installed software entry.", error);
    }

    [Fact]
    public void TryValidate_heartbeat_accepts_a_complete_payload()
    {
        var heartbeat = CreateHeartbeat();

        var valid = InventorySnapshotValidator.TryValidate(heartbeat, out var error);

        Assert.True(valid);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryValidate_heartbeat_rejects_null_payload()
    {
        var valid = InventorySnapshotValidator.TryValidate((AgentHeartbeat?)null, out var error);

        Assert.False(valid);
        Assert.Equal("The heartbeat is missing required fields or exceeds persisted field limits.", error);
    }

    [Theory]
    [MemberData(nameof(InvalidHeartbeatCases))]
    public void TryValidate_heartbeat_rejects_missing_or_oversized_fields(AgentHeartbeat heartbeat)
    {
        var valid = InventorySnapshotValidator.TryValidate(heartbeat, out var error);

        Assert.False(valid);
        Assert.Equal("The heartbeat is missing required fields or exceeds persisted field limits.", error);
    }

    public static TheoryData<InventoryIngestionRequest> InvalidSnapshotCases()
    {
        var valid = CreateSnapshot();
        return new TheoryData<InventoryIngestionRequest>
        {
            valid with { Pc = null! },
            valid with { Pc = valid.Pc with { DeviceCode = " " } },
            valid with { Pc = valid.Pc with { HostName = "" } },
            valid with { Pc = valid.Pc with { DomainName = null! } },
            valid with { Pc = valid.Pc with { OperatingSystem = null! } },
            valid with { Pc = valid.Pc with { AgentVersion = " " } },
            valid with { CollectedAtUtc = default },
            valid with { Pc = valid.Pc with { DeviceCode = new string('d', 129) } },
            valid with { Pc = valid.Pc with { HostName = new string('h', 129) } },
            valid with { Pc = valid.Pc with { DomainName = new string('n', 129) } },
            valid with { Pc = valid.Pc with { OperatingSystem = new string('o', 129) } },
            valid with { Pc = valid.Pc with { AgentVersion = new string('v', 33) } }
        };
    }

    public static TheoryData<InstalledSoftwareEntry?> InvalidSoftwareCases() =>
        new()
        {
            null,
            new InstalledSoftwareEntry(" ", "1.0", "Pub", @"C:\App", "Machine", "Registry.Uninstall"),
            new InstalledSoftwareEntry("App", "1.0", "Pub", @"C:\App", "", "Registry.Uninstall"),
            new InstalledSoftwareEntry("App", "1.0", "Pub", @"C:\App", "Machine", " ")
        };

    public static TheoryData<AgentHeartbeat> InvalidHeartbeatCases()
    {
        var valid = CreateHeartbeat();
        return new TheoryData<AgentHeartbeat>
        {
            valid with { DeviceCode = "" },
            valid with { HostName = " " },
            valid with { ServiceName = "" },
            valid with { Version = " " },
            valid with { Status = "" },
            valid with { ReportedAtUtc = default },
            valid with { DeviceCode = new string('d', 129) },
            valid with { HostName = new string('h', 129) },
            valid with { Version = new string('v', 33) }
        };
    }

    internal static InventoryIngestionRequest CreateSnapshot() =>
        new(
            new PcIdentity("PC-001", "host-a", "CORP", "Windows 11", "1.0.0"),
            [new InstalledSoftwareEntry("Widget", "1.2.3", "Acme", @"C:\Program Files\Widget", "Machine", "Registry.Uninstall")],
            new DateTimeOffset(2026, 1, 15, 8, 30, 0, TimeSpan.Zero));

    private static AgentHeartbeat CreateHeartbeat() =>
        new("PC-001", "host-a", "Worker", "1.0.0", new DateTimeOffset(2026, 1, 15, 8, 30, 0, TimeSpan.Zero), "Healthy");
}
