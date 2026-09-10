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
    DateTimeOffset CollectedAtUtc);

public sealed record UninstallRequestCreateRequest(string DeviceCode);

public sealed record UninstallRequestCreatedResponse(
    long Id,
    string DeviceCode,
    string Status,
    DateTimeOffset RequestedAtUtc);

public sealed record AgentUninstallRequestResponse(
    long Id,
    string DeviceCode,
    string Status,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string? Code);

public sealed record UninstallRequestConsumeRequest(string DeviceCode, string Code);
