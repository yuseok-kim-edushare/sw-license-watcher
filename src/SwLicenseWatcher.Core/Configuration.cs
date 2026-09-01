using System.ComponentModel.DataAnnotations;

namespace SwLicenseWatcher.Core;

public sealed class WorkerAgentOptions
{
    [Required]
    public string DeviceCode { get; set; } = Environment.MachineName;

    [Required]
    public string ServerBaseUrl { get; set; } = "http://localhost:5080";

    public string SnapshotPath { get; set; } = "/api/inventory/snapshots";

    public string HeartbeatPath { get; set; } = "/api/agents/heartbeats";

    public string DomainName { get; set; } = "WORKGROUP";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(30);

    public TimeSpan MaxJitter { get; set; } = TimeSpan.FromMinutes(15);

    public bool RunOnceForDiagnostics { get; set; }

    public string ApiToken { get; set; } = string.Empty;

    public string HealthFilePath { get; set; } = @"C:\ProgramData\SwLicenseWatcher\state\worker-health.json";
}

public sealed class WatchdogOptions
{
    [Required]
    public string DeviceCode { get; set; } = Environment.MachineName;

    [Required]
    public string ServerBaseUrl { get; set; } = "http://localhost:5080";

    public string ManifestPath { get; set; } = "/api/updates/worker/manifest";

    public string WorkerServiceName { get; set; } = "SwLicenseWatcher.Agent.Worker";

    public string StagingDirectory { get; set; } = @"C:\ProgramData\SwLicenseWatcher\staging";

    public string BackupDirectory { get; set; } = @"C:\ProgramData\SwLicenseWatcher\backup";

    public string WorkerInstallDirectory { get; set; } = @"C:\Program Files\SwLicenseWatcher\Agent.Worker";

    public string WorkerHealthFilePath { get; set; } = @"C:\ProgramData\SwLicenseWatcher\state\worker-health.json";

    public string ApiToken { get; set; } = string.Empty;

    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(4);

    public TimeSpan MaxJitter { get; set; } = TimeSpan.FromMinutes(60);

    public long MaxPackageBytes { get; set; } = 512 * 1024 * 1024;

    public long MaxExtractedBytes { get; set; } = 1024 * 1024 * 1024;

    public bool RunOnceForDiagnostics { get; set; }
}

public sealed class LocalStateStoreOptions
{
    public string InstanceName { get; set; } = "SwLicenseWatcher";

    public string QueueDirectory { get; set; } = @"C:\ProgramData\SwLicenseWatcher\state\queue";

    public string DpapiScope { get; set; } = "LocalMachine";

    public int MaxQueuedSnapshots { get; set; } = 48;

    public long MaxQueueBytes { get; set; } = 64 * 1024 * 1024;
}

public sealed class ApiSecurityOptions
{
    [Required]
    public string Token { get; set; } = string.Empty;

    public bool RequireHttps { get; set; } = true;
}

public sealed class UpdateManifestOptions
{
    public string TargetServiceName { get; set; } = "SwLicenseWatcher.Agent.Worker";

    public string Version { get; set; } = "1.0.0";

    [Required]
    public string PackageUrl { get; set; } = "https://updates.example.local/sw-license-watcher/worker-1.0.0.zip";

    public string Sha256 { get; set; } = "REPLACE_WITH_RELEASE_SHA256";

    public bool RequireAuthenticode { get; set; } = true;

    public int RollbackAfterMinutes { get; set; } = 10;

    public UpdateManifest ToManifest() =>
        new(TargetServiceName, Version, PackageUrl, Sha256, RequireAuthenticode, RollbackAfterMinutes);
}

public sealed class SqlServerStorageOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    [Required]
    public string SchemaName { get; set; } = "inventory";

    public PcTableOptions PcTable { get; set; } = new();

    public InstalledSoftwareTableOptions InstalledSoftwareTable { get; set; } = new();

    public SoftwarePolicyTableOptions SoftwarePolicyTable { get; set; } = new();

    public SoftwareViolationTableOptions SoftwareViolationTable { get; set; } = new();
}

public sealed class PcTableOptions
{
    public string TableName { get; set; } = "pc_entity";

    public string PrimaryKeyColumn { get; set; } = "pc_id";

    public string DeviceCodeColumn { get; set; } = "device_code";

    public string HostNameColumn { get; set; } = "host_name";

    public string DomainNameColumn { get; set; } = "domain_name";

    public string OperatingSystemColumn { get; set; } = "operating_system";

    public string AgentVersionColumn { get; set; } = "agent_version";

    public string LastHeartbeatUtcColumn { get; set; } = "last_heartbeat_utc";

    public string LastInventoryUtcColumn { get; set; } = "last_inventory_utc";
}

public sealed class InstalledSoftwareTableOptions
{
    public string TableName { get; set; } = "pc_installed_sw";

    public string PrimaryKeyColumn { get; set; } = "installed_sw_id";

    public string PcForeignKeyColumn { get; set; } = "pc_id";

    public string DisplayNameColumn { get; set; } = "display_name";

    public string DisplayVersionColumn { get; set; } = "display_version";

    public string PublisherColumn { get; set; } = "publisher";

    public string InstallLocationColumn { get; set; } = "install_location";

    public string DiscoveryScopeColumn { get; set; } = "discovery_scope";

    public string DiscoverySourceColumn { get; set; } = "discovery_source";

    public string CollectedAtUtcColumn { get; set; } = "collected_at_utc";
}

public sealed class SoftwarePolicyTableOptions
{
    public string TableName { get; set; } = "software_policy_list";

    public string PrimaryKeyColumn { get; set; } = "policy_id";

    public string ClassificationColumn { get; set; } = "classification";

    public string ProductNameColumn { get; set; } = "product_name";

    public string PublisherColumn { get; set; } = "publisher";

    public string VersionPatternColumn { get; set; } = "version_pattern";

    public string NotesColumn { get; set; } = "notes";

    public string EnabledColumn { get; set; } = "enabled";

    public string UpdatedAtUtcColumn { get; set; } = "updated_at_utc";
}

public sealed class SoftwareViolationTableOptions
{
    public string TableName { get; set; } = "software_violation";

    public string PrimaryKeyColumn { get; set; } = "violation_id";

    public string PcForeignKeyColumn { get; set; } = "pc_id";

    public string PolicyForeignKeyColumn { get; set; } = "policy_id";

    public string DisplayNameColumn { get; set; } = "display_name";

    public string DisplayVersionColumn { get; set; } = "display_version";

    public string PublisherColumn { get; set; } = "publisher";

    public string DetectedAtUtcColumn { get; set; } = "detected_at_utc";

    public string LastSeenAtUtcColumn { get; set; } = "last_seen_at_utc";
}

public sealed class NotificationOptions
{
    public WebhookNotificationOptions Webhook { get; set; } = new();

    public SmtpNotificationOptions Smtp { get; set; } = new();

    public NotificationEventOptions Events { get; set; } = new();

    public TimeSpan StaleHeartbeatThreshold { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan StaleHeartbeatCheckInterval { get; set; } = TimeSpan.FromMinutes(15);

    public bool HasEnabledChannel => Webhook.Enabled || Smtp.Enabled;
}

public sealed class WebhookNotificationOptions
{
    public bool Enabled { get; set; }

    public string Url { get; set; } = string.Empty;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

public sealed class SmtpNotificationOptions
{
    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool EnableSsl { get; set; } = true;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string From { get; set; } = string.Empty;

    public string[] Recipients { get; set; } = [];
}

public sealed class NotificationEventOptions
{
    public bool NewSoftware { get; set; } = true;

    public bool StaleHeartbeat { get; set; } = true;
}
