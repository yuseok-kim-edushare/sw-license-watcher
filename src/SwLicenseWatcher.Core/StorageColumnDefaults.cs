namespace SwLicenseWatcher.Core;

public static class StorageColumnDefaults
{
    public static void ApplyUnspecifiedColumnDefaults(this SqlServerStorageOptions options)
    {
        options.PcTable ??= new PcTableOptions();
        options.InstalledSoftwareTable ??= new InstalledSoftwareTableOptions();
        options.SoftwarePolicyTable ??= new SoftwarePolicyTableOptions();
        options.SoftwareViolationTable ??= new SoftwareViolationTableOptions();
        options.StaleHeartbeatNotificationTable ??= new StaleHeartbeatNotificationTableOptions();
        options.UninstallRequestTable ??= new UninstallRequestTableOptions();
        options.SoftwareLicenseTable ??= new SoftwareLicenseTableOptions();
        options.WorkerUpdatePinTable ??= new WorkerUpdatePinTableOptions();
        options.UserMessageTable ??= new UserMessageTableOptions();

        Apply(options.PcTable, new PcTableOptions());
        Apply(options.InstalledSoftwareTable, new InstalledSoftwareTableOptions());
        Apply(options.SoftwarePolicyTable, new SoftwarePolicyTableOptions());
        Apply(options.SoftwareViolationTable, new SoftwareViolationTableOptions());
        Apply(options.StaleHeartbeatNotificationTable, new StaleHeartbeatNotificationTableOptions());
        Apply(options.UninstallRequestTable, new UninstallRequestTableOptions());
        Apply(options.SoftwareLicenseTable, new SoftwareLicenseTableOptions());
        Apply(options.WorkerUpdatePinTable, new WorkerUpdatePinTableOptions());
        Apply(options.UserMessageTable, new UserMessageTableOptions());
    }

    private static void Apply(PcTableOptions target, PcTableOptions defaults)
    {
        target.TableName = Pick(target.TableName, defaults.TableName);
        target.PrimaryKeyColumn = Pick(target.PrimaryKeyColumn, defaults.PrimaryKeyColumn);
        target.DeviceCodeColumn = Pick(target.DeviceCodeColumn, defaults.DeviceCodeColumn);
        target.HostNameColumn = Pick(target.HostNameColumn, defaults.HostNameColumn);
        target.DomainNameColumn = Pick(target.DomainNameColumn, defaults.DomainNameColumn);
        target.OperatingSystemColumn = Pick(target.OperatingSystemColumn, defaults.OperatingSystemColumn);
        target.AgentVersionColumn = Pick(target.AgentVersionColumn, defaults.AgentVersionColumn);
        target.LastHeartbeatUtcColumn = Pick(target.LastHeartbeatUtcColumn, defaults.LastHeartbeatUtcColumn);
        target.LastInventoryUtcColumn = Pick(target.LastInventoryUtcColumn, defaults.LastInventoryUtcColumn);
        target.AssignedHostNameColumn = Pick(target.AssignedHostNameColumn, defaults.AssignedHostNameColumn);
        target.AdminNotesColumn = Pick(target.AdminNotesColumn, defaults.AdminNotesColumn);
        target.AssignedDeviceCodeColumn = Pick(target.AssignedDeviceCodeColumn, defaults.AssignedDeviceCodeColumn);
        target.DeviceIdColumn = Pick(target.DeviceIdColumn, defaults.DeviceIdColumn);
        target.DevicePublicKeyColumn = Pick(target.DevicePublicKeyColumn, defaults.DevicePublicKeyColumn);
        target.DeviceCertificateColumn = Pick(target.DeviceCertificateColumn, defaults.DeviceCertificateColumn);
        target.DeviceKeySha256Column = Pick(target.DeviceKeySha256Column, defaults.DeviceKeySha256Column);
        target.InstalledAtUtcColumn = Pick(target.InstalledAtUtcColumn, defaults.InstalledAtUtcColumn);
        target.SupersededAtUtcColumn = Pick(target.SupersededAtUtcColumn, defaults.SupersededAtUtcColumn);
    }

    private static void Apply(InstalledSoftwareTableOptions target, InstalledSoftwareTableOptions defaults)
    {
        target.TableName = Pick(target.TableName, defaults.TableName);
        target.PrimaryKeyColumn = Pick(target.PrimaryKeyColumn, defaults.PrimaryKeyColumn);
        target.PcForeignKeyColumn = Pick(target.PcForeignKeyColumn, defaults.PcForeignKeyColumn);
        target.DisplayNameColumn = Pick(target.DisplayNameColumn, defaults.DisplayNameColumn);
        target.DisplayVersionColumn = Pick(target.DisplayVersionColumn, defaults.DisplayVersionColumn);
        target.PublisherColumn = Pick(target.PublisherColumn, defaults.PublisherColumn);
        target.InstallLocationColumn = Pick(target.InstallLocationColumn, defaults.InstallLocationColumn);
        target.DiscoveryScopeColumn = Pick(target.DiscoveryScopeColumn, defaults.DiscoveryScopeColumn);
        target.DiscoverySourceColumn = Pick(target.DiscoverySourceColumn, defaults.DiscoverySourceColumn);
        target.ClassificationColumn = Pick(target.ClassificationColumn, defaults.ClassificationColumn);
        target.CollectedAtUtcColumn = Pick(target.CollectedAtUtcColumn, defaults.CollectedAtUtcColumn);
        target.InstalledAtUtcColumn = Pick(target.InstalledAtUtcColumn, defaults.InstalledAtUtcColumn);
    }

    private static void Apply(SoftwarePolicyTableOptions target, SoftwarePolicyTableOptions defaults)
    {
        target.TableName = Pick(target.TableName, defaults.TableName);
        target.PrimaryKeyColumn = Pick(target.PrimaryKeyColumn, defaults.PrimaryKeyColumn);
        target.ClassificationColumn = Pick(target.ClassificationColumn, defaults.ClassificationColumn);
        target.ProductNameColumn = Pick(target.ProductNameColumn, defaults.ProductNameColumn);
        target.PublisherColumn = Pick(target.PublisherColumn, defaults.PublisherColumn);
        target.VersionPatternColumn = Pick(target.VersionPatternColumn, defaults.VersionPatternColumn);
        target.NotesColumn = Pick(target.NotesColumn, defaults.NotesColumn);
        target.EnabledColumn = Pick(target.EnabledColumn, defaults.EnabledColumn);
        target.UpdatedAtUtcColumn = Pick(target.UpdatedAtUtcColumn, defaults.UpdatedAtUtcColumn);
        target.DefaultLicenseSourceColumn = Pick(target.DefaultLicenseSourceColumn, defaults.DefaultLicenseSourceColumn);
    }

    private static void Apply(SoftwareViolationTableOptions target, SoftwareViolationTableOptions defaults)
    {
        target.TableName = Pick(target.TableName, defaults.TableName);
        target.PrimaryKeyColumn = Pick(target.PrimaryKeyColumn, defaults.PrimaryKeyColumn);
        target.PcForeignKeyColumn = Pick(target.PcForeignKeyColumn, defaults.PcForeignKeyColumn);
        target.PolicyForeignKeyColumn = Pick(target.PolicyForeignKeyColumn, defaults.PolicyForeignKeyColumn);
        target.DisplayNameColumn = Pick(target.DisplayNameColumn, defaults.DisplayNameColumn);
        target.DisplayVersionColumn = Pick(target.DisplayVersionColumn, defaults.DisplayVersionColumn);
        target.PublisherColumn = Pick(target.PublisherColumn, defaults.PublisherColumn);
        target.DetectedAtUtcColumn = Pick(target.DetectedAtUtcColumn, defaults.DetectedAtUtcColumn);
        target.LastSeenAtUtcColumn = Pick(target.LastSeenAtUtcColumn, defaults.LastSeenAtUtcColumn);
    }

    private static void Apply(StaleHeartbeatNotificationTableOptions target, StaleHeartbeatNotificationTableOptions defaults)
    {
        target.TableName = Pick(target.TableName, defaults.TableName);
        target.PrimaryKeyColumn = Pick(target.PrimaryKeyColumn, defaults.PrimaryKeyColumn);
        target.PcForeignKeyColumn = Pick(target.PcForeignKeyColumn, defaults.PcForeignKeyColumn);
        target.NotifiedAtUtcColumn = Pick(target.NotifiedAtUtcColumn, defaults.NotifiedAtUtcColumn);
    }

    private static void Apply(UninstallRequestTableOptions target, UninstallRequestTableOptions defaults)
    {
        target.TableName = Pick(target.TableName, defaults.TableName);
        target.PrimaryKeyColumn = Pick(target.PrimaryKeyColumn, defaults.PrimaryKeyColumn);
        target.PcForeignKeyColumn = Pick(target.PcForeignKeyColumn, defaults.PcForeignKeyColumn);
        target.StatusColumn = Pick(target.StatusColumn, defaults.StatusColumn);
        target.RequestedAtUtcColumn = Pick(target.RequestedAtUtcColumn, defaults.RequestedAtUtcColumn);
        target.ApprovedAtUtcColumn = Pick(target.ApprovedAtUtcColumn, defaults.ApprovedAtUtcColumn);
        target.ConsumedAtUtcColumn = Pick(target.ConsumedAtUtcColumn, defaults.ConsumedAtUtcColumn);
        target.ExpiresAtUtcColumn = Pick(target.ExpiresAtUtcColumn, defaults.ExpiresAtUtcColumn);
        target.CodeHashColumn = Pick(target.CodeHashColumn, defaults.CodeHashColumn);
        target.CodeColumn = Pick(target.CodeColumn, defaults.CodeColumn);
        target.OriginColumn = Pick(target.OriginColumn, defaults.OriginColumn);
    }

    private static void Apply(SoftwareLicenseTableOptions target, SoftwareLicenseTableOptions defaults)
    {
        target.TableName = Pick(target.TableName, defaults.TableName);
        target.PcForeignKeyColumn = Pick(target.PcForeignKeyColumn, defaults.PcForeignKeyColumn);
        target.SoftwareNameColumn = Pick(target.SoftwareNameColumn, defaults.SoftwareNameColumn);
        target.LicenseSourceColumn = Pick(target.LicenseSourceColumn, defaults.LicenseSourceColumn);
        target.UpdatedAtUtcColumn = Pick(target.UpdatedAtUtcColumn, defaults.UpdatedAtUtcColumn);
    }

    private static void Apply(WorkerUpdatePinTableOptions target, WorkerUpdatePinTableOptions defaults)
    {
        target.TableName = Pick(target.TableName, defaults.TableName);
        target.TargetServiceNameColumn = Pick(target.TargetServiceNameColumn, defaults.TargetServiceNameColumn);
        target.VersionColumn = Pick(target.VersionColumn, defaults.VersionColumn);
        target.PackageUrlColumn = Pick(target.PackageUrlColumn, defaults.PackageUrlColumn);
        target.Sha256Column = Pick(target.Sha256Column, defaults.Sha256Column);
        target.RequireAuthenticodeColumn = Pick(target.RequireAuthenticodeColumn, defaults.RequireAuthenticodeColumn);
        target.RollbackAfterMinutesColumn = Pick(target.RollbackAfterMinutesColumn, defaults.RollbackAfterMinutesColumn);
        target.UpdatedAtUtcColumn = Pick(target.UpdatedAtUtcColumn, defaults.UpdatedAtUtcColumn);
    }

    private static void Apply(UserMessageTableOptions target, UserMessageTableOptions defaults)
    {
        target.TableName = Pick(target.TableName, defaults.TableName);
        target.PrimaryKeyColumn = Pick(target.PrimaryKeyColumn, defaults.PrimaryKeyColumn);
        target.PcForeignKeyColumn = Pick(target.PcForeignKeyColumn, defaults.PcForeignKeyColumn);
        target.TitleColumn = Pick(target.TitleColumn, defaults.TitleColumn);
        target.BodyColumn = Pick(target.BodyColumn, defaults.BodyColumn);
        target.StatusColumn = Pick(target.StatusColumn, defaults.StatusColumn);
        target.CreatedAtUtcColumn = Pick(target.CreatedAtUtcColumn, defaults.CreatedAtUtcColumn);
        target.ConsumedAtUtcColumn = Pick(target.ConsumedAtUtcColumn, defaults.ConsumedAtUtcColumn);
    }

    private static string Pick(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
