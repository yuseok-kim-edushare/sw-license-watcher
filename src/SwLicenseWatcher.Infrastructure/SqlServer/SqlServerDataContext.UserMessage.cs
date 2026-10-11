using System.Data;
using System.Data.Common;
using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.Relational;

public sealed partial class RelationalDataContext
{
    public async Task<EnqueuedUserMessage?> EnqueueUserMessageAsync(
        string deviceCode,
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var pcId = await FindPcIdAsync(connection, transaction, deviceCode, cancellationToken);
        if (pcId is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var aliases = await ReadDeviceAliasesAsync(connection, transaction, pcId.Value, cancellationToken);
        var createdAtUtc = DateTimeOffset.UtcNow;
        var id = await ExecuteInsertReturningIdAsync(
            connection,
            transaction,
            BuildInsertUserMessageSql(),
            cancellationToken,
            ("@pcId", pcId.Value),
            ("@title", title),
            ("@body", body),
            ("@status", UserMessageGrant.Pending),
            ("@createdAt", createdAtUtc));
        await transaction.CommitAsync(cancellationToken);
        return new EnqueuedUserMessage(new AgentUserMessageCommand(id, title, body), aliases);
    }

    public async Task<IReadOnlyList<EnqueuedUserMessage>> BroadcastUserMessagesAsync(
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var createdAtUtc = DateTimeOffset.UtcNow;
        var items = dialect.Provider == StorageProvider.MySql
            ? await BroadcastUserMessagesPerPcAsync(connection, transaction, title, body, createdAtUtc, cancellationToken)
            : await ReadBroadcastUserMessagesAsync(connection, transaction, title, body, createdAtUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return items;
    }

    private async Task<List<EnqueuedUserMessage>> ReadBroadcastUserMessagesAsync(
        DbConnection connection,
        DbTransaction transaction,
        string title,
        string body,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        await using var insert = CreateCommand(connection, transaction, BuildBroadcastUserMessagesSql());
        Bind(insert,
            ("@title", title),
            ("@body", body),
            ("@status", UserMessageGrant.Pending),
            ("@createdAt", createdAtUtc));
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

        return items;
    }

    private async Task<List<EnqueuedUserMessage>> BroadcastUserMessagesPerPcAsync(
        DbConnection connection,
        DbTransaction transaction,
        string title,
        string body,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        var pc = options.PcTable;
        var sql = $"""
            SELECT {Name(pc.PrimaryKeyColumn)}, {Name(pc.DeviceCodeColumn)}, {Name(pc.AssignedDeviceCodeColumn)}, {Name(pc.DeviceIdColumn)}
            FROM {Name(options.SchemaName, pc.TableName)};
            """;
        var targets = new List<(long PcId, string DeviceCode, string? AssignedDeviceCode, string? DeviceId)>();
        await using (var command = CreateCommand(connection, transaction, sql))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                targets.Add((
                    reader.GetInt64(reader.GetOrdinal(pc.PrimaryKeyColumn)),
                    reader.GetString(reader.GetOrdinal(pc.DeviceCodeColumn)),
                    ReadNullableString(reader, pc.AssignedDeviceCodeColumn),
                    ReadNullableString(reader, pc.DeviceIdColumn)));
            }
        }

        var items = new List<EnqueuedUserMessage>(targets.Count);
        foreach (var target in targets)
        {
            var id = await ExecuteInsertReturningIdAsync(
                connection,
                transaction,
                BuildInsertUserMessageSql(),
                cancellationToken,
                ("@pcId", target.PcId),
                ("@title", title),
                ("@body", body),
                ("@status", UserMessageGrant.Pending),
                ("@createdAt", createdAtUtc));
            items.Add(new EnqueuedUserMessage(
                new AgentUserMessageCommand(id, title, body),
                ReadAliases(target.DeviceCode, target.AssignedDeviceCode, target.DeviceId)));
        }

        return items;
    }

    public async Task<List<AgentUserMessageCommand>> ListPendingUserMessagesAsync(
        string deviceCode,
        int take,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, null, BuildListPendingUserMessagesSql());
        Bind(command,
            ("@deviceCode", deviceCode),
            ("@status", UserMessageGrant.Pending),
            ("@take", take)
        );
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
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, null, BuildConsumeUserMessageSql());
        Bind(command,
            ("@id", id),
            ("@deviceCode", deviceCode),
            ("@pending", UserMessageGrant.Pending),
            ("@consumed", UserMessageGrant.Consumed),
            ("@consumedAt", DateTimeOffset.UtcNow)
        );
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    internal string BuildInsertUserMessageSql()
    {
        var table = options.UserMessageTable;
        return InsertReturningId(
            Name(options.SchemaName, table.TableName),
            $"{Name(table.PcForeignKeyColumn)}, {Name(table.TitleColumn)}, {Name(table.BodyColumn)}, {Name(table.StatusColumn)}, {Name(table.CreatedAtUtcColumn)}",
            "@pcId, @title, @body, @status, @createdAt",
            table.PrimaryKeyColumn);
    }

    internal string BuildBroadcastUserMessagesSql()
    {
        var table = options.UserMessageTable;
        var pc = options.PcTable;
        if (dialect.Provider == StorageProvider.PostgreSql)
        {
            return $"""
                WITH inserted AS (
                    INSERT INTO {Name(options.SchemaName, table.TableName)}
                        ({Name(table.PcForeignKeyColumn)}, {Name(table.TitleColumn)}, {Name(table.BodyColumn)},
                         {Name(table.StatusColumn)}, {Name(table.CreatedAtUtcColumn)})
                    SELECT p.{Name(pc.PrimaryKeyColumn)}, @title, @body, @status, @createdAt
                    FROM {Name(options.SchemaName, pc.TableName)} AS p
                    RETURNING {Name(table.PrimaryKeyColumn)}, {Name(table.PcForeignKeyColumn)}
                )
                SELECT inserted.{Name(table.PrimaryKeyColumn)}, p.{Name(pc.DeviceCodeColumn)},
                       p.{Name(pc.AssignedDeviceCodeColumn)}, p.{Name(pc.DeviceIdColumn)}
                FROM inserted
                INNER JOIN {Name(options.SchemaName, pc.TableName)} AS p
                    ON p.{Name(pc.PrimaryKeyColumn)} = inserted.{Name(table.PcForeignKeyColumn)};
                """;
        }

        if (dialect.Provider == StorageProvider.MySql)
        {
            return BuildInsertUserMessageSql();
        }

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
            SELECT {TakePrefix("@take")} m.{Name(table.PrimaryKeyColumn)}, m.{Name(table.TitleColumn)}, m.{Name(table.BodyColumn)}
            FROM {Name(options.SchemaName, table.TableName)} AS m
            INNER JOIN {Name(options.SchemaName, pc.TableName)} AS p
                ON p.{Name(pc.PrimaryKeyColumn)} = m.{Name(table.PcForeignKeyColumn)}
            WHERE ({PcLookupPredicate("p")})
              AND m.{Name(table.StatusColumn)} = @status
            ORDER BY m.{Name(table.CreatedAtUtcColumn)}, m.{Name(table.PrimaryKeyColumn)}{TakeSuffix("@take")};
            """;
    }

    internal string BuildConsumeUserMessageSql()
    {
        var table = options.UserMessageTable;
        var pc = options.PcTable;
        var assignments = $"""
            {Name(table.StatusColumn)} = @consumed,
                {Name(table.ConsumedAtUtcColumn)} = @consumedAt
            """;
        var filter = $"""
            m.{Name(table.PrimaryKeyColumn)} = @id
              AND ({PcLookupPredicate("p")})
              AND (
                    m.{Name(table.StatusColumn)} = @pending
                 OR m.{Name(table.StatusColumn)} = @consumed
              )
            """;
        if (dialect.Provider == StorageProvider.PostgreSql)
        {
            return $"""
                UPDATE {Name(options.SchemaName, table.TableName)} AS m
                SET {assignments}
                FROM {Name(options.SchemaName, pc.TableName)} AS p
                WHERE p.{Name(pc.PrimaryKeyColumn)} = m.{Name(table.PcForeignKeyColumn)}
                  AND {filter};
                """;
        }

        if (dialect.Provider == StorageProvider.MySql)
        {
            return $"""
                UPDATE {Name(options.SchemaName, table.TableName)} AS m
                INNER JOIN {Name(options.SchemaName, pc.TableName)} AS p
                    ON p.{Name(pc.PrimaryKeyColumn)} = m.{Name(table.PcForeignKeyColumn)}
                SET {assignments}
                WHERE {filter};
                """;
        }

        return $"""
            UPDATE m
            SET {assignments}
            FROM {Name(options.SchemaName, table.TableName)} AS m
            INNER JOIN {Name(options.SchemaName, pc.TableName)} AS p
                ON p.{Name(pc.PrimaryKeyColumn)} = m.{Name(table.PcForeignKeyColumn)}
            WHERE {filter};
            """;
    }

    private async Task<IReadOnlyList<string>> ReadDeviceAliasesAsync(
        DbConnection connection,
        DbTransaction transaction,
        long pcId,
        CancellationToken cancellationToken)
    {
        var pc = options.PcTable;
        var sql = $"""
            SELECT {Name(pc.DeviceCodeColumn)}, {Name(pc.AssignedDeviceCodeColumn)}, {Name(pc.DeviceIdColumn)}
            FROM {Name(options.SchemaName, pc.TableName)}
            WHERE {Name(pc.PrimaryKeyColumn)} = @pcId;
            """;
        await using var command = CreateCommand(connection, transaction, sql);
        Bind(command, ("@pcId", pcId));
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
