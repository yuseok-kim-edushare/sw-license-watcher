using System.Text;

namespace SwLicenseWatcher.Core;

public sealed class SqlServerSchemaScriptBuilder
{
    public string Build(SqlServerStorageOptions options)
    {
        var schema = Escape(options.SchemaName);
        var schemaLiteral = EscapeSqlLiteral(options.SchemaName);
        var pc = options.PcTable;
        var installedSoftware = options.InstalledSoftwareTable;
        var policy = options.SoftwarePolicyTable;

        var sql = new StringBuilder();
        sql.AppendLine($"IF SCHEMA_ID(N'{schemaLiteral}') IS NULL EXEC(N'CREATE SCHEMA [{schema}]');");
        sql.AppendLine();
        sql.AppendLine($"CREATE TABLE [{schema}].[{Escape(pc.TableName)}] (");
        sql.AppendLine($"    [{Escape(pc.PrimaryKeyColumn)}] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,");
        sql.AppendLine($"    [{Escape(pc.DeviceCodeColumn)}] NVARCHAR(128) NOT NULL,");
        sql.AppendLine($"    [{Escape(pc.HostNameColumn)}] NVARCHAR(128) NOT NULL,");
        sql.AppendLine($"    [{Escape(pc.DomainNameColumn)}] NVARCHAR(128) NOT NULL,");
        sql.AppendLine($"    [{Escape(pc.OperatingSystemColumn)}] NVARCHAR(128) NOT NULL,");
        sql.AppendLine($"    [{Escape(pc.AgentVersionColumn)}] NVARCHAR(32) NOT NULL,");
        sql.AppendLine($"    [{Escape(pc.LastHeartbeatUtcColumn)}] DATETIMEOFFSET NULL,");
        sql.AppendLine($"    [{Escape(pc.LastInventoryUtcColumn)}] DATETIMEOFFSET NULL,");
        sql.AppendLine($"    CONSTRAINT [UX_{Escape(pc.TableName)}_{Escape(pc.DeviceCodeColumn)}] UNIQUE ([{Escape(pc.DeviceCodeColumn)}])");
        sql.AppendLine(");");
        sql.AppendLine();
        sql.AppendLine($"CREATE TABLE [{schema}].[{Escape(installedSoftware.TableName)}] (");
        sql.AppendLine($"    [{Escape(installedSoftware.PrimaryKeyColumn)}] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,");
        sql.AppendLine($"    [{Escape(installedSoftware.PcForeignKeyColumn)}] BIGINT NOT NULL,");
        sql.AppendLine($"    [{Escape(installedSoftware.DisplayNameColumn)}] NVARCHAR(256) NOT NULL,");
        sql.AppendLine($"    [{Escape(installedSoftware.DisplayVersionColumn)}] NVARCHAR(64) NULL,");
        sql.AppendLine($"    [{Escape(installedSoftware.PublisherColumn)}] NVARCHAR(256) NULL,");
        sql.AppendLine($"    [{Escape(installedSoftware.InstallLocationColumn)}] NVARCHAR(512) NULL,");
        sql.AppendLine($"    [{Escape(installedSoftware.DiscoveryScopeColumn)}] NVARCHAR(32) NOT NULL,");
        sql.AppendLine($"    [{Escape(installedSoftware.DiscoverySourceColumn)}] NVARCHAR(64) NOT NULL,");
        sql.AppendLine($"    [{Escape(installedSoftware.CollectedAtUtcColumn)}] DATETIMEOFFSET NOT NULL,");
        sql.AppendLine($"    CONSTRAINT [FK_{Escape(installedSoftware.TableName)}_{Escape(pc.TableName)}] FOREIGN KEY ([{Escape(installedSoftware.PcForeignKeyColumn)}]) REFERENCES [{schema}].[{Escape(pc.TableName)}]([{Escape(pc.PrimaryKeyColumn)}])");
        sql.AppendLine(");");
        sql.AppendLine();
        sql.AppendLine($"CREATE TABLE [{schema}].[{Escape(policy.TableName)}] (");
        sql.AppendLine($"    [{Escape(policy.PrimaryKeyColumn)}] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,");
        sql.AppendLine($"    [{Escape(policy.ClassificationColumn)}] NVARCHAR(32) NOT NULL,");
        sql.AppendLine($"    [{Escape(policy.ProductNameColumn)}] NVARCHAR(256) NOT NULL,");
        sql.AppendLine($"    [{Escape(policy.PublisherColumn)}] NVARCHAR(256) NULL,");
        sql.AppendLine($"    [{Escape(policy.VersionPatternColumn)}] NVARCHAR(64) NULL,");
        sql.AppendLine($"    [{Escape(policy.NotesColumn)}] NVARCHAR(1024) NULL,");
        sql.AppendLine($"    [{Escape(policy.EnabledColumn)}] BIT NOT NULL CONSTRAINT [DF_{Escape(policy.TableName)}_{Escape(policy.EnabledColumn)}] DEFAULT(1),");
        sql.AppendLine($"    [{Escape(policy.UpdatedAtUtcColumn)}] DATETIMEOFFSET NOT NULL");
        sql.AppendLine(");");
        sql.AppendLine();
        sql.AppendLine($"CREATE INDEX [IX_{Escape(installedSoftware.TableName)}_{Escape(installedSoftware.PcForeignKeyColumn)}] ON [{schema}].[{Escape(installedSoftware.TableName)}]([{Escape(installedSoftware.PcForeignKeyColumn)}]);");
        sql.AppendLine($"CREATE INDEX [IX_{Escape(policy.TableName)}_{Escape(policy.ClassificationColumn)}] ON [{schema}].[{Escape(policy.TableName)}]([{Escape(policy.ClassificationColumn)}]);");
        return sql.ToString();
    }

    private static string Escape(string identifier) => identifier.Replace("]", "]]", StringComparison.Ordinal);

    private static string EscapeSqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
