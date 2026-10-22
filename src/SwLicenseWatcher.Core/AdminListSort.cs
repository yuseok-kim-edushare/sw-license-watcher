namespace SwLicenseWatcher.Core;

public static class AdminListSort
{
    public const string DeviceCode = "deviceCode";
    public const string HostName = "hostName";
    public const string DomainName = "domainName";
    public const string OperatingSystem = "operatingSystem";
    public const string AgentVersion = "agentVersion";
    public const string LastHeartbeatUtc = "lastHeartbeatUtc";
    public const string LastInventoryUtc = "lastInventoryUtc";
    public const string InstalledAtUtc = "installedAtUtc";

    public const string Name = "name";
    public const string Version = "version";
    public const string Classification = "classification";
    public const string DeviceCount = "deviceCount";

    public const string SoftwareName = "softwareName";
    public const string SoftwareVersion = "softwareVersion";
    public const string PolicyProductName = "policyProductName";
    public const string DetectedAtUtc = "detectedAtUtc";
    public const string LastSeenAtUtc = "lastSeenAtUtc";

    public const string Id = "id";
    public const string ProductName = "productName";
    public const string Publisher = "publisher";
    public const string VersionPattern = "versionPattern";
    public const string DefaultLicenseSource = "defaultLicenseSource";
    public const string Enabled = "enabled";
    public const string UpdatedAtUtc = "updatedAtUtc";

    public const string Origin = "origin";
    public const string Status = "status";
    public const string RequestedAtUtc = "requestedAtUtc";
    public const string ApprovedAtUtc = "approvedAtUtc";
    public const string ExpiresAtUtc = "expiresAtUtc";

    public static IReadOnlySet<string> Devices { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        DeviceCode, HostName, DomainName, OperatingSystem, AgentVersion, LastHeartbeatUtc, LastInventoryUtc, InstalledAtUtc
    };

    public static IReadOnlySet<string> Software { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Name, Version, Classification, DeviceCount
    };

    public static IReadOnlySet<string> Violations { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        DeviceCode, HostName, SoftwareName, SoftwareVersion, PolicyProductName, Classification, DetectedAtUtc, LastSeenAtUtc
    };

    public static IReadOnlySet<string> Policies { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Id, ProductName, Publisher, VersionPattern, Classification, DefaultLicenseSource, Enabled, UpdatedAtUtc
    };

    public static IReadOnlySet<string> UninstallRequests { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Id, DeviceCode, HostName, Origin, Status, RequestedAtUtc, ApprovedAtUtc, ExpiresAtUtc
    };
}
