using System.Data;
using Microsoft.Data.SqlClient;
using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

internal sealed partial class SqlServerDataContext
{
    public async Task<EnqueuedUserMessage?> EnqueueUserMessageAsync(
        string deviceCode,
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var pcId = await FindPcIdAsync(connection, transaction, deviceCode, cancellationToken);
        if (pcId is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var aliases = await ReadDeviceAliasesAsync(connection, transaction, pcId.Value, cancellationToken);
        var createdAtUtc = DateTimeOffset.UtcNow;
        await using var insert = new SqlCommand(BuildInsertUserMessageSql(), connection, transaction);
        insert.Parameters.AddRange(
        [
            new("@pcId", pcId.Value),
            new("@title", title),
            new("@body", body),
            new("@status", UserMessageGrant.Pending),
            new("@createdAt", createdAtUtc)
        ]);
        var id = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return new EnqueuedUserMessage(new AgentUserMessageCommand(id, title, body), aliases);
    }

    public async Task<IReadOnlyList<EnqueuedUserMessage>> BroadcastUserMessagesAsync(
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var createdAtUtc = DateTimeOffset.UtcNow;
        await using var insert = new SqlCommand(BuildBroadcastUserMessagesSql(), connection, transaction);
        insert.Parameters.AddRange(
        [
            new("@title", title),
            new("@body", body),
            new("@status", UserMessageGrant.Pending),
            new("@createdAt", createdAtUtc)
        ]);
        await using var reader = await insert.ExecuteReaderAsync(cancellationToken);
        var items = new List<EnqueuedUserMessage>();
        var table = options.UserMessageTable;
        var pc = options.PcTable;
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(reader.GetOrdinal(table.PrimaryKeyColumn));
            var aliases = ReadAliases(
                reader.GetString(reader.GetOrdinal(pc.DeviceCodeColumn)),
                ReadNullableString(reader, pc.AssignedDeviceCodeColumn),
                ReadNullableString(reader, pc.DeviceIdColumn));
            items.Add(new EnqueuedUserMessage(new AgentUserMessageCommand(id, title, body), aliases));
        }

        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return items;
    }

    public async Task<List<AgentUserMessageCommand>> ListPendingUserMessagesAsync(
        string deviceCode,
        int take,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(BuildListPendingUserMessagesSql(), connection);
        command.Parameters.AddRange(
        [
            new("@deviceCode", deviceCode),
            new("@status", UserMessageGrant.Pending),
            new("@take", take)
        ]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var table = options.UserMessageTable;
        var items = new List<AgentUserMessageCommand>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new AgentUserMessageCommand(
                reader.GetInt64(reader.GetOrdinal(table.PrimaryKeyColumn)),
                reader.GetString(reader.GetOrdinal(table.TitleColumn)),
                reader.GetString(reader.GetOrdinal(table.BodyColumn))));
        }

        return items;
    }

    public async Task<AgentUserMessageCommand?> GetOldestPendingUserMessageAsync(
        string deviceCode,
        CancellationToken cancellationToken)
    {
        var items = await ListPendingUserMessagesAsync(deviceCode, 1, cancellationToken);
        return items.Count == 0 ? null : items[0];
    }

    public async Task<bool> ConsumeUserMessageAsync(
        long id,
        string deviceCode,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(BuildConsumeUserMessageSql(), connection);
        command.Parameters.AddRange(
        [
            new("@id", id),
            new("@deviceCode", deviceCode),
            new("@pending", UserMessageGrant.Pending),
            new("@consumed", UserMessageGrant.Consumed),
            new("@consumedAt", DateTimeOffset.UtcNow)
        ]);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    internal string BuildInsertUserMessageSql()
    {
        var table = options.UserMessageTable;
        return $"""
            INSERT INTO {Name(options.SchemaName, table.TableName)}
            ({Name(table.PcForeignKeyColumn)}, {Name(table.TitleColumn)}, {Name(table.BodyColumn)},
             {Name(table.StatusColumn)}, {Name(table.CreatedAtUtcColumn)})
            OUTPUT INSERTED.{Name(table.PrimaryKeyColumn)}
            VALUES (@pcId, @title, @body, @status, @createdAt);
            """;
    }

    internal string BuildBroadcastUserMessagesSql()
    {
        var table = options.UserMessageTable;
        var pc = options.PcTable;
        return $"""
            MERGE {Name(options.SchemaName, table.TableName)} AS target
            USING {Name(options.SchemaName, pc.TableName)} AS p
            ON 1 = 0
            WHEN NOT MATCHED THEN
                INSERT ({Name(table.PcForeignKeyColumn)}, {Name(table.TitleColumn)}, {Name(table.BodyColumn)},
                        {Name(table.StatusColumn)}, {Name(table.CreatedAtUtcColumn)})
                VALUES (p.{Name(pc.PrimaryKeyColumn)}, @title, @body, @status, @createdAt)
            OUTPUT INSERTED.{Name(table.PrimaryKeyColumn)}, p.{Name(pc.DeviceCodeColumn)},
                   p.{Name(pc.AssignedDeviceCodeColumn)}, p.{Name(pc.DeviceIdColumn)};
            """;
    }

    internal string BuildListPendingUserMessagesSql()
    {
        var table = options.UserMessageTable;
        var pc = options.PcTable;
        return $"""
            SELECT TOP (@take) m.{Name(table.PrimaryKeyColumn)}, m.{Name(table.TitleColumn)}, m.{Name(table.BodyColumn)}
            FROM {Name(options.SchemaName, table.TableName)} AS m
            INNER JOIN {Name(options.SchemaName, pc.TableName)} AS p
                ON p.{Name(pc.PrimaryKeyColumn)} = m.{Name(table.PcForeignKeyColumn)}
            WHERE ({PcLookupPredicate("p")})
              AND m.{Name(table.StatusColumn)} = @status
            ORDER BY m.{Name(table.CreatedAtUtcColumn)}, m.{Name(table.PrimaryKeyColumn)};
            """;
    }

    internal string BuildConsumeUserMessageSql()
    {
        var table = options.UserMessageTable;
        var pc = options.PcTable;
        return $"""
            UPDATE m
            SET {Name(table.StatusColumn)} = @consumed,
                {Name(table.ConsumedAtUtcColumn)} = @consumedAt
            FROM {Name(options.SchemaName, table.TableName)} AS m
            INNER JOIN {Name(options.SchemaName, pc.TableName)} AS p
                ON p.{Name(pc.PrimaryKeyColumn)} = m.{Name(table.PcForeignKeyColumn)}
            WHERE m.{Name(table.PrimaryKeyColumn)} = @id
              AND ({PcLookupPredicate("p")})
              AND (
                    m.{Name(table.StatusColumn)} = @pending
                 OR m.{Name(table.StatusColumn)} = @consumed
              );
            """;
    }

    private async Task<IReadOnlyList<string>> ReadDeviceAliasesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long pcId,
        CancellationToken cancellationToken)
    {
        var pc = options.PcTable;
        var sql = $"""
            SELECT {Name(pc.DeviceCodeColumn)}, {Name(pc.AssignedDeviceCodeColumn)}, {Name(pc.DeviceIdColumn)}
            FROM {Name(options.SchemaName, pc.TableName)}
            WHERE {Name(pc.PrimaryKeyColumn)} = @pcId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@pcId", pcId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return [];
        }

        return ReadAliases(
            reader.GetString(reader.GetOrdinal(pc.DeviceCodeColumn)),
            ReadNullableString(reader, pc.AssignedDeviceCodeColumn),
            ReadNullableString(reader, pc.DeviceIdColumn));
    }

    private static IReadOnlyList<string> ReadAliases(string deviceCode, string? assignedDeviceCode, string? deviceId)
    {
        var aliases = new List<string>();
        AddAlias(aliases, deviceCode);
        AddAlias(aliases, assignedDeviceCode);
        AddAlias(aliases, deviceId);
        return aliases;
    }

    private static void AddAlias(List<string> aliases, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        if (!aliases.Exists(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            aliases.Add(trimmed);
        }
    }
}
