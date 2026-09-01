using System.Text.Json.Serialization;

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
    string DiscoverySource,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Classification = null);

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

[JsonConverter(typeof(JsonStringEnumConverter<SoftwarePolicyClassification>))]
public enum SoftwarePolicyClassification
{
    [JsonStringEnumMemberName("white")]
    Whitelist,
    [JsonStringEnumMemberName("managed")]
    Managed,
    [JsonStringEnumMemberName("black")]
    Blacklist
}

public sealed record SoftwarePolicyEntry(
    long Id,
    string ProductName,
    string? Publisher,
    string? VersionPattern,
    SoftwarePolicyClassification Classification,
    string? Notes,
    bool Enabled,
    DateTimeOffset UpdatedAtUtc);

public sealed record SoftwarePolicyWriteRequest(
    string ProductName,
    string? Publisher,
    string? VersionPattern,
    SoftwarePolicyClassification? Classification,
    string? Notes,
    bool Enabled = true);

public sealed record SoftwarePolicyMatch(
    InstalledSoftwareEntry Software,
    SoftwarePolicyEntry? Policy)
{
    public bool IsUnclassified => Policy is null;

    public bool IsBlacklisted => Policy?.Classification == SoftwarePolicyClassification.Blacklist;

    public SoftwarePolicyClassification? Classification => Policy?.Classification;

    public string StoredClassification => SoftwarePolicyClassificationNames.ToInstalledSoftwareStorage(Classification);
}

public sealed record SoftwareViolationEntry(
    long Id,
    string DeviceCode,
    string HostName,
    string SoftwareName,
    string? SoftwareVersion,
    string? Publisher,
    long PolicyId,
    string PolicyProductName,
    SoftwarePolicyClassification Classification,
    DateTimeOffset DetectedAtUtc,
    DateTimeOffset LastSeenAtUtc);

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
