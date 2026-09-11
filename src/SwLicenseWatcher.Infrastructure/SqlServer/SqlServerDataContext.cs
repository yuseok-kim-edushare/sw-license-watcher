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
            WHERE {PcLookupPredicate()};
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
        var pcId = await FindPcIdForAgentAsync(connection, transaction, pc, cancellationToken);
        if (pcId is null)
        {
            var insert = $"""
                INSERT INTO {Name(options.SchemaName, table.TableName)}
                ({Name(table.DeviceCodeColumn)}, {Name(table.HostNameColumn)}, {Name(table.DomainNameColumn)},
                 {Name(table.OperatingSystemColumn)}, {Name(table.AgentVersionColumn)},
                 {Name(table.LastHeartbeatUtcColumn)}, {Name(table.LastInventoryUtcColumn)})
                VALUES (@deviceCode, @hostName, @domainName, @operatingSystem, @agentVersion, @heartbeatAt, @inventoryAt);
                SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
                """;
            await using var insertCommand = new SqlCommand(insert, connection, transaction);
            insertCommand.Parameters.AddRange(UpsertParameters(pc, heartbeatAt, inventoryAt));
            return Convert.ToInt64(await insertCommand.ExecuteScalarAsync(cancellationToken));
        }

        var update = $"""
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
            WHERE {Name(table.PrimaryKeyColumn)} = @pcId
             AND (@heartbeatAt IS NULL OR {Name(table.LastHeartbeatUtcColumn)} IS NULL OR @heartbeatAt >= {Name(table.LastHeartbeatUtcColumn)});
            """;
        await using var updateCommand = new SqlCommand(update, connection, transaction);
        updateCommand.Parameters.AddRange(UpsertParameters(pc, heartbeatAt, inventoryAt));
        updateCommand.Parameters.Add(new SqlParameter("@pcId", pcId.Value));
        await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        return pcId.Value;
    }

    private async Task<long?> FindPcIdForAgentAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        PcIdentity pc,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(pc.DeviceId))
        {
            var byId = await FindPcIdByColumnAsync(connection, transaction, options.PcTable.DeviceIdColumn, pc.DeviceId, cancellationToken);
            if (byId is not null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(pc.DevicePublicKey))
        {
            var byKey = await FindPcIdByColumnAsync(connection, transaction, options.PcTable.DevicePublicKeyColumn, pc.DevicePublicKey, cancellationToken);
            if (byKey is not null)
            {
                return byKey;
            }
        }

        return await FindPcIdAsync(connection, transaction, pc.DeviceCode, cancellationToken);
    }

    private async Task<long?> FindPcIdByColumnAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string column,
        string value,
        CancellationToken cancellationToken)
    {
        var table = options.PcTable;
        var sql = $"""
            SELECT {Name(table.PrimaryKeyColumn)}
            FROM {Name(options.SchemaName, table.TableName)}
            WHERE {Name(column)} = @value;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@value", value));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt64(result);
    }

    private SqlParameter[] UpsertParameters(PcIdentity pc, DateTimeOffset? heartbeatAt, DateTimeOffset? inventoryAt) =>
    [
        new("@deviceCode", pc.DeviceCode), new("@hostName", pc.HostName), new("@domainName", pc.DomainName),
        new("@operatingSystem", pc.OperatingSystem), new("@agentVersion", pc.AgentVersion),
        new("@heartbeatAt", DbValue(heartbeatAt)), new("@inventoryAt", DbValue(inventoryAt))
    ];

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

    private static string ReadOrigin(SqlDataReader reader, string column)
    {
        var stored = ReadNullableString(reader, column);
        return string.Equals(stored, UninstallGrant.OriginAdmin, StringComparison.OrdinalIgnoreCase)
            ? UninstallGrant.OriginAdmin
            : UninstallGrant.OriginAgent;
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

    internal string DisplayDeviceCodeSql(string? tableAlias = null)
    {
        var table = options.PcTable;
        var prefix = string.IsNullOrEmpty(tableAlias) ? "" : tableAlias + ".";
        return $"COALESCE(NULLIF({prefix}{Name(table.AssignedDeviceCodeColumn)}, N''), {prefix}{Name(table.DeviceCodeColumn)})";
    }

    internal string PcLookupPredicate(string? tableAlias = null)
    {
        var table = options.PcTable;
        var prefix = string.IsNullOrEmpty(tableAlias) ? "" : tableAlias + ".";
        return $"""
            {prefix}{Name(table.DeviceCodeColumn)} = @deviceCode
            OR {prefix}{Name(table.AssignedDeviceCodeColumn)} = @deviceCode
            OR {prefix}{Name(table.DeviceIdColumn)} = @deviceCode
            """;
    }

    internal string HostNameSearchSql(string? tableAlias = null)
    {
        var table = options.PcTable;
        var prefix = string.IsNullOrEmpty(tableAlias) ? "" : tableAlias + ".";
        return $"""
            {prefix}{Name(table.DeviceCodeColumn)} LIKE @search
            OR {prefix}{Name(table.HostNameColumn)} LIKE @search
            OR {prefix}{Name(table.AssignedHostNameColumn)} LIKE @search
            OR {prefix}{Name(table.AssignedDeviceCodeColumn)} LIKE @search
            OR {prefix}{Name(table.DeviceIdColumn)} LIKE @search
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
