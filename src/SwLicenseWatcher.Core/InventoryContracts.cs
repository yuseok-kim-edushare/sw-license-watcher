namespace SwLicenseWatcher.Core;

public sealed record PcIdentity(
    string DeviceCode,
    string HostName,
    string DomainName,
    string OperatingSystem,
    string AgentVersion);

public sealed record InstalledSoftwareEntry(
    string Name,
    string? Version,
    string? Publisher,
    string? InstallLocation,
    string DiscoveryScope,
    string DiscoverySource);

public sealed record InventoryIngestionRequest(
    PcIdentity Pc,
    IReadOnlyCollection<InstalledSoftwareEntry> InstalledSoftware,
    DateTimeOffset CollectedAtUtc);

public sealed record AgentHeartbeat(
    string DeviceCode,
    string HostName,
    string ServiceName,
    string Version,
    DateTimeOffset ReportedAtUtc,
    string Status);

public enum SoftwarePolicyClassification
{
    Whitelist,
    Managed,
    Blacklist
}

public sealed record SoftwarePolicyEntry(
    string ProductName,
    string? Publisher,
    string? VersionPattern,
    SoftwarePolicyClassification Classification,
    string? Notes,
    bool Enabled);

public sealed record WorkerHealthReport(
    string ServiceName,
    string Version,
    DateTimeOffset ReportedAtUtc);

public sealed record UpdateManifest(
    string TargetServiceName,
    string Version,
    string PackageUrl,
    string Sha256,
    bool RequireAuthenticode,
    int RollbackAfterMinutes);
