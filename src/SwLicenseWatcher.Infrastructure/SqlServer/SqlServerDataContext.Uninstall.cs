using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.Relational;

public sealed partial class RelationalDataContext
{
    public async Task<UninstallRequestCreatedResponse?> CreateUninstallRequestAsync(
        string deviceCode,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var pcId = await FindPcIdAsync(connection, transaction, deviceCode, cancellationToken);
        if (pcId is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await ExecuteAsync(
            connection,
            transaction,
            BuildDeletePendingUninstallRequestsSql(),
            [("@pcId", pcId.Value)],
            cancellationToken);

        var requestedAtUtc = DateTimeOffset.UtcNow;
        var id = await ExecuteInsertReturningIdAsync(
            connection,
            transaction,
            BuildInsertUninstallRequestSql(),
            cancellationToken,
            ("@pcId", pcId.Value),
            ("@status", UninstallGrant.Pending),
            ("@requestedAt", requestedAtUtc),
            ("@origin", UninstallGrant.OriginAgent));
        await transaction.CommitAsync(cancellationToken);
        return new UninstallRequestCreatedResponse(
            id, deviceCode, UninstallGrant.Pending, requestedAtUtc, UninstallGrant.OriginAgent);
    }

    public async Task<AgentUninstallRequestResponse?> GetAgentUninstallRequestAsync(
        long id,
        string deviceCode,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, null, BuildGetAgentUninstallRequestSql());
        Bind(command,("@id", id), ("@deviceCode", deviceCode));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var uninstall = options.UninstallRequestTable;
        var storedStatus = reader.GetString(reader.GetOrdinal(uninstall.StatusColumn));
        var expiresAtUtc = ReadNullableDateTimeOffset(reader, uninstall.ExpiresAtUtcColumn);
        var status = UninstallGrant.ResolveStatus(storedStatus, expiresAtUtc, DateTimeOffset.UtcNow);
        var code = status == UninstallGrant.Approved
            ? ReadNullableString(reader, uninstall.CodeColumn)
            : null;
        return new AgentUninstallRequestResponse(
            reader.GetInt64(reader.GetOrdinal(uninstall.PrimaryKeyColumn)),
            reader.GetString(reader.GetOrdinal(options.PcTable.DeviceCodeColumn)),
            status,
            ReadDateTimeOffset(reader, reader.GetOrdinal(uninstall.RequestedAtUtcColumn)),
            ReadNullableDateTimeOffset(reader, uninstall.ApprovedAtUtcColumn),
            expiresAtUtc,
            code);
    }

    public async Task<bool> ConsumeUninstallRequestAsync(
        long id,
        string deviceCode,
        string code,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using var lookup = CreateCommand(connection, transaction, BuildGetAgentUninstallRequestSql());
        Bind(lookup,("@id", id), ("@deviceCode", deviceCode));
        await using var reader = await lookup.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await reader.DisposeAsync();
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var uninstall = options.UninstallRequestTable;
        var storedStatus = reader.GetString(reader.GetOrdinal(uninstall.StatusColumn));
        var expiresAtUtc = ReadNullableDateTimeOffset(reader, uninstall.ExpiresAtUtcColumn);
        var hash = ReadNullableString(reader, uninstall.CodeHashColumn);
        await reader.DisposeAsync();

        var status = UninstallGrant.ResolveStatus(storedStatus, expiresAtUtc, DateTimeOffset.UtcNow);
        if (status != UninstallGrant.Approved || !UninstallGrant.CodeMatchesHash(code, hash))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await ExecuteAsync(
            connection,
            transaction,
            BuildConsumeUninstallRequestSql(),
            [("@id", id), ("@consumedAt", DateTimeOffset.UtcNow), ("@status", UninstallGrant.Consumed)],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<(int TotalCount, List<AdminUninstallRequest> Items)> ListUninstallRequestsAsync(
        int skip,
        int take,
        string? search,
        CancellationToken cancellationToken,
        string? sort = null,
        bool sortDescending = false)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, null, BuildListUninstallRequestsSql(sort, sortDescending));
        Bind(command,
            ("@search", DbValue(ToContainsPattern(search))),
            ("@skip", skip),
            ("@take", take)
        );
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<AdminUninstallRequest>();
        var totalCount = 0;
        var uninstall = options.UninstallRequestTable;
        var pc = options.PcTable;
        var utcNow = DateTimeOffset.UtcNow;
        while (await reader.ReadAsync(cancellationToken))
        {
            totalCount = ReadInt32(reader, "total_count");
            var expiresAtUtc = ReadNullableDateTimeOffset(reader, uninstall.ExpiresAtUtcColumn);
            items.Add(new AdminUninstallRequest(
                reader.GetInt64(reader.GetOrdinal(uninstall.PrimaryKeyColumn)),
                reader.GetString(reader.GetOrdinal(pc.DeviceCodeColumn)),
                reader.GetString(reader.GetOrdinal(pc.HostNameColumn)),
                UninstallGrant.ResolveStatus(
                    reader.GetString(reader.GetOrdinal(uninstall.StatusColumn)),
                    expiresAtUtc,
                    utcNow),
                ReadDateTimeOffset(reader, reader.GetOrdinal(uninstall.RequestedAtUtcColumn)),
                ReadNullableDateTimeOffset(reader, uninstall.ApprovedAtUtcColumn),
                ReadNullableDateTimeOffset(reader, uninstall.ConsumedAtUtcColumn),
                expiresAtUtc,
                ReadOrigin(reader, uninstall.OriginColumn)));
        }

        return (totalCount, items);
    }

    public async Task<bool> ApproveUninstallRequestAsync(long id, CancellationToken cancellationToken)
    {
        var code = UninstallGrant.CreateCode();
        var approvedAtUtc = DateTimeOffset.UtcNow;
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, null, BuildApproveUninstallRequestSql());
        Bind(command,
            ("@id", id),
            ("@pending", UninstallGrant.Pending),
            ("@status", UninstallGrant.Approved),
            ("@code", code),
            ("@codeHash", UninstallGrant.HashCode(code)),
            ("@approvedAt", approvedAtUtc),
            ("@expiresAt", approvedAtUtc + UninstallGrant.ApprovalLifetime)
        );
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> DenyUninstallRequestAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, null, BuildDenyUninstallRequestSql());
        Bind(command,
            ("@id", id),
            ("@pending", UninstallGrant.Pending),
            ("@status", UninstallGrant.Denied)
        );
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<UninstallRequestCreatedResponse?> CreateDirectedUninstallRequestAsync(
        string deviceCode,
        CancellationToken cancellationToken)
    {
        var code = UninstallGrant.CreateCode();
        var requestedAtUtc = DateTimeOffset.UtcNow;
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var pcId = await FindPcIdAsync(connection, transaction, deviceCode, cancellationToken);
        if (pcId is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await ExecuteAsync(
            connection,
            transaction,
            BuildDeleteOpenUninstallRequestsSql(),
            [("@pcId", pcId.Value)],
            cancellationToken);

        var id = await ExecuteInsertReturningIdAsync(
            connection,
            transaction,
            BuildInsertDirectedUninstallRequestSql(),
            cancellationToken,
            ("@pcId", pcId.Value),
            ("@status", UninstallGrant.Approved),
            ("@requestedAt", requestedAtUtc),
            ("@approvedAt", requestedAtUtc),
            ("@expiresAt", requestedAtUtc + UninstallGrant.DirectedLifetime),
            ("@code", code),
            ("@codeHash", UninstallGrant.HashCode(code)),
            ("@origin", UninstallGrant.OriginAdmin));
        await transaction.CommitAsync(cancellationToken);
        return new UninstallRequestCreatedResponse(
            id, deviceCode, UninstallGrant.Approved, requestedAtUtc, UninstallGrant.OriginAdmin);
    }

    public async Task<AgentUninstallCommand?> GetDirectedUninstallCommandAsync(
        string deviceCode,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, null, BuildGetDirectedUninstallCommandSql());
        Bind(command,
            ("@deviceCode", deviceCode),
            ("@status", UninstallGrant.Approved),
            ("@origin", UninstallGrant.OriginAdmin)
        );
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var uninstall = options.UninstallRequestTable;
        var expiresAtUtc = ReadNullableDateTimeOffset(reader, uninstall.ExpiresAtUtcColumn);
        var status = UninstallGrant.ResolveStatus(
            reader.GetString(reader.GetOrdinal(uninstall.StatusColumn)),
            expiresAtUtc,
            DateTimeOffset.UtcNow);
        var code = ReadNullableString(reader, uninstall.CodeColumn);
        if (status != UninstallGrant.Approved || string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return new AgentUninstallCommand(
            reader.GetInt64(reader.GetOrdinal(uninstall.PrimaryKeyColumn)),
            code);
    }

    public async Task<bool> CancelDirectedUninstallRequestAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, null, BuildCancelDirectedUninstallRequestSql());
        Bind(command,
            ("@id", id),
            ("@approved", UninstallGrant.Approved),
            ("@origin", UninstallGrant.OriginAdmin),
            ("@status", UninstallGrant.Cancelled)
        );
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    internal string BuildDeletePendingUninstallRequestsSql()
    {
        var table = options.UninstallRequestTable;
        return $"""
            DELETE FROM {Name(options.SchemaName, table.TableName)}
            WHERE {Name(table.PcForeignKeyColumn)} = @pcId
              AND {Name(table.StatusColumn)} = {Lit(UninstallGrant.Pending)};
            """;
    }

    internal string BuildInsertUninstallRequestSql()
    {
        var table = options.UninstallRequestTable;
        return InsertReturningId(
            Name(options.SchemaName, table.TableName),
            $"{Name(table.PcForeignKeyColumn)}, {Name(table.StatusColumn)}, {Name(table.RequestedAtUtcColumn)}, {Name(table.OriginColumn)}",
            "@pcId, @status, @requestedAt, @origin",
            table.PrimaryKeyColumn);
    }

    internal string BuildInsertDirectedUninstallRequestSql()
    {
        var table = options.UninstallRequestTable;
        return InsertReturningId(
            Name(options.SchemaName, table.TableName),
            $"""
            {Name(table.PcForeignKeyColumn)}, {Name(table.StatusColumn)}, {Name(table.RequestedAtUtcColumn)},
            {Name(table.ApprovedAtUtcColumn)}, {Name(table.ExpiresAtUtcColumn)}, {Name(table.CodeColumn)},
            {Name(table.CodeHashColumn)}, {Name(table.OriginColumn)}
            """,
            "@pcId, @status, @requestedAt, @approvedAt, @expiresAt, @code, @codeHash, @origin",
            table.PrimaryKeyColumn);
    }

    internal string BuildDeleteOpenUninstallRequestsSql()
    {
        var table = options.UninstallRequestTable;
        return $"""
            DELETE FROM {Name(options.SchemaName, table.TableName)}
            WHERE {Name(table.PcForeignKeyColumn)} = @pcId
              AND {Name(table.StatusColumn)} IN ({Lit(UninstallGrant.Pending)}, {Lit(UninstallGrant.Approved)});
            """;
    }

    internal string BuildGetDirectedUninstallCommandSql()
    {
        var table = options.UninstallRequestTable;
        var pc = options.PcTable;
        return $"""
            SELECT {TakePrefix("1")} r.{Name(table.PrimaryKeyColumn)}, r.{Name(table.StatusColumn)},
                   r.{Name(table.ExpiresAtUtcColumn)}, r.{Name(table.CodeColumn)}
            FROM {Name(options.SchemaName, table.TableName)} AS r
            INNER JOIN {Name(options.SchemaName, pc.TableName)} AS p
                ON p.{Name(pc.PrimaryKeyColumn)} = r.{Name(table.PcForeignKeyColumn)}
            WHERE ({PcLookupPredicate("p")})
              AND r.{Name(table.StatusColumn)} = @status
              AND r.{Name(table.OriginColumn)} = @origin
            ORDER BY r.{Name(table.RequestedAtUtcColumn)} DESC{TakeSuffix("1")};
            """;
    }

    internal string BuildCancelDirectedUninstallRequestSql()
    {
        var table = options.UninstallRequestTable;
        return $"""
            UPDATE {Name(options.SchemaName, table.TableName)}
            SET {Name(table.StatusColumn)} = @status,
                {Name(table.CodeColumn)} = NULL,
                {Name(table.CodeHashColumn)} = NULL
            WHERE {Name(table.PrimaryKeyColumn)} = @id
              AND {Name(table.StatusColumn)} = @approved
              AND {Name(table.OriginColumn)} = @origin
              AND {Name(table.ConsumedAtUtcColumn)} IS NULL;
            """;
    }

    internal string BuildGetAgentUninstallRequestSql()
    {
        var table = options.UninstallRequestTable;
        var pc = options.PcTable;
        return $"""
            SELECT r.{Name(table.PrimaryKeyColumn)}, p.{Name(pc.DeviceCodeColumn)}, r.{Name(table.StatusColumn)},
                   r.{Name(table.RequestedAtUtcColumn)}, r.{Name(table.ApprovedAtUtcColumn)},
                   r.{Name(table.ExpiresAtUtcColumn)}, r.{Name(table.CodeHashColumn)}, r.{Name(table.CodeColumn)}
            FROM {Name(options.SchemaName, table.TableName)} AS r
            INNER JOIN {Name(options.SchemaName, pc.TableName)} AS p
                ON p.{Name(pc.PrimaryKeyColumn)} = r.{Name(table.PcForeignKeyColumn)}
            WHERE r.{Name(table.PrimaryKeyColumn)} = @id
              AND ({PcLookupPredicate("p")});
            """;
    }

    internal string BuildConsumeUninstallRequestSql()
    {
        var table = options.UninstallRequestTable;
        return $"""
            UPDATE {Name(options.SchemaName, table.TableName)}
            SET {Name(table.StatusColumn)} = @status,
                {Name(table.ConsumedAtUtcColumn)} = @consumedAt,
                {Name(table.CodeColumn)} = NULL
            WHERE {Name(table.PrimaryKeyColumn)} = @id;
            """;
    }

    internal string BuildListUninstallRequestsSql(string? sort = null, bool sortDescending = false)
    {
        var table = options.UninstallRequestTable;
        var pc = options.PcTable;
        var pendingFirst = $"CASE WHEN r.{Name(table.StatusColumn)} = {Lit(UninstallGrant.Pending)} THEN 0 ELSE 1 END, r.{Name(table.RequestedAtUtcColumn)} DESC";
        var expression = UninstallOrderExpression(table, pc, sort);
        var orderBy = expression is null
            ? pendingFirst
            : OrderByColumn(expression, sortDescending, $"r.{Name(table.PrimaryKeyColumn)}");

        return $"""
            SELECT
                COUNT(*) OVER() AS total_count,
                r.{Name(table.PrimaryKeyColumn)}, p.{Name(pc.DeviceCodeColumn)},
                {DisplayHostNameSql("p")} AS {Name(pc.HostNameColumn)},
                r.{Name(table.StatusColumn)}, r.{Name(table.RequestedAtUtcColumn)},
                r.{Name(table.ApprovedAtUtcColumn)}, r.{Name(table.ConsumedAtUtcColumn)},
                r.{Name(table.ExpiresAtUtcColumn)}, r.{Name(table.OriginColumn)}
            FROM {Name(options.SchemaName, table.TableName)} AS r
            INNER JOIN {Name(options.SchemaName, pc.TableName)} AS p
                ON p.{Name(pc.PrimaryKeyColumn)} = r.{Name(table.PcForeignKeyColumn)}
            WHERE (@search IS NULL
                OR {HostNameSearchSql("p")}
                OR r.{Name(table.StatusColumn)} LIKE @search{LikeEscape}
                OR r.{Name(table.OriginColumn)} LIKE @search{LikeEscape})
            ORDER BY {orderBy}
            {Pagination};
            """;
    }

    private string? UninstallOrderExpression(UninstallRequestTableOptions table, PcTableOptions pc, string? sort) =>
        sort switch
        {
            AdminListSort.Id => $"r.{Name(table.PrimaryKeyColumn)}",
            AdminListSort.DeviceCode => $"p.{Name(pc.DeviceCodeColumn)}",
            AdminListSort.HostName => DisplayHostNameSql("p"),
            AdminListSort.Origin => $"r.{Name(table.OriginColumn)}",
            AdminListSort.Status => $"r.{Name(table.StatusColumn)}",
            AdminListSort.RequestedAtUtc => $"r.{Name(table.RequestedAtUtcColumn)}",
            AdminListSort.ApprovedAtUtc => $"r.{Name(table.ApprovedAtUtcColumn)}",
            AdminListSort.ExpiresAtUtc => $"r.{Name(table.ExpiresAtUtcColumn)}",
            _ => null
        };

    internal string BuildApproveUninstallRequestSql()
    {
        var table = options.UninstallRequestTable;
        return $"""
            UPDATE {Name(options.SchemaName, table.TableName)}
            SET {Name(table.StatusColumn)} = @status,
                {Name(table.CodeColumn)} = @code,
                {Name(table.CodeHashColumn)} = @codeHash,
                {Name(table.ApprovedAtUtcColumn)} = @approvedAt,
                {Name(table.ExpiresAtUtcColumn)} = @expiresAt
            WHERE {Name(table.PrimaryKeyColumn)} = @id
              AND {Name(table.StatusColumn)} = @pending;
            """;
    }

    internal string BuildDenyUninstallRequestSql()
    {
        var table = options.UninstallRequestTable;
        return $"""
            UPDATE {Name(options.SchemaName, table.TableName)}
            SET {Name(table.StatusColumn)} = @status,
                {Name(table.CodeColumn)} = NULL,
                {Name(table.CodeHashColumn)} = NULL
            WHERE {Name(table.PrimaryKeyColumn)} = @id
              AND {Name(table.StatusColumn)} = @pending;
            """;
    }

}
