using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.SqlClient;
using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

internal sealed partial class SqlServerDataContext(SqlServerStorageOptions options)
{
    private async Task<long?> FindPcIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string deviceCode,
        CancellationToken cancellationToken)
    {
        var table = options.PcTable;
        var sql = $"""
            SELECT {Name(table.PrimaryKeyColumn)}
            FROM {Name(options.SchemaName, table.TableName)}
            WHERE {Name(table.DeviceCodeColumn)} = @deviceCode;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@deviceCode", deviceCode));
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    private static SqlCommand CreateCommand(SqlConnection connection, SqlTransaction? transaction, string sql) =>
        transaction is null
            ? new SqlCommand(sql, connection)
            : new SqlCommand(sql, connection, transaction);

    private async Task<List<InstalledSoftwareEntry>> ReadInstalledSoftwareAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        long pcId,
        string? classification,
        CancellationToken cancellationToken)
    {
        var software = options.InstalledSoftwareTable;
        var sql = $"""
            SELECT {Name(software.DisplayNameColumn)}, {Name(software.DisplayVersionColumn)}, {Name(software.PublisherColumn)},
                   {Name(software.InstallLocationColumn)}, {Name(software.DiscoveryScopeColumn)}, {Name(software.DiscoverySourceColumn)},
                   {Name(software.ClassificationColumn)}
            FROM {Name(options.SchemaName, software.TableName)}
            WHERE {Name(software.PcForeignKeyColumn)} = @pcId
              AND (@classification IS NULL OR {Name(software.ClassificationColumn)} = @classification)
            ORDER BY {Name(software.DisplayNameColumn)}, {Name(software.PrimaryKeyColumn)};
            """;
        await using var command = transaction is null
            ? new SqlCommand(sql, connection)
            : new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@pcId", pcId));
        command.Parameters.Add(new SqlParameter("@classification", DbValue(classification)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<InstalledSoftwareEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new InstalledSoftwareEntry(
                reader.GetString(reader.GetOrdinal(software.DisplayNameColumn)),
                ReadNullableString(reader, software.DisplayVersionColumn),
                ReadNullableString(reader, software.PublisherColumn),
                ReadNullableString(reader, software.InstallLocationColumn),
                reader.GetString(reader.GetOrdinal(software.DiscoveryScopeColumn)),
                reader.GetString(reader.GetOrdinal(software.DiscoverySourceColumn)),
                ReadClassification(reader, software.ClassificationColumn)));
        }

        return entries;
    }

    private async Task<long> UpsertPcAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        PcIdentity pc,
        DateTimeOffset? inventoryAt,
        DateTimeOffset? heartbeatAt,
        CancellationToken cancellationToken)
    {
        var table = options.PcTable;
        var sql = $"""
            UPDATE {Name(options.SchemaName, table.TableName)}
            SET {Name(table.HostNameColumn)} = @hostName,
                {Name(table.DomainNameColumn)} = CASE WHEN @domainName = N'' THEN {Name(table.DomainNameColumn)} ELSE @domainName END,
                {Name(table.OperatingSystemColumn)} = CASE WHEN @operatingSystem = N'' THEN {Name(table.OperatingSystemColumn)} ELSE @operatingSystem END,
                {Name(table.AgentVersionColumn)} = CASE
                    WHEN @inventoryAt IS NOT NULL
                     AND {Name(table.LastHeartbeatUtcColumn)} > @inventoryAt
                    THEN {Name(table.AgentVersionColumn)}
                    ELSE @agentVersion
                END,
                {Name(table.LastHeartbeatUtcColumn)} = COALESCE(@heartbeatAt, {Name(table.LastHeartbeatUtcColumn)}),
                {Name(table.LastInventoryUtcColumn)} = COALESCE(@inventoryAt, {Name(table.LastInventoryUtcColumn)})
            WHERE {Name(table.DeviceCodeColumn)} = @deviceCode
             AND (@heartbeatAt IS NULL OR {Name(table.LastHeartbeatUtcColumn)} IS NULL OR @heartbeatAt >= {Name(table.LastHeartbeatUtcColumn)});
            IF @@ROWCOUNT = 0 AND NOT EXISTS (
               SELECT 1 FROM {Name(options.SchemaName, table.TableName)}
               WHERE {Name(table.DeviceCodeColumn)} = @deviceCode
            )
            BEGIN
                INSERT INTO {Name(options.SchemaName, table.TableName)}
                ({Name(table.DeviceCodeColumn)}, {Name(table.HostNameColumn)}, {Name(table.DomainNameColumn)},
                 {Name(table.OperatingSystemColumn)}, {Name(table.AgentVersionColumn)},
                 {Name(table.LastHeartbeatUtcColumn)}, {Name(table.LastInventoryUtcColumn)})
                VALUES (@deviceCode, @hostName, @domainName, @operatingSystem, @agentVersion, @heartbeatAt, @inventoryAt);
            END;
            SELECT {Name(table.PrimaryKeyColumn)} FROM {Name(options.SchemaName, table.TableName)}
            WHERE {Name(table.DeviceCodeColumn)} = @deviceCode;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(
        [
            new("@deviceCode", pc.DeviceCode), new("@hostName", pc.HostName), new("@domainName", pc.DomainName),
            new("@operatingSystem", pc.OperatingSystem), new("@agentVersion", pc.AgentVersion),
            new("@heartbeatAt", DbValue(heartbeatAt)), new("@inventoryAt", DbValue(inventoryAt))
        ]);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task ExecuteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        SqlParameter[] parameters,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static object DbValue(object? value) => value ?? DBNull.Value;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    internal static string? ToContainsPattern(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var escaped = search.Trim()
            .Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    private static string ReadClassification(SqlDataReader reader, string column)
    {
        var stored = ReadNullableString(reader, column);
        return SoftwarePolicyClassificationNames.TryParseInstalledSoftware(stored, out var classification)
            ? classification
            : SoftwarePolicyClassificationNames.Unclassified;
    }

    private static string? ReadNullableString(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    internal static string Name(params string[] parts) =>
        string.Join('.', parts.Select(part => $"[{part.Replace("]", "]]", StringComparison.Ordinal)}]"));

    internal string DisplayHostNameSql(string? tableAlias = null)
    {
        var table = options.PcTable;
        var prefix = string.IsNullOrEmpty(tableAlias) ? "" : tableAlias + ".";
        return $"COALESCE(NULLIF({prefix}{Name(table.AssignedHostNameColumn)}, N''), {prefix}{Name(table.HostNameColumn)})";
    }

    internal string HostNameSearchSql(string? tableAlias = null)
    {
        var table = options.PcTable;
        var prefix = string.IsNullOrEmpty(tableAlias) ? "" : tableAlias + ".";
        return $"""
            {prefix}{Name(table.DeviceCodeColumn)} LIKE @search
            OR {prefix}{Name(table.HostNameColumn)} LIKE @search
            OR {prefix}{Name(table.AssignedHostNameColumn)} LIKE @search
            """;
    }
}

internal sealed class SoftwareAggregateKeyComparer : IEqualityComparer<(string Name, string? Version)>
{
    public static SoftwareAggregateKeyComparer Instance { get; } = new();

    public bool Equals((string Name, string? Version) left, (string Name, string? Version) right) =>
        string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Version, right.Version, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode((string Name, string? Version) value) =>
        HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Name),
            value.Version is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(value.Version));
}
