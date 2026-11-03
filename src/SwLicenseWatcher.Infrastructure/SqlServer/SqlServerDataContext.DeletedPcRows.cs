using System.Globalization;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.Relational;

public sealed partial class RelationalDataContext
{
    public async Task<int> RemoveRowsLeftByDeletedPcsAsync(CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var removed = 0;
        foreach (var sql in BuildRemoveRowsLeftByDeletedPcsSql())
        {
            removed += await ExecuteAsync(connection, transaction, sql, [], cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return removed;
    }

    internal async Task DeleteInstalledSoftwareAsync(long pcId, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, null, BuildDeleteInstalledSoftwareSql(), [("@pcId", pcId)], cancellationToken);
    }

    internal async Task<int> CountPcChildRowsAsync(long pcId, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var total = 0;
        foreach (var (table, column) in PcChildTables())
        {
            var sql = $"""
                SELECT COUNT(*)
                FROM {Name(options.SchemaName, table)}
                WHERE {Name(column)} = @pcId;
                """;
            await using var command = CreateCommand(connection, null, sql);
            Bind(command, ("@pcId", pcId));
            var value = await command.ExecuteScalarAsync(cancellationToken);
            total += Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        return total;
    }

    internal async Task DeletePcKeepingChildRowsAsync(long pcId, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        if (dialect.Provider == StorageProvider.MySql)
        {
            await ExecuteAsync(connection, null, "SET FOREIGN_KEY_CHECKS = 0", [], cancellationToken);
            try
            {
                await ExecuteAsync(connection, null, BuildDeleteDeviceSql(), [("@pcId", pcId)], cancellationToken);
            }
            finally
            {
                await ExecuteAsync(connection, null, "SET FOREIGN_KEY_CHECKS = 1", [], cancellationToken);
            }

            return;
        }

        foreach (var table in ForeignKeyToggleTables())
        {
            await ExecuteAsync(connection, null, ForeignKeyToggleSql(table, enabled: false), [], cancellationToken);
        }

        try
        {
            await ExecuteAsync(connection, null, BuildDeleteDeviceSql(), [("@pcId", pcId)], cancellationToken);
        }
        finally
        {
            foreach (var table in ForeignKeyToggleTables())
            {
                await ExecuteAsync(connection, null, ForeignKeyToggleSql(table, enabled: true), [], cancellationToken);
            }
        }
    }

    internal IReadOnlyList<string> BuildDeletePcChildRowsSql()
    {
        var sql = new List<string>();
        foreach (var (table, column) in PcChildTables())
        {
            sql.Add($"""
                DELETE FROM {Name(options.SchemaName, table)}
                WHERE {Name(column)} = @pcId;
                """);
        }

        return sql;
    }

    internal IReadOnlyList<string> BuildRemoveRowsLeftByDeletedPcsSql()
    {
        var pc = options.PcTable;
        var sql = new List<string>();
        foreach (var (table, column) in PcChildTables())
        {
            sql.Add(DeleteAliasedSql(
                table,
                $"""
                NOT EXISTS (
                    SELECT 1
                    FROM {Name(options.SchemaName, pc.TableName)} AS parent
                    WHERE parent.{Name(pc.PrimaryKeyColumn)} = target.{Name(column)})
                """));
        }

        var violation = options.SoftwareViolationTable;
        var software = options.InstalledSoftwareTable;
        sql.Add(DeleteAliasedSql(
            violation.TableName,
            $"""
            NOT EXISTS (
                SELECT 1
                FROM {Name(options.SchemaName, software.TableName)} AS installed
                WHERE installed.{Name(software.PcForeignKeyColumn)} = target.{Name(violation.PcForeignKeyColumn)}
                  AND installed.{Name(software.DisplayNameColumn)} = target.{Name(violation.DisplayNameColumn)})
            """));
        return sql;
    }

    private string DeleteAliasedSql(string table, string predicate)
    {
        var qualified = Name(options.SchemaName, table);
        return dialect.Provider == StorageProvider.PostgreSql
            ? $"""
              DELETE FROM {qualified} AS target
              WHERE {predicate};
              """
            : $"""
              DELETE target
              FROM {qualified} AS target
              WHERE {predicate};
              """;
    }

    private string ForeignKeyToggleSql(string table, bool enabled)
    {
        var qualified = Name(options.SchemaName, table);
        if (dialect.Provider == StorageProvider.PostgreSql)
        {
            return enabled
                ? $"ALTER TABLE {qualified} ENABLE TRIGGER ALL"
                : $"ALTER TABLE {qualified} DISABLE TRIGGER ALL";
        }

        return enabled
            ? $"ALTER TABLE {qualified} WITH NOCHECK CHECK CONSTRAINT ALL"
            : $"ALTER TABLE {qualified} NOCHECK CONSTRAINT ALL";
    }

    private IEnumerable<string> ForeignKeyToggleTables()
    {
        if (dialect.Provider == StorageProvider.PostgreSql)
        {
            yield return options.PcTable.TableName;
        }

        foreach (var (table, _) in PcChildTables())
        {
            yield return table;
        }
    }

    private IEnumerable<(string Table, string PcColumn)> PcChildTables()
    {
        var software = options.InstalledSoftwareTable;
        var violation = options.SoftwareViolationTable;
        var license = options.SoftwareLicenseTable;
        var uninstall = options.UninstallRequestTable;
        var message = options.UserMessageTable;
        var stale = options.StaleHeartbeatNotificationTable;
        yield return (software.TableName, software.PcForeignKeyColumn);
        yield return (violation.TableName, violation.PcForeignKeyColumn);
        yield return (license.TableName, license.PcForeignKeyColumn);
        yield return (uninstall.TableName, uninstall.PcForeignKeyColumn);
        yield return (message.TableName, message.PcForeignKeyColumn);
        yield return (stale.TableName, stale.PcForeignKeyColumn);
    }
}
