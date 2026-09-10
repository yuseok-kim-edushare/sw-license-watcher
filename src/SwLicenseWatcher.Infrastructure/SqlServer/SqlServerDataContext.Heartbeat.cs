using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.SqlClient;
using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

internal sealed partial class SqlServerDataContext
{
    public async Task SaveHeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var identity = new PcIdentity(heartbeat.DeviceCode, heartbeat.HostName, string.Empty, string.Empty, heartbeat.Version);
        var pcId = await UpsertPcAsync(connection, transaction, identity, null, heartbeat.ReportedAtUtc, cancellationToken);
        await ExecuteAsync(
            connection,
            transaction,
            BuildClearStaleHeartbeatNotificationIfHeartbeatAppliedSql(),
            [new("@pcId", pcId), new("@heartbeatAt", heartbeat.ReportedAtUtc)],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<List<StalePcHeartbeat>> GetStaleHeartbeatsAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return await ReadStaleHeartbeatsAsync(connection, transaction: null, cutoff, cancellationToken);
    }

    public async Task<List<StalePcHeartbeat>> ClaimNewlyStaleHeartbeatsAsync(
        DateTimeOffset cutoff,
        DateTimeOffset notifiedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        await ExecuteAsync(
            connection,
            transaction,
            BuildClearRecoveredStaleHeartbeatNotificationsSql(),
            [new("@cutoff", cutoff)],
            cancellationToken);

        var stalePcs = await ReadStaleHeartbeatsAsync(connection, transaction, cutoff, cancellationToken);
        var notifiedCodes = await ReadNotifiedStaleHeartbeatDeviceCodesAsync(connection, transaction, cancellationToken);
        var notified = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (var deviceCode in notifiedCodes)
        {
            notified.TryAdd(deviceCode, 0);
        }

        var newlyStale = InventoryDecisions.TakeNewlyStale(stalePcs, notified);
        foreach (var pc in newlyStale)
        {
            await ExecuteAsync(
                connection,
                transaction,
                BuildInsertStaleHeartbeatNotificationSql(),
                [new("@deviceCode", pc.DeviceCode), new("@notifiedAt", notifiedAtUtc)],
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return newlyStale;
    }

    internal string BuildGetStaleHeartbeatsSql()
    {
        var table = options.PcTable;
        return $"""
            SELECT {Name(table.DeviceCodeColumn)}, {Name(table.HostNameColumn)}, {Name(table.LastHeartbeatUtcColumn)}
            FROM {Name(options.SchemaName, table.TableName)}
            WHERE {Name(table.LastHeartbeatUtcColumn)} IS NOT NULL
              AND {Name(table.LastHeartbeatUtcColumn)} < @cutoff
            ORDER BY {Name(table.LastHeartbeatUtcColumn)}, {Name(table.DeviceCodeColumn)};
            """;
    }

    internal string BuildGetNotifiedStaleHeartbeatDeviceCodesSql()
    {
        var pc = options.PcTable;
        var notification = options.StaleHeartbeatNotificationTable;
        return $"""
            SELECT p.{Name(pc.DeviceCodeColumn)}
            FROM {Name(options.SchemaName, notification.TableName)} AS n
            INNER JOIN {Name(options.SchemaName, pc.TableName)} AS p
                ON p.{Name(pc.PrimaryKeyColumn)} = n.{Name(notification.PcForeignKeyColumn)};
            """;
    }

    internal string BuildClearRecoveredStaleHeartbeatNotificationsSql()
    {
        var pc = options.PcTable;
        var notification = options.StaleHeartbeatNotificationTable;
        return $"""
            DELETE n
            FROM {Name(options.SchemaName, notification.TableName)} AS n
            INNER JOIN {Name(options.SchemaName, pc.TableName)} AS p
                ON p.{Name(pc.PrimaryKeyColumn)} = n.{Name(notification.PcForeignKeyColumn)}
            WHERE p.{Name(pc.LastHeartbeatUtcColumn)} IS NULL
               OR p.{Name(pc.LastHeartbeatUtcColumn)} >= @cutoff;
            """;
    }

    internal string BuildInsertStaleHeartbeatNotificationSql()
    {
        var pc = options.PcTable;
        var notification = options.StaleHeartbeatNotificationTable;
        return $"""
            INSERT INTO {Name(options.SchemaName, notification.TableName)}
            ({Name(notification.PcForeignKeyColumn)}, {Name(notification.NotifiedAtUtcColumn)})
            SELECT p.{Name(pc.PrimaryKeyColumn)}, @notifiedAt
            FROM {Name(options.SchemaName, pc.TableName)} AS p
            WHERE p.{Name(pc.DeviceCodeColumn)} = @deviceCode
              AND NOT EXISTS (
                  SELECT 1
                  FROM {Name(options.SchemaName, notification.TableName)} AS n
                  WHERE n.{Name(notification.PcForeignKeyColumn)} = p.{Name(pc.PrimaryKeyColumn)});
            """;
    }

    internal string BuildClearStaleHeartbeatNotificationIfHeartbeatAppliedSql()
    {
        var pc = options.PcTable;
        var notification = options.StaleHeartbeatNotificationTable;
        return $"""
            DELETE n
            FROM {Name(options.SchemaName, notification.TableName)} AS n
            WHERE n.{Name(notification.PcForeignKeyColumn)} = @pcId
              AND EXISTS (
                  SELECT 1
                  FROM {Name(options.SchemaName, pc.TableName)} AS p
                  WHERE p.{Name(pc.PrimaryKeyColumn)} = @pcId
                    AND p.{Name(pc.LastHeartbeatUtcColumn)} = @heartbeatAt);
            """;
    }

    private async Task<List<StalePcHeartbeat>> ReadStaleHeartbeatsAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        var table = options.PcTable;
        var sql = BuildGetStaleHeartbeatsSql();
        await using var command = CreateCommand(connection, transaction, sql);
        command.Parameters.Add(new SqlParameter("@cutoff", cutoff));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var stale = new List<StalePcHeartbeat>();
        while (await reader.ReadAsync(cancellationToken))
        {
            stale.Add(new StalePcHeartbeat(
                reader.GetString(reader.GetOrdinal(table.DeviceCodeColumn)),
                reader.GetString(reader.GetOrdinal(table.HostNameColumn)),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal(table.LastHeartbeatUtcColumn))));
        }

        return stale;
    }

    private async Task<List<string>> ReadNotifiedStaleHeartbeatDeviceCodesAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var pc = options.PcTable;
        var sql = BuildGetNotifiedStaleHeartbeatDeviceCodesSql();
        await using var command = CreateCommand(connection, transaction, sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var deviceCodes = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            deviceCodes.Add(reader.GetString(reader.GetOrdinal(pc.DeviceCodeColumn)));
        }

        return deviceCodes;
    }

}
