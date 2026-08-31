using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

public sealed record HealthResponse(string Status, DateTimeOffset Utc);

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
