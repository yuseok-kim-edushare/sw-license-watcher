using System.Text;

namespace SwLicenseWatcher.Core;

public sealed class MySqlSchemaScriptBuilder : ISchemaScriptBuilder
{
    public string Build(SqlServerStorageOptions options) =>
        string.Join(Environment.NewLine + Environment.NewLine, BuildBatches(options));

    public IReadOnlyList<string> BuildBatches(SqlServerStorageOptions options)
    {
        SqlIdentifierValidator.Validate(options, SqlIdentifierValidator.MySqlMaxLength);
        var pc = options.PcTable;
        var installedSoftware = options.InstalledSoftwareTable;
        var policy = options.SoftwarePolicyTable;
        var violation = options.SoftwareViolationTable;
        var staleHeartbeat = options.StaleHeartbeatNotificationTable;
        var uninstall = options.UninstallRequestTable;
        var license = options.SoftwareLicenseTable;
        var workerPin = options.WorkerUpdatePinTable;
        var userMessage = options.UserMessageTable;
        var statements = new List<string>();

        statements.Add(CreatePc(pc));
        statements.Add(CreateInstalledSoftware(installedSoftware, pc));
        statements.Add(CreatePolicy(policy));
        statements.Add(CreateViolation(violation, pc, policy));
        statements.Add(CreateStaleHeartbeat(staleHeartbeat, pc));
        statements.Add(CreateUninstall(uninstall, pc));
        statements.Add(CreateLicense(license, pc));
        statements.Add(CreateWorkerPin(workerPin));
        statements.Add(CreateUserMessage(userMessage, pc));

        AddColumn(statements, policy.TableName, policy.DefaultLicenseSourceColumn, "VARCHAR(16) NULL");
        AddColumn(statements, pc.TableName, pc.AssignedHostNameColumn, "VARCHAR(128) NULL");
        AddColumn(statements, pc.TableName, pc.AdminNotesColumn, "VARCHAR(1024) NULL");
        AddColumn(statements, pc.TableName, pc.AssignedDeviceCodeColumn, "VARCHAR(128) NULL");
        AddColumn(statements, pc.TableName, pc.DeviceIdColumn, "VARCHAR(36) NULL");
        AddColumn(statements, pc.TableName, pc.DevicePublicKeyColumn, "LONGTEXT NULL");
        AddColumn(statements, pc.TableName, pc.DeviceCertificateColumn, "LONGTEXT NULL");
        AddColumn(
            statements,
            uninstall.TableName,
            uninstall.OriginColumn,
            $"VARCHAR(16) NOT NULL DEFAULT '{UninstallGrant.OriginAgent}'");

        AddUniqueIndex(statements, pc.TableName, pc.DeviceIdColumn);
        AddUniqueIndex(statements, pc.TableName, pc.AssignedDeviceCodeColumn);
        AddIndex(statements, installedSoftware.TableName, installedSoftware.PcForeignKeyColumn);
        AddIndex(statements, installedSoftware.TableName, installedSoftware.ClassificationColumn);
        AddIndex(statements, policy.TableName, policy.ClassificationColumn);
        AddIndex(statements, violation.TableName, violation.PolicyForeignKeyColumn);
        AddIndex(statements, uninstall.TableName, uninstall.PcForeignKeyColumn);
        AddIndex(statements, uninstall.TableName, uninstall.StatusColumn);
        AddIndex(statements, license.TableName, license.PcForeignKeyColumn);
        AddIndex(statements, userMessage.TableName, userMessage.PcForeignKeyColumn);
        AddIndex(statements, userMessage.TableName, userMessage.StatusColumn);
        return statements;
    }

    private static string CreatePc(PcTableOptions pc)
    {
        var table = Q(pc.TableName);
        return $"""
            CREATE TABLE IF NOT EXISTS {table} (
                {Q(pc.PrimaryKeyColumn)} BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                {Q(pc.DeviceCodeColumn)} VARCHAR(128) NOT NULL,
                {Q(pc.HostNameColumn)} VARCHAR(128) NOT NULL,
                {Q(pc.DomainNameColumn)} VARCHAR(128) NOT NULL,
                {Q(pc.OperatingSystemColumn)} VARCHAR(128) NOT NULL,
                {Q(pc.AgentVersionColumn)} VARCHAR(32) NOT NULL,
                {Q(pc.LastHeartbeatUtcColumn)} DATETIME(6) NULL,
                {Q(pc.LastInventoryUtcColumn)} DATETIME(6) NULL,
                {Q(pc.AssignedHostNameColumn)} VARCHAR(128) NULL,
                {Q(pc.AdminNotesColumn)} VARCHAR(1024) NULL,
                {Q(pc.AssignedDeviceCodeColumn)} VARCHAR(128) NULL,
                {Q(pc.DeviceIdColumn)} VARCHAR(36) NULL,
                {Q(pc.DevicePublicKeyColumn)} LONGTEXT NULL,
                {Q(pc.DeviceCertificateColumn)} LONGTEXT NULL,
                UNIQUE KEY {Q(Ident("UX", pc.TableName, pc.DeviceCodeColumn))} ({Q(pc.DeviceCodeColumn)})
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
            """;
    }

    private static string CreateInstalledSoftware(InstalledSoftwareTableOptions software, PcTableOptions pc) => $"""
        CREATE TABLE IF NOT EXISTS {Q(software.TableName)} (
            {Q(software.PrimaryKeyColumn)} BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            {Q(software.PcForeignKeyColumn)} BIGINT NOT NULL,
            {Q(software.DisplayNameColumn)} VARCHAR(256) NOT NULL,
            {Q(software.DisplayVersionColumn)} VARCHAR(64) NULL,
            {Q(software.PublisherColumn)} VARCHAR(256) NULL,
            {Q(software.InstallLocationColumn)} VARCHAR(512) NULL,
            {Q(software.DiscoveryScopeColumn)} VARCHAR(256) NOT NULL,
            {Q(software.DiscoverySourceColumn)} VARCHAR(64) NOT NULL,
            {Q(software.ClassificationColumn)} VARCHAR(32) NOT NULL,
            {Q(software.CollectedAtUtcColumn)} DATETIME(6) NOT NULL,
            CONSTRAINT {Q(Ident("FK", software.TableName, pc.TableName))} FOREIGN KEY ({Q(software.PcForeignKeyColumn)}) REFERENCES {Q(pc.TableName)} ({Q(pc.PrimaryKeyColumn)})
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """;

    private static string CreatePolicy(SoftwarePolicyTableOptions policy) => $"""
        CREATE TABLE IF NOT EXISTS {Q(policy.TableName)} (
            {Q(policy.PrimaryKeyColumn)} BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            {Q(policy.ClassificationColumn)} VARCHAR(32) NOT NULL,
            {Q(policy.ProductNameColumn)} VARCHAR(256) NOT NULL,
            {Q(policy.PublisherColumn)} VARCHAR(256) NULL,
            {Q(policy.VersionPatternColumn)} VARCHAR(64) NULL,
            {Q(policy.NotesColumn)} VARCHAR(1024) NULL,
            {Q(policy.EnabledColumn)} TINYINT(1) NOT NULL DEFAULT 1,
            {Q(policy.UpdatedAtUtcColumn)} DATETIME(6) NOT NULL,
            {Q(policy.DefaultLicenseSourceColumn)} VARCHAR(16) NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """;

    private static string CreateViolation(
        SoftwareViolationTableOptions violation,
        PcTableOptions pc,
        SoftwarePolicyTableOptions policy) => $"""
        CREATE TABLE IF NOT EXISTS {Q(violation.TableName)} (
            {Q(violation.PrimaryKeyColumn)} BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            {Q(violation.PcForeignKeyColumn)} BIGINT NOT NULL,
            {Q(violation.PolicyForeignKeyColumn)} BIGINT NOT NULL,
            {Q(violation.DisplayNameColumn)} VARCHAR(256) NOT NULL,
            {Q(violation.DisplayVersionColumn)} VARCHAR(64) NULL,
            {Q(violation.PublisherColumn)} VARCHAR(256) NULL,
            {Q(violation.DetectedAtUtcColumn)} DATETIME(6) NOT NULL,
            {Q(violation.LastSeenAtUtcColumn)} DATETIME(6) NOT NULL,
            CONSTRAINT {Q(Ident("FK", violation.TableName, pc.TableName))} FOREIGN KEY ({Q(violation.PcForeignKeyColumn)}) REFERENCES {Q(pc.TableName)} ({Q(pc.PrimaryKeyColumn)}) ON DELETE CASCADE,
            CONSTRAINT {Q(Ident("FK", violation.TableName, policy.TableName))} FOREIGN KEY ({Q(violation.PolicyForeignKeyColumn)}) REFERENCES {Q(policy.TableName)} ({Q(policy.PrimaryKeyColumn)}) ON DELETE CASCADE,
            CONSTRAINT {Q(Ident("UX", violation.TableName, violation.PcForeignKeyColumn, violation.DisplayNameColumn))} UNIQUE ({Q(violation.PcForeignKeyColumn)}, {Q(violation.DisplayNameColumn)})
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """;

    private static string CreateStaleHeartbeat(StaleHeartbeatNotificationTableOptions stale, PcTableOptions pc) => $"""
        CREATE TABLE IF NOT EXISTS {Q(stale.TableName)} (
            {Q(stale.PrimaryKeyColumn)} BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            {Q(stale.PcForeignKeyColumn)} BIGINT NOT NULL,
            {Q(stale.NotifiedAtUtcColumn)} DATETIME(6) NOT NULL,
            CONSTRAINT {Q(Ident("FK", stale.TableName, pc.TableName))} FOREIGN KEY ({Q(stale.PcForeignKeyColumn)}) REFERENCES {Q(pc.TableName)} ({Q(pc.PrimaryKeyColumn)}) ON DELETE CASCADE,
            CONSTRAINT {Q(Ident("UX", stale.TableName, stale.PcForeignKeyColumn))} UNIQUE ({Q(stale.PcForeignKeyColumn)})
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """;

    private static string CreateUninstall(UninstallRequestTableOptions uninstall, PcTableOptions pc) => $"""
        CREATE TABLE IF NOT EXISTS {Q(uninstall.TableName)} (
            {Q(uninstall.PrimaryKeyColumn)} BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            {Q(uninstall.PcForeignKeyColumn)} BIGINT NOT NULL,
            {Q(uninstall.StatusColumn)} VARCHAR(16) NOT NULL,
            {Q(uninstall.RequestedAtUtcColumn)} DATETIME(6) NOT NULL,
            {Q(uninstall.ApprovedAtUtcColumn)} DATETIME(6) NULL,
            {Q(uninstall.ConsumedAtUtcColumn)} DATETIME(6) NULL,
            {Q(uninstall.ExpiresAtUtcColumn)} DATETIME(6) NULL,
            {Q(uninstall.CodeHashColumn)} VARCHAR(64) NULL,
            {Q(uninstall.CodeColumn)} VARCHAR(16) NULL,
            {Q(uninstall.OriginColumn)} VARCHAR(16) NOT NULL DEFAULT '{UninstallGrant.OriginAgent}',
            CONSTRAINT {Q(Ident("FK", uninstall.TableName, pc.TableName))} FOREIGN KEY ({Q(uninstall.PcForeignKeyColumn)}) REFERENCES {Q(pc.TableName)} ({Q(pc.PrimaryKeyColumn)}) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """;

    private static string CreateLicense(SoftwareLicenseTableOptions license, PcTableOptions pc) => $"""
        CREATE TABLE IF NOT EXISTS {Q(license.TableName)} (
            {Q(license.PcForeignKeyColumn)} BIGINT NOT NULL,
            {Q(license.SoftwareNameColumn)} VARCHAR(256) NOT NULL,
            {Q(license.LicenseSourceColumn)} VARCHAR(16) NOT NULL,
            {Q(license.UpdatedAtUtcColumn)} DATETIME(6) NOT NULL,
            CONSTRAINT {Q(Ident("UX", license.TableName, license.PcForeignKeyColumn, license.SoftwareNameColumn))} UNIQUE ({Q(license.PcForeignKeyColumn)}, {Q(license.SoftwareNameColumn)}),
            CONSTRAINT {Q(Ident("FK", license.TableName, pc.TableName))} FOREIGN KEY ({Q(license.PcForeignKeyColumn)}) REFERENCES {Q(pc.TableName)} ({Q(pc.PrimaryKeyColumn)}) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """;

    private static string CreateWorkerPin(WorkerUpdatePinTableOptions pin) => $"""
        CREATE TABLE IF NOT EXISTS {Q(pin.TableName)} (
            {Q(pin.TargetServiceNameColumn)} VARCHAR(128) NOT NULL,
            {Q(pin.VersionColumn)} VARCHAR(32) NOT NULL,
            {Q(pin.PackageUrlColumn)} VARCHAR(2048) NOT NULL,
            {Q(pin.Sha256Column)} VARCHAR(64) NOT NULL,
            {Q(pin.RequireAuthenticodeColumn)} TINYINT(1) NOT NULL DEFAULT 1,
            {Q(pin.RollbackAfterMinutesColumn)} INT NOT NULL,
            {Q(pin.UpdatedAtUtcColumn)} DATETIME(6) NOT NULL,
            CONSTRAINT {Q(Ident("PK", pin.TableName))} PRIMARY KEY ({Q(pin.TargetServiceNameColumn)})
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """;

    private static string CreateUserMessage(UserMessageTableOptions message, PcTableOptions pc) => $"""
        CREATE TABLE IF NOT EXISTS {Q(message.TableName)} (
            {Q(message.PrimaryKeyColumn)} BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            {Q(message.PcForeignKeyColumn)} BIGINT NOT NULL,
            {Q(message.TitleColumn)} VARCHAR(128) NOT NULL,
            {Q(message.BodyColumn)} VARCHAR(1024) NOT NULL,
            {Q(message.StatusColumn)} VARCHAR(16) NOT NULL,
            {Q(message.CreatedAtUtcColumn)} DATETIME(6) NOT NULL,
            {Q(message.ConsumedAtUtcColumn)} DATETIME(6) NULL,
            CONSTRAINT {Q(Ident("FK", message.TableName, pc.TableName))} FOREIGN KEY ({Q(message.PcForeignKeyColumn)}) REFERENCES {Q(pc.TableName)} ({Q(pc.PrimaryKeyColumn)}) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        """;

    private static void AddColumn(List<string> statements, string table, string column, string definition)
    {
        var tableLiteral = EscapeLiteral(table);
        var columnLiteral = EscapeLiteral(column);
        var alter = EscapeLiteral($"ALTER TABLE {Q(table)} ADD COLUMN {Q(column)} {definition}");
        statements.Add($"""
            SET @swlw_exists := (
                SELECT COUNT(*) FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableLiteral}' AND COLUMN_NAME = '{columnLiteral}');
            SET @swlw_sql := IF(@swlw_exists = 0, '{alter}', 'DO 0');
            PREPARE swlw_stmt FROM @swlw_sql;
            EXECUTE swlw_stmt;
            DEALLOCATE PREPARE swlw_stmt
            """);
    }

    private static void AddIndex(List<string> statements, string table, string column) =>
        AddIndexCore(statements, table, Ident("IX", table, column), $"CREATE INDEX {Q(Ident("IX", table, column))} ON {Q(table)} ({Q(column)})");

    private static void AddUniqueIndex(List<string> statements, string table, string column) =>
        AddIndexCore(statements, table, Ident("UX", table, column), $"CREATE UNIQUE INDEX {Q(Ident("UX", table, column))} ON {Q(table)} ({Q(column)})");

    private static void AddIndexCore(List<string> statements, string table, string indexName, string createSql)
    {
        var tableLiteral = EscapeLiteral(table);
        var indexLiteral = EscapeLiteral(indexName);
        var create = EscapeLiteral(createSql);
        statements.Add($"""
            SET @swlw_exists := (
                SELECT COUNT(*) FROM information_schema.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{tableLiteral}' AND INDEX_NAME = '{indexLiteral}');
            SET @swlw_sql := IF(@swlw_exists = 0, '{create}', 'DO 0');
            PREPARE swlw_stmt FROM @swlw_sql;
            EXECUTE swlw_stmt;
            DEALLOCATE PREPARE swlw_stmt
            """);
    }

    private static string Ident(string prefix, params string[] parts) =>
        SchemaIdentifiers.Build(prefix, SqlIdentifierValidator.MySqlMaxLength, parts);

    private static string Q(string identifier) => $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";

    private static string EscapeLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal).Replace("\\", "\\\\", StringComparison.Ordinal);
}
