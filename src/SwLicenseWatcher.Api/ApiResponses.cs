using System.Text.Json.Serialization;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

public sealed record HealthResponse(
    string Status,
    DateTimeOffset Utc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Reason = null);

public sealed record ErrorResponse(string Error);

public sealed record DesignArchitecture(
    string Agent,
    string LocalState,
    string InventoryCollection,
    string UpdateSafety);

public sealed record DesignSqlServer(
    bool HasConnectionStringConfigured,
    string SchemaName,
    string SchemaScript);

public sealed record DesignCounts(int SnapshotCount, int HeartbeatCount);

public sealed record DesignResponse(
    DesignArchitecture Architecture,
    DesignSqlServer SqlServer,
    DesignCounts LatestCounts,
    UpdateManifest WorkerManifest);

public sealed record SnapshotAcceptedResponse(
    string DeviceCode,
    int InstalledSoftwareCount,
    DateTimeOffset CollectedAtUtc);

public sealed record DeviceSummary(
    string DeviceCode,
    string HostName,
    string DomainName,
    string OperatingSystem,
    string AgentVersion,
    DateTimeOffset? LastHeartbeatUtc,
    DateTimeOffset? LastInventoryUtc);

public sealed record DeviceListResponse(
    int Skip,
    int Take,
    int TotalCount,
    IReadOnlyList<DeviceSummary> Items);

public sealed record DeviceDetail(
    string DeviceCode,
    string HostName,
    string DomainName,
    string OperatingSystem,
    string AgentVersion,
    DateTimeOffset? LastHeartbeatUtc,
    DateTimeOffset? LastInventoryUtc,
    IReadOnlyList<InstalledSoftwareEntry> InstalledSoftware);

public sealed record SoftwareAggregate(
    string Name,
    string? Version,
    string Classification,
    int DeviceCount);

public sealed record SoftwareAggregateListResponse(
    int Skip,
    int Take,
    int TotalCount,
    IReadOnlyList<SoftwareAggregate> Items);

public sealed record SoftwareDevice(
    string DeviceCode,
    string HostName,
    string DomainName,
    string OperatingSystem,
    string AgentVersion,
    DateTimeOffset? LastHeartbeatUtc,
    DateTimeOffset? LastInventoryUtc,
    string? Version,
    string? Publisher,
    string Classification);

public sealed record SoftwareDeviceListResponse(
    string Name,
    int Skip,
    int Take,
    int TotalCount,
    IReadOnlyList<SoftwareDevice> Items);

public sealed record PolicyListResponse(
    int Skip,
    int Take,
    int TotalCount,
    IReadOnlyList<SoftwarePolicyEntry> Items);

public sealed record ViolationListResponse(
    int Skip,
    int Take,
    int TotalCount,
    IReadOnlyList<SoftwareViolationEntry> Items);
