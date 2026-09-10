using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.SqlClient;
using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

internal sealed partial class SqlServerDataContext
{
    public async Task<UninstallRequestCreatedResponse?> CreateUninstallRequestAsync(
        string deviceCode,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

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
            [new("@pcId", pcId.Value)],
            cancellationToken);

        var requestedAtUtc = DateTimeOffset.UtcNow;
        await using var insert = new SqlCommand(BuildInsertUninstallRequestSql(), connection, transaction);
        insert.Parameters.AddRange(
        [
            new("@pcId", pcId.Value),
            new("@status", UninstallGrant.Pending),
            new("@requestedAt", requestedAtUtc)
        ]);
        var id = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return new UninstallRequestCreatedResponse(id, deviceCode, UninstallGrant.Pending, requestedAtUtc);
    }

    public async Task<AgentUninstallRequestResponse?> GetAgentUninstallRequestAsync(
        long id,
        string deviceCode,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(BuildGetAgentUninstallRequestSql(), connection);
        command.Parameters.AddRange([new("@id", id), new("@deviceCode", deviceCode)]);
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
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal(uninstall.RequestedAtUtcColumn)),
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
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using var lookup = new SqlCommand(BuildGetAgentUninstallRequestSql(), connection, transaction);
        lookup.Parameters.AddRange([new("@id", id), new("@deviceCode", deviceCode)]);
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
            [new("@id", id), new("@consumedAt", DateTimeOffset.UtcNow), new("@status", UninstallGrant.Consumed)],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<(int TotalCount, List<AdminUninstallRequest> Items)> ListUninstallRequestsAsync(
        int skip,
        int take,
        string? search,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(BuildListUninstallRequestsSql(), connection);
        command.Parameters.AddRange(
        [
            new("@search", DbValue(ToContainsPattern(search))),
            new("@skip", skip),
            new("@take", take)
        ]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<AdminUninstallRequest>();
        var totalCount = 0;
        var uninstall = options.UninstallRequestTable;
        var pc = options.PcTable;
        var utcNow = DateTimeOffset.UtcNow;
        while (await reader.ReadAsync(cancellationToken))
        {
            totalCount = reader.GetInt32(reader.GetOrdinal("total_count"));
            var expiresAtUtc = ReadNullableDateTimeOffset(reader, uninstall.ExpiresAtUtcColumn);
            items.Add(new AdminUninstallRequest(
                reader.GetInt64(reader.GetOrdinal(uninstall.PrimaryKeyColumn)),
                reader.GetString(reader.GetOrdinal(pc.DeviceCodeColumn)),
                reader.GetString(reader.GetOrdinal(pc.HostNameColumn)),
                UninstallGrant.ResolveStatus(
                    reader.GetString(reader.GetOrdinal(uninstall.StatusColumn)),
                    expiresAtUtc,
                    utcNow),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal(uninstall.RequestedAtUtcColumn)),
                ReadNullableDateTimeOffset(reader, uninstall.ApprovedAtUtcColumn),
                ReadNullableDateTimeOffset(reader, uninstall.ConsumedAtUtcColumn),
                expiresAtUtc));
        }

        return (totalCount, items);
    }

    public async Task<bool> ApproveUninstallRequestAsync(long id, CancellationToken cancellationToken)
    {
        var code = UninstallGrant.CreateCode();
        var approvedAtUtc = DateTimeOffset.UtcNow;
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(BuildApproveUninstallRequestSql(), connection);
        command.Parameters.AddRange(
        [
            new("@id", id),
            new("@pending", UninstallGrant.Pending),
            new("@status", UninstallGrant.Approved),
            new("@code", code),
            new("@codeHash", UninstallGrant.HashCode(code)),
            new("@approvedAt", approvedAtUtc),
            new("@expiresAt", approvedAtUtc + UninstallGrant.ApprovalLifetime)
        ]);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> DenyUninstallRequestAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(BuildDenyUninstallRequestSql(), connection);
        command.Parameters.AddRange(
        [
            new("@id", id),
            new("@pending", UninstallGrant.Pending),
            new("@status", UninstallGrant.Denied)
        ]);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    internal string BuildDeletePendingUninstallRequestsSql()
    {
        var table = options.UninstallRequestTable;
        return $"""
            DELETE FROM {Name(options.SchemaName, table.TableName)}
            WHERE {Name(table.PcForeignKeyColumn)} = @pcId
              AND {Name(table.StatusColumn)} = N'{UninstallGrant.Pending}';
            """;
    }

    internal string BuildInsertUninstallRequestSql()
    {
        var table = options.UninstallRequestTable;
        return $"""
            INSERT INTO {Name(options.SchemaName, table.TableName)}
            ({Name(table.PcForeignKeyColumn)}, {Name(table.StatusColumn)}, {Name(table.RequestedAtUtcColumn)})
            OUTPUT INSERTED.{Name(table.PrimaryKeyColumn)}
            VALUES (@pcId, @status, @requestedAt);
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
              AND p.{Name(pc.DeviceCodeColumn)} = @deviceCode;
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

    internal string BuildListUninstallRequestsSql()
    {
        var table = options.UninstallRequestTable;
        var pc = options.PcTable;
        return $"""
            SELECT
                COUNT(*) OVER() AS total_count,
                r.{Name(table.PrimaryKeyColumn)}, p.{Name(pc.DeviceCodeColumn)},
                {DisplayHostNameSql("p")} AS {Name(pc.HostNameColumn)},
                r.{Name(table.StatusColumn)}, r.{Name(table.RequestedAtUtcColumn)},
                r.{Name(table.ApprovedAtUtcColumn)}, r.{Name(table.ConsumedAtUtcColumn)},
                r.{Name(table.ExpiresAtUtcColumn)}
            FROM {Name(options.SchemaName, table.TableName)} AS r
            INNER JOIN {Name(options.SchemaName, pc.TableName)} AS p
                ON p.{Name(pc.PrimaryKeyColumn)} = r.{Name(table.PcForeignKeyColumn)}
            WHERE (@search IS NULL
                OR {HostNameSearchSql("p")}
                OR r.{Name(table.StatusColumn)} LIKE @search)
            ORDER BY CASE WHEN r.{Name(table.StatusColumn)} = N'{UninstallGrant.Pending}' THEN 0 ELSE 1 END,
                     r.{Name(table.RequestedAtUtcColumn)} DESC
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            """;
    }

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
