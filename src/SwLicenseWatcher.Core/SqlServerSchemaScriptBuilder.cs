using System.Security.Cryptography;
using System.Text;

namespace SwLicenseWatcher.Core;

public sealed class SqlServerSchemaScriptBuilder
{
    public string Build(SqlServerStorageOptions options)
    {
        var schema = Escape(options.SchemaName);
        var schemaLiteral = EscapeSqlLiteral(options.SchemaName);
        var schemaCommandIdentifier = EscapeSqlLiteral(schema);
        var pc = options.PcTable;
        var installedSoftware = options.InstalledSoftwareTable;
        var policy = options.SoftwarePolicyTable;

        var sql = new StringBuilder();
        sql.AppendLine($"IF SCHEMA_ID(N'{schemaLiteral}') IS NULL EXEC(N'CREATE SCHEMA [{schemaCommandIdentifier}]');");
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
        sql.AppendLine($"    CONSTRAINT [{Escape(BuildIdentifier("UX", pc.TableName, pc.DeviceCodeColumn))}] UNIQUE ([{Escape(pc.DeviceCodeColumn)}])");
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
        sql.AppendLine($"    CONSTRAINT [{Escape(BuildIdentifier("FK", installedSoftware.TableName, pc.TableName))}] FOREIGN KEY ([{Escape(installedSoftware.PcForeignKeyColumn)}]) REFERENCES [{schema}].[{Escape(pc.TableName)}]([{Escape(pc.PrimaryKeyColumn)}])");
        sql.AppendLine(");");
        sql.AppendLine();
        sql.AppendLine($"CREATE TABLE [{schema}].[{Escape(policy.TableName)}] (");
        sql.AppendLine($"    [{Escape(policy.PrimaryKeyColumn)}] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,");
        sql.AppendLine($"    [{Escape(policy.ClassificationColumn)}] NVARCHAR(32) NOT NULL,");
        sql.AppendLine($"    [{Escape(policy.ProductNameColumn)}] NVARCHAR(256) NOT NULL,");
        sql.AppendLine($"    [{Escape(policy.PublisherColumn)}] NVARCHAR(256) NULL,");
        sql.AppendLine($"    [{Escape(policy.VersionPatternColumn)}] NVARCHAR(64) NULL,");
        sql.AppendLine($"    [{Escape(policy.NotesColumn)}] NVARCHAR(1024) NULL,");
        sql.AppendLine($"    [{Escape(policy.EnabledColumn)}] BIT NOT NULL CONSTRAINT [{Escape(BuildIdentifier("DF", policy.TableName, policy.EnabledColumn))}] DEFAULT(1),");
        sql.AppendLine($"    [{Escape(policy.UpdatedAtUtcColumn)}] DATETIMEOFFSET NOT NULL");
        sql.AppendLine(");");
        sql.AppendLine();
        sql.AppendLine($"CREATE INDEX [{Escape(BuildIdentifier("IX", installedSoftware.TableName, installedSoftware.PcForeignKeyColumn))}] ON [{schema}].[{Escape(installedSoftware.TableName)}]([{Escape(installedSoftware.PcForeignKeyColumn)}]);");
        sql.AppendLine($"CREATE INDEX [{Escape(BuildIdentifier("IX", policy.TableName, policy.ClassificationColumn))}] ON [{schema}].[{Escape(policy.TableName)}]([{Escape(policy.ClassificationColumn)}]);");
        return sql.ToString();
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

    private static string Escape(string identifier) => identifier.Replace("]", "]]", StringComparison.Ordinal);

    private static string EscapeSqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
