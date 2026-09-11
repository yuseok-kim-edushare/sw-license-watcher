using System.Text.Json.Serialization;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

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
    DateTimeOffset CollectedAtUtc,
    string? AssignedHostName = null,
    string? AssignedDeviceCode = null,
    string? DeviceId = null,
    string? DeviceCertificate = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    AgentUninstallCommand? UninstallCommand = null);

public sealed record AgentHeartbeatAcceptedResponse(
    string DeviceCode,
    string HostName,
    string ServiceName,
    string Version,
    DateTimeOffset ReportedAtUtc,
    string Status,
    string? AssignedHostName,
    string? AssignedDeviceCode = null,
    string? DeviceId = null,
    string? DeviceCertificate = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    AgentUninstallCommand? UninstallCommand = null);

public sealed record SchemaStatusResponse(
    bool ApplyOnStartup,
    bool ApplyInBackground,
    bool? LastSucceeded,
    int LastBatchCount,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSucceededUtc,
    string? LastError);

public sealed record SchemaApplyResponse(
    bool Applied,
    int BatchCount,
    DateTimeOffset Utc,
    string? Error = null);
