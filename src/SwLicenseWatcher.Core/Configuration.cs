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

    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(4);

    public TimeSpan MaxJitter { get; set; } = TimeSpan.FromMinutes(60);

    public TimeSpan WorkerHealthyTimeout { get; set; } = TimeSpan.FromMinutes(10);

    public bool RunOnceForDiagnostics { get; set; }
}

public sealed class LocalStateStoreOptions
{
    public string InstanceName { get; set; } = "SwLicenseWatcher";

    public string EsentDatabaseFilePath { get; set; } = @"C:\ProgramData\SwLicenseWatcher\state\agent.edb";

    public string MetadataTableName { get; set; } = "AgentMetadata";

    public string SnapshotTableName { get; set; } = "InventoryCheckpoint";

    public string DpapiScope { get; set; } = "LocalMachine";
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
