using System.Data;
using Microsoft.Data.SqlClient;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

public sealed class SqlServerInventoryRepository(SqlServerStorageOptions options)
{
    public async Task SaveSnapshotAsync(InventoryIngestionRequest snapshot, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        if (await IsStaleSnapshotAsync(connection, transaction, snapshot, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }

        var pcId = await UpsertPcAsync(connection, transaction, snapshot.Pc, snapshot.CollectedAtUtc, null, cancellationToken);
        var software = options.InstalledSoftwareTable;
        await ExecuteAsync(connection, transaction,
            $"DELETE FROM {Name(options.SchemaName, software.TableName)} WHERE {Name(software.PcForeignKeyColumn)} = @pcId",
            [new("@pcId", pcId)], cancellationToken);

        foreach (var entry in snapshot.InstalledSoftware)
        {
            var sql = $"""
                INSERT INTO {Name(options.SchemaName, software.TableName)}
                ({Name(software.PcForeignKeyColumn)}, {Name(software.DisplayNameColumn)}, {Name(software.DisplayVersionColumn)},
                 {Name(software.PublisherColumn)}, {Name(software.InstallLocationColumn)}, {Name(software.DiscoveryScopeColumn)},
                 {Name(software.DiscoverySourceColumn)}, {Name(software.CollectedAtUtcColumn)})
                VALUES (@pcId, @name, @version, @publisher, @location, @scope, @source, @collectedAt)
                """;
            await ExecuteAsync(connection, transaction, sql,
            [
                new("@pcId", pcId), new("@name", Truncate(entry.Name, 256)), new("@version", DbValue(Truncate(entry.Version, 64))),
                new("@publisher", DbValue(Truncate(entry.Publisher, 256))), new("@location", DbValue(Truncate(entry.InstallLocation, 512))),
                new("@scope", Truncate(entry.DiscoveryScope, 256)), new("@source", Truncate(entry.DiscoverySource, 64)),
                new("@collectedAt", snapshot.CollectedAtUtc)
            ], cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveHeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var identity = new PcIdentity(heartbeat.DeviceCode, heartbeat.HostName, string.Empty, string.Empty, heartbeat.Version);
        await UpsertPcAsync(connection, transaction, identity, null, heartbeat.ReportedAtUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<bool> IsStaleSnapshotAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        InventoryIngestionRequest snapshot,
        CancellationToken cancellationToken)
    {
        var table = options.PcTable;
        var sql = $"""
            SELECT {Name(table.LastInventoryUtcColumn)}
            FROM {Name(options.SchemaName, table.TableName)} WITH (UPDLOCK, HOLDLOCK)
            WHERE {Name(table.DeviceCodeColumn)} = @deviceCode;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@deviceCode", snapshot.Pc.DeviceCode));
        var stored = await command.ExecuteScalarAsync(cancellationToken);
        return stored is DateTimeOffset lastInventoryUtc && lastInventoryUtc >= snapshot.CollectedAtUtc;
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
                {Name(table.AgentVersionColumn)} = @agentVersion,
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

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    private static string Name(params string[] parts) =>
        string.Join('.', parts.Select(part => $"[{part.Replace("]", "]]", StringComparison.Ordinal)}]"));
}
