using System.Security.Cryptography;
using System.Text;

namespace SwLicenseWatcher.Core;

public sealed class SqlServerSchemaScriptBuilder
{
    public string Build(SqlServerStorageOptions options)
    {
        SqlIdentifierValidator.Validate(options);
        var schema = Escape(options.SchemaName);
        var schemaLiteral = EscapeSqlLiteral(options.SchemaName);
        var schemaCommandIdentifier = EscapeSqlLiteral(schema);
        var pc = options.PcTable;
        var installedSoftware = options.InstalledSoftwareTable;
        var policy = options.SoftwarePolicyTable;
        var violation = options.SoftwareViolationTable;
        var staleHeartbeat = options.StaleHeartbeatNotificationTable;
        var uninstall = options.UninstallRequestTable;
        var license = options.SoftwareLicenseTable;
        var workerPin = options.WorkerUpdatePinTable;

        var sql = new StringBuilder();
        sql.AppendLine($"IF SCHEMA_ID(N'{schemaLiteral}') IS NULL EXEC(N'CREATE SCHEMA [{schemaCommandIdentifier}]');");
        sql.AppendLine();
        AppendTableIfMissing(sql, schema, pc.TableName);
        sql.AppendLine($"CREATE TABLE [{schema}].[{Escape(pc.TableName)}] (");
        sql.AppendLine($"    [{Escape(pc.PrimaryKeyColumn)}] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,");
        sql.AppendLine($"    [{Escape(pc.DeviceCodeColumn)}] NVARCHAR(128) NOT NULL,");
        sql.AppendLine($"    [{Escape(pc.HostNameColumn)}] NVARCHAR(128) NOT NULL,");
        sql.AppendLine($"    [{Escape(pc.DomainNameColumn)}] NVARCHAR(128) NOT NULL,");
        sql.AppendLine($"    [{Escape(pc.OperatingSystemColumn)}] NVARCHAR(128) NOT NULL,");
        sql.AppendLine($"    [{Escape(pc.AgentVersionColumn)}] NVARCHAR(32) NOT NULL,");
        sql.AppendLine($"    [{Escape(pc.LastHeartbeatUtcColumn)}] DATETIMEOFFSET NULL,");
        sql.AppendLine($"    [{Escape(pc.LastInventoryUtcColumn)}] DATETIMEOFFSET NULL,");
        sql.AppendLine($"    [{Escape(pc.AssignedHostNameColumn)}] NVARCHAR(128) NULL,");
        sql.AppendLine($"    [{Escape(pc.AdminNotesColumn)}] NVARCHAR(1024) NULL,");
        sql.AppendLine($"    CONSTRAINT [{Escape(BuildIdentifier("UX", pc.TableName, pc.DeviceCodeColumn))}] UNIQUE ([{Escape(pc.DeviceCodeColumn)}])");
        sql.AppendLine(");");
        sql.AppendLine("END");
        sql.AppendLine();
        AppendTableIfMissing(sql, schema, installedSoftware.TableName);
        sql.AppendLine($"CREATE TABLE [{schema}].[{Escape(installedSoftware.TableName)}] (");
        sql.AppendLine($"    [{Escape(installedSoftware.PrimaryKeyColumn)}] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,");
        sql.AppendLine($"    [{Escape(installedSoftware.PcForeignKeyColumn)}] BIGINT NOT NULL,");
        sql.AppendLine($"    [{Escape(installedSoftware.DisplayNameColumn)}] NVARCHAR(256) NOT NULL,");
        sql.AppendLine($"    [{Escape(installedSoftware.DisplayVersionColumn)}] NVARCHAR(64) NULL,");
        sql.AppendLine($"    [{Escape(installedSoftware.PublisherColumn)}] NVARCHAR(256) NULL,");
        sql.AppendLine($"    [{Escape(installedSoftware.InstallLocationColumn)}] NVARCHAR(512) NULL,");
        sql.AppendLine($"    [{Escape(installedSoftware.DiscoveryScopeColumn)}] NVARCHAR(256) NOT NULL,");
        sql.AppendLine($"    [{Escape(installedSoftware.DiscoverySourceColumn)}] NVARCHAR(64) NOT NULL,");
        sql.AppendLine($"    [{Escape(installedSoftware.ClassificationColumn)}] NVARCHAR(32) NOT NULL,");
        sql.AppendLine($"    [{Escape(installedSoftware.CollectedAtUtcColumn)}] DATETIMEOFFSET NOT NULL,");
        sql.AppendLine($"    CONSTRAINT [{Escape(BuildIdentifier("FK", installedSoftware.TableName, pc.TableName))}] FOREIGN KEY ([{Escape(installedSoftware.PcForeignKeyColumn)}]) REFERENCES [{schema}].[{Escape(pc.TableName)}]([{Escape(pc.PrimaryKeyColumn)}])");
        sql.AppendLine(");");
        sql.AppendLine("END");
        sql.AppendLine();
        AppendTableIfMissing(sql, schema, policy.TableName);
        sql.AppendLine($"CREATE TABLE [{schema}].[{Escape(policy.TableName)}] (");
        sql.AppendLine($"    [{Escape(policy.PrimaryKeyColumn)}] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,");
        sql.AppendLine($"    [{Escape(policy.ClassificationColumn)}] NVARCHAR(32) NOT NULL,");
        sql.AppendLine($"    [{Escape(policy.ProductNameColumn)}] NVARCHAR(256) NOT NULL,");
        sql.AppendLine($"    [{Escape(policy.PublisherColumn)}] NVARCHAR(256) NULL,");
        sql.AppendLine($"    [{Escape(policy.VersionPatternColumn)}] NVARCHAR(64) NULL,");
        sql.AppendLine($"    [{Escape(policy.NotesColumn)}] NVARCHAR(1024) NULL,");
        sql.AppendLine($"    [{Escape(policy.EnabledColumn)}] BIT NOT NULL CONSTRAINT [{Escape(BuildIdentifier("DF", policy.TableName, policy.EnabledColumn))}] DEFAULT(1),");
        sql.AppendLine($"    [{Escape(policy.UpdatedAtUtcColumn)}] DATETIMEOFFSET NOT NULL,");
        sql.AppendLine($"    [{Escape(policy.DefaultLicenseSourceColumn)}] NVARCHAR(16) NULL");
        sql.AppendLine(");");
        sql.AppendLine("END");
        sql.AppendLine();
        AppendTableIfMissing(sql, schema, violation.TableName);
        sql.AppendLine($"CREATE TABLE [{schema}].[{Escape(violation.TableName)}] (");
        sql.AppendLine($"    [{Escape(violation.PrimaryKeyColumn)}] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,");
        sql.AppendLine($"    [{Escape(violation.PcForeignKeyColumn)}] BIGINT NOT NULL,");
        sql.AppendLine($"    [{Escape(violation.PolicyForeignKeyColumn)}] BIGINT NOT NULL,");
        sql.AppendLine($"    [{Escape(violation.DisplayNameColumn)}] NVARCHAR(256) NOT NULL,");
        sql.AppendLine($"    [{Escape(violation.DisplayVersionColumn)}] NVARCHAR(64) NULL,");
        sql.AppendLine($"    [{Escape(violation.PublisherColumn)}] NVARCHAR(256) NULL,");
        sql.AppendLine($"    [{Escape(violation.DetectedAtUtcColumn)}] DATETIMEOFFSET NOT NULL,");
        sql.AppendLine($"    [{Escape(violation.LastSeenAtUtcColumn)}] DATETIMEOFFSET NOT NULL,");
        sql.AppendLine($"    CONSTRAINT [{Escape(BuildIdentifier("FK", violation.TableName, pc.TableName))}] FOREIGN KEY ([{Escape(violation.PcForeignKeyColumn)}]) REFERENCES [{schema}].[{Escape(pc.TableName)}]([{Escape(pc.PrimaryKeyColumn)}]) ON DELETE CASCADE,");
        sql.AppendLine($"    CONSTRAINT [{Escape(BuildIdentifier("FK", violation.TableName, policy.TableName))}] FOREIGN KEY ([{Escape(violation.PolicyForeignKeyColumn)}]) REFERENCES [{schema}].[{Escape(policy.TableName)}]([{Escape(policy.PrimaryKeyColumn)}]) ON DELETE CASCADE,");
        sql.AppendLine($"    CONSTRAINT [{Escape(BuildIdentifier("UX", violation.TableName, violation.PcForeignKeyColumn, violation.DisplayNameColumn))}] UNIQUE ([{Escape(violation.PcForeignKeyColumn)}], [{Escape(violation.DisplayNameColumn)}])");
        sql.AppendLine(");");
        sql.AppendLine("END");
        sql.AppendLine();
        AppendTableIfMissing(sql, schema, staleHeartbeat.TableName);
        sql.AppendLine($"CREATE TABLE [{schema}].[{Escape(staleHeartbeat.TableName)}] (");
        sql.AppendLine($"    [{Escape(staleHeartbeat.PrimaryKeyColumn)}] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,");
        sql.AppendLine($"    [{Escape(staleHeartbeat.PcForeignKeyColumn)}] BIGINT NOT NULL,");
        sql.AppendLine($"    [{Escape(staleHeartbeat.NotifiedAtUtcColumn)}] DATETIMEOFFSET NOT NULL,");
        sql.AppendLine($"    CONSTRAINT [{Escape(BuildIdentifier("FK", staleHeartbeat.TableName, pc.TableName))}] FOREIGN KEY ([{Escape(staleHeartbeat.PcForeignKeyColumn)}]) REFERENCES [{schema}].[{Escape(pc.TableName)}]([{Escape(pc.PrimaryKeyColumn)}]) ON DELETE CASCADE,");
        sql.AppendLine($"    CONSTRAINT [{Escape(BuildIdentifier("UX", staleHeartbeat.TableName, staleHeartbeat.PcForeignKeyColumn))}] UNIQUE ([{Escape(staleHeartbeat.PcForeignKeyColumn)}])");
        sql.AppendLine(");");
        sql.AppendLine("END");
        sql.AppendLine();
        AppendTableIfMissing(sql, schema, uninstall.TableName);
        sql.AppendLine($"CREATE TABLE [{schema}].[{Escape(uninstall.TableName)}] (");
        sql.AppendLine($"    [{Escape(uninstall.PrimaryKeyColumn)}] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,");
        sql.AppendLine($"    [{Escape(uninstall.PcForeignKeyColumn)}] BIGINT NOT NULL,");
        sql.AppendLine($"    [{Escape(uninstall.StatusColumn)}] NVARCHAR(16) NOT NULL,");
        sql.AppendLine($"    [{Escape(uninstall.RequestedAtUtcColumn)}] DATETIMEOFFSET NOT NULL,");
        sql.AppendLine($"    [{Escape(uninstall.ApprovedAtUtcColumn)}] DATETIMEOFFSET NULL,");
        sql.AppendLine($"    [{Escape(uninstall.ConsumedAtUtcColumn)}] DATETIMEOFFSET NULL,");
        sql.AppendLine($"    [{Escape(uninstall.ExpiresAtUtcColumn)}] DATETIMEOFFSET NULL,");
        sql.AppendLine($"    [{Escape(uninstall.CodeHashColumn)}] NVARCHAR(64) NULL,");
        sql.AppendLine($"    [{Escape(uninstall.CodeColumn)}] NVARCHAR(16) NULL,");
        sql.AppendLine($"    CONSTRAINT [{Escape(BuildIdentifier("FK", uninstall.TableName, pc.TableName))}] FOREIGN KEY ([{Escape(uninstall.PcForeignKeyColumn)}]) REFERENCES [{schema}].[{Escape(pc.TableName)}]([{Escape(pc.PrimaryKeyColumn)}]) ON DELETE CASCADE");
        sql.AppendLine(");");
        sql.AppendLine("END");
        sql.AppendLine();
        AppendTableIfMissing(sql, schema, license.TableName);
        sql.AppendLine($"CREATE TABLE [{schema}].[{Escape(license.TableName)}] (");
        sql.AppendLine($"    [{Escape(license.PcForeignKeyColumn)}] BIGINT NOT NULL,");
        sql.AppendLine($"    [{Escape(license.SoftwareNameColumn)}] NVARCHAR(256) NOT NULL,");
        sql.AppendLine($"    [{Escape(license.LicenseSourceColumn)}] NVARCHAR(16) NOT NULL,");
        sql.AppendLine($"    [{Escape(license.UpdatedAtUtcColumn)}] DATETIMEOFFSET NOT NULL,");
        sql.AppendLine($"    CONSTRAINT [{Escape(BuildIdentifier("UX", license.TableName, license.PcForeignKeyColumn, license.SoftwareNameColumn))}] UNIQUE ([{Escape(license.PcForeignKeyColumn)}], [{Escape(license.SoftwareNameColumn)}]),");
        sql.AppendLine($"    CONSTRAINT [{Escape(BuildIdentifier("FK", license.TableName, pc.TableName))}] FOREIGN KEY ([{Escape(license.PcForeignKeyColumn)}]) REFERENCES [{schema}].[{Escape(pc.TableName)}]([{Escape(pc.PrimaryKeyColumn)}]) ON DELETE CASCADE");
        sql.AppendLine(");");
        sql.AppendLine("END");
        sql.AppendLine();
        AppendTableIfMissing(sql, schema, workerPin.TableName);
        sql.AppendLine($"CREATE TABLE [{schema}].[{Escape(workerPin.TableName)}] (");
        sql.AppendLine($"    [{Escape(workerPin.TargetServiceNameColumn)}] NVARCHAR(128) NOT NULL,");
        sql.AppendLine($"    [{Escape(workerPin.VersionColumn)}] NVARCHAR(32) NOT NULL,");
        sql.AppendLine($"    [{Escape(workerPin.PackageUrlColumn)}] NVARCHAR(2048) NOT NULL,");
        sql.AppendLine($"    [{Escape(workerPin.Sha256Column)}] NVARCHAR(64) NOT NULL,");
        sql.AppendLine($"    [{Escape(workerPin.RequireAuthenticodeColumn)}] BIT NOT NULL CONSTRAINT [{Escape(BuildIdentifier("DF", workerPin.TableName, workerPin.RequireAuthenticodeColumn))}] DEFAULT(1),");
        sql.AppendLine($"    [{Escape(workerPin.RollbackAfterMinutesColumn)}] INT NOT NULL,");
        sql.AppendLine($"    [{Escape(workerPin.UpdatedAtUtcColumn)}] DATETIMEOFFSET NOT NULL,");
        sql.AppendLine($"    CONSTRAINT [{Escape(BuildIdentifier("PK", workerPin.TableName))}] PRIMARY KEY ([{Escape(workerPin.TargetServiceNameColumn)}])");
        sql.AppendLine(");");
        sql.AppendLine("END");
        sql.AppendLine();
        AppendColumnIfMissing(sql, schema, policy.TableName, policy.DefaultLicenseSourceColumn, "NVARCHAR(16) NULL");
        AppendColumnIfMissing(sql, schema, pc.TableName, pc.AssignedHostNameColumn, "NVARCHAR(128) NULL");
        AppendColumnIfMissing(sql, schema, pc.TableName, pc.AdminNotesColumn, "NVARCHAR(1024) NULL");
        sql.AppendLine();
        AppendIndexIfMissing(sql, schema, installedSoftware.TableName, installedSoftware.PcForeignKeyColumn);
        AppendIndexIfMissing(sql, schema, installedSoftware.TableName, installedSoftware.ClassificationColumn);
        AppendIndexIfMissing(sql, schema, policy.TableName, policy.ClassificationColumn);
        AppendIndexIfMissing(sql, schema, violation.TableName, violation.PolicyForeignKeyColumn);
        AppendIndexIfMissing(sql, schema, uninstall.TableName, uninstall.PcForeignKeyColumn);
        AppendIndexIfMissing(sql, schema, uninstall.TableName, uninstall.StatusColumn);
        AppendIndexIfMissing(sql, schema, license.TableName, license.PcForeignKeyColumn);
        return sql.ToString();
    }

    private static void AppendTableIfMissing(StringBuilder sql, string escapedSchema, string tableName)
    {
        var objectId = EscapeSqlLiteral($"[{escapedSchema}].[{Escape(tableName)}]");
        sql.AppendLine($"IF OBJECT_ID(N'{objectId}', N'U') IS NULL");
        sql.AppendLine("BEGIN");
    }

    private static void AppendColumnIfMissing(
        StringBuilder sql,
        string escapedSchema,
        string tableName,
        string columnName,
        string definition)
    {
        var objectId = EscapeSqlLiteral($"[{escapedSchema}].[{Escape(tableName)}]");
        var columnLiteral = EscapeSqlLiteral(columnName);
        sql.AppendLine($"IF COL_LENGTH(N'{objectId}', N'{columnLiteral}') IS NULL");
        sql.AppendLine($"ALTER TABLE [{escapedSchema}].[{Escape(tableName)}] ADD [{Escape(columnName)}] {definition};");
    }

    private static void AppendIndexIfMissing(StringBuilder sql, string escapedSchema, string tableName, string columnName)
    {
        var indexName = Escape(BuildIdentifier("IX", tableName, columnName));
        var objectId = EscapeSqlLiteral($"[{escapedSchema}].[{Escape(tableName)}]");
        var indexLiteral = EscapeSqlLiteral(indexName);
        sql.AppendLine($"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{indexLiteral}' AND object_id = OBJECT_ID(N'{objectId}'))");
        sql.AppendLine($"CREATE INDEX [{indexName}] ON [{escapedSchema}].[{Escape(tableName)}]([{Escape(columnName)}]);");
    }

    private static string BuildIdentifier(string prefix, params string[] parts)
    {
        var identifier = string.Join('_', [prefix, .. parts]);
        if (identifier.Length <= 128)
        {
            return identifier;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identifier)))[..16];
        return $"{identifier[..(128 - hash.Length - 1)]}_{hash}";
    }

    internal static string Escape(string identifier) => identifier.Replace("]", "]]", StringComparison.Ordinal);

    internal static string EscapeSqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}

public static class SqlIdentifierValidator
{
    public static bool IsValid(string? identifier) =>
        !string.IsNullOrWhiteSpace(identifier) &&
        identifier.Length <= 128 &&
        (char.IsAsciiLetter(identifier[0]) || identifier[0] == '_') &&
        identifier.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    public static void Validate(SqlServerStorageOptions options)
    {
        var identifiers = new[]
        {
            options.SchemaName,
            options.PcTable.TableName, options.PcTable.PrimaryKeyColumn, options.PcTable.DeviceCodeColumn,
            options.PcTable.HostNameColumn, options.PcTable.DomainNameColumn, options.PcTable.OperatingSystemColumn,
            options.PcTable.AgentVersionColumn, options.PcTable.LastHeartbeatUtcColumn, options.PcTable.LastInventoryUtcColumn,
            options.PcTable.AssignedHostNameColumn, options.PcTable.AdminNotesColumn,
            options.InstalledSoftwareTable.TableName, options.InstalledSoftwareTable.PrimaryKeyColumn,
            options.InstalledSoftwareTable.PcForeignKeyColumn, options.InstalledSoftwareTable.DisplayNameColumn,
            options.InstalledSoftwareTable.DisplayVersionColumn, options.InstalledSoftwareTable.PublisherColumn,
            options.InstalledSoftwareTable.InstallLocationColumn, options.InstalledSoftwareTable.DiscoveryScopeColumn,
            options.InstalledSoftwareTable.DiscoverySourceColumn, options.InstalledSoftwareTable.ClassificationColumn,
            options.InstalledSoftwareTable.CollectedAtUtcColumn,
            options.SoftwarePolicyTable.TableName, options.SoftwarePolicyTable.PrimaryKeyColumn,
            options.SoftwarePolicyTable.ClassificationColumn, options.SoftwarePolicyTable.ProductNameColumn,
            options.SoftwarePolicyTable.PublisherColumn, options.SoftwarePolicyTable.VersionPatternColumn,
            options.SoftwarePolicyTable.NotesColumn, options.SoftwarePolicyTable.EnabledColumn,
            options.SoftwarePolicyTable.UpdatedAtUtcColumn, options.SoftwarePolicyTable.DefaultLicenseSourceColumn,
            options.SoftwareViolationTable.TableName, options.SoftwareViolationTable.PrimaryKeyColumn,
            options.SoftwareViolationTable.PcForeignKeyColumn, options.SoftwareViolationTable.PolicyForeignKeyColumn,
            options.SoftwareViolationTable.DisplayNameColumn, options.SoftwareViolationTable.DisplayVersionColumn,
            options.SoftwareViolationTable.PublisherColumn, options.SoftwareViolationTable.DetectedAtUtcColumn,
            options.SoftwareViolationTable.LastSeenAtUtcColumn,
            options.StaleHeartbeatNotificationTable.TableName, options.StaleHeartbeatNotificationTable.PrimaryKeyColumn,
            options.StaleHeartbeatNotificationTable.PcForeignKeyColumn, options.StaleHeartbeatNotificationTable.NotifiedAtUtcColumn,
            options.UninstallRequestTable.TableName, options.UninstallRequestTable.PrimaryKeyColumn,
            options.UninstallRequestTable.PcForeignKeyColumn, options.UninstallRequestTable.StatusColumn,
            options.UninstallRequestTable.RequestedAtUtcColumn, options.UninstallRequestTable.ApprovedAtUtcColumn,
            options.UninstallRequestTable.ConsumedAtUtcColumn, options.UninstallRequestTable.ExpiresAtUtcColumn,
            options.UninstallRequestTable.CodeHashColumn, options.UninstallRequestTable.CodeColumn,
            options.SoftwareLicenseTable.TableName, options.SoftwareLicenseTable.PcForeignKeyColumn,
            options.SoftwareLicenseTable.SoftwareNameColumn, options.SoftwareLicenseTable.LicenseSourceColumn,
            options.SoftwareLicenseTable.UpdatedAtUtcColumn,
            options.WorkerUpdatePinTable.TableName, options.WorkerUpdatePinTable.TargetServiceNameColumn,
            options.WorkerUpdatePinTable.VersionColumn, options.WorkerUpdatePinTable.PackageUrlColumn,
            options.WorkerUpdatePinTable.Sha256Column, options.WorkerUpdatePinTable.RequireAuthenticodeColumn,
            options.WorkerUpdatePinTable.RollbackAfterMinutesColumn, options.WorkerUpdatePinTable.UpdatedAtUtcColumn
        };

        if (identifiers.Any(identifier => !IsValid(identifier)))
        {
            throw new ArgumentException("SQL identifiers must start with a letter or underscore and contain only ASCII letters, digits, or underscores (maximum 128 characters).");
        }
    }
}
