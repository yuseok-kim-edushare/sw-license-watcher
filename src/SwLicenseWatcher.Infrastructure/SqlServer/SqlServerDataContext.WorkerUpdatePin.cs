using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.Relational;

public sealed partial class RelationalDataContext
{
    public async Task<UpdateManifest?> GetWorkerUpdatePinAsync(string targetServiceName, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, null, BuildGetWorkerUpdatePinSql());
        Bind(command, ("@targetServiceName", targetServiceName));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadWorkerUpdatePin(reader);
    }

    public async Task<UpdateManifest> UpsertWorkerUpdatePinAsync(UpdateManifest pin, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var updatedAt = DateTimeOffset.UtcNow;
        await using var command = CreateCommand(connection, null, BuildUpsertWorkerUpdatePinSql());
        AddWorkerUpdatePinParameters(command, pin, updatedAt);
        if (!dialect.DmlReturnsRows)
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return await GetWorkerUpdatePinAsync(pin.TargetServiceName, cancellationToken)
                ?? throw new InvalidOperationException("The Worker update pin upsert did not return a row.");
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The Worker update pin upsert did not return a row.");
        }

        return ReadWorkerUpdatePin(reader);
    }

    public async Task<bool> SeedWorkerUpdatePinIfEmptyAsync(UpdateManifest pin, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, null, BuildSeedWorkerUpdatePinSql());
        AddWorkerUpdatePinParameters(command, pin, DateTimeOffset.UtcNow);
        var inserted = await command.ExecuteNonQueryAsync(cancellationToken);
        return inserted > 0;
    }

    internal string BuildGetWorkerUpdatePinSql()
    {
        var table = options.WorkerUpdatePinTable;
        return $"""
            SELECT {WorkerUpdatePinSelectList()}
            FROM {Name(options.SchemaName, table.TableName)}
            WHERE {Name(table.TargetServiceNameColumn)} = @targetServiceName;
            """;
    }

    internal string BuildUpsertWorkerUpdatePinSql()
    {
        var table = options.WorkerUpdatePinTable;
        var qualified = Name(options.SchemaName, table.TableName);
        var columns = $"""
            {Name(table.TargetServiceNameColumn)}, {Name(table.VersionColumn)}, {Name(table.PackageUrlColumn)},
            {Name(table.Sha256Column)}, {Name(table.RequireAuthenticodeColumn)}, {Name(table.RollbackAfterMinutesColumn)},
            {Name(table.UpdatedAtUtcColumn)}
            """;
        var values = "@targetServiceName, @version, @packageUrl, @sha256, @requireAuthenticode, @rollbackAfterMinutes, @updatedAt";
        if (dialect.Provider == StorageProvider.PostgreSql)
        {
            return $"""
                INSERT INTO {qualified} ({columns})
                VALUES ({values})
                ON CONFLICT ({Name(table.TargetServiceNameColumn)}) DO UPDATE SET
                    {Name(table.VersionColumn)} = EXCLUDED.{Name(table.VersionColumn)},
                    {Name(table.PackageUrlColumn)} = EXCLUDED.{Name(table.PackageUrlColumn)},
                    {Name(table.Sha256Column)} = EXCLUDED.{Name(table.Sha256Column)},
                    {Name(table.RequireAuthenticodeColumn)} = EXCLUDED.{Name(table.RequireAuthenticodeColumn)},
                    {Name(table.RollbackAfterMinutesColumn)} = EXCLUDED.{Name(table.RollbackAfterMinutesColumn)},
                    {Name(table.UpdatedAtUtcColumn)} = EXCLUDED.{Name(table.UpdatedAtUtcColumn)}
                RETURNING {WorkerUpdatePinSelectList()};
                """;
        }

        if (dialect.Provider == StorageProvider.MySql)
        {
            return $"""
                INSERT INTO {qualified} ({columns})
                VALUES ({values})
                ON DUPLICATE KEY UPDATE
                    {Name(table.VersionColumn)} = VALUES({table.VersionColumn}),
                    {Name(table.PackageUrlColumn)} = VALUES({table.PackageUrlColumn}),
                    {Name(table.Sha256Column)} = VALUES({table.Sha256Column}),
                    {Name(table.RequireAuthenticodeColumn)} = VALUES({table.RequireAuthenticodeColumn}),
                    {Name(table.RollbackAfterMinutesColumn)} = VALUES({table.RollbackAfterMinutesColumn}),
                    {Name(table.UpdatedAtUtcColumn)} = VALUES({table.UpdatedAtUtcColumn});
                """;
        }

        return $"""
            MERGE {qualified} AS target
            USING (SELECT @targetServiceName AS {Name(table.TargetServiceNameColumn)}) AS source
            ON target.{Name(table.TargetServiceNameColumn)} = source.{Name(table.TargetServiceNameColumn)}
            WHEN MATCHED THEN
                UPDATE SET {Name(table.VersionColumn)} = @version,
                    {Name(table.PackageUrlColumn)} = @packageUrl,
                    {Name(table.Sha256Column)} = @sha256,
                    {Name(table.RequireAuthenticodeColumn)} = @requireAuthenticode,
                    {Name(table.RollbackAfterMinutesColumn)} = @rollbackAfterMinutes,
                    {Name(table.UpdatedAtUtcColumn)} = @updatedAt
            WHEN NOT MATCHED THEN
                INSERT ({columns})
                VALUES ({values})
            OUTPUT {WorkerUpdatePinSelectList("INSERTED")};
            """;
    }

    internal string BuildSeedWorkerUpdatePinSql()
    {
        var table = options.WorkerUpdatePinTable;
        var qualified = Name(options.SchemaName, table.TableName);
        var columns = $"""
            {Name(table.TargetServiceNameColumn)}, {Name(table.VersionColumn)}, {Name(table.PackageUrlColumn)},
            {Name(table.Sha256Column)}, {Name(table.RequireAuthenticodeColumn)}, {Name(table.RollbackAfterMinutesColumn)},
            {Name(table.UpdatedAtUtcColumn)}
            """;
        if (dialect.Provider != StorageProvider.SqlServer)
        {
            return $"""
                INSERT INTO {qualified} ({columns})
                SELECT @targetServiceName, @version, @packageUrl, @sha256, @requireAuthenticode, @rollbackAfterMinutes, @updatedAt
                WHERE NOT EXISTS (
                    SELECT 1 FROM {qualified}
                    WHERE {Name(table.TargetServiceNameColumn)} = @targetServiceName);
                """;
        }

        return $"""
            IF NOT EXISTS (
                SELECT 1 FROM {qualified}
                WHERE {Name(table.TargetServiceNameColumn)} = @targetServiceName)
            BEGIN
                INSERT INTO {qualified}
                ({columns})
                VALUES (@targetServiceName, @version, @packageUrl, @sha256, @requireAuthenticode, @rollbackAfterMinutes, @updatedAt);
            END;
            """;
    }

    private string WorkerUpdatePinSelectList(string? tableAlias = null)
    {
        var table = options.WorkerUpdatePinTable;
        var prefix = string.IsNullOrEmpty(tableAlias) ? string.Empty : tableAlias + ".";
        return string.Join(", ",
        [
            $"{prefix}{Name(table.TargetServiceNameColumn)}",
            $"{prefix}{Name(table.VersionColumn)}",
            $"{prefix}{Name(table.PackageUrlColumn)}",
            $"{prefix}{Name(table.Sha256Column)}",
            $"{prefix}{Name(table.RequireAuthenticodeColumn)}",
            $"{prefix}{Name(table.RollbackAfterMinutesColumn)}"
        ]);
    }

    private UpdateManifest ReadWorkerUpdatePin(DbDataReader reader)
    {
        var table = options.WorkerUpdatePinTable;
        return new UpdateManifest(
            reader.GetString(reader.GetOrdinal(table.TargetServiceNameColumn)),
            reader.GetString(reader.GetOrdinal(table.VersionColumn)),
            reader.GetString(reader.GetOrdinal(table.PackageUrlColumn)),
            reader.GetString(reader.GetOrdinal(table.Sha256Column)),
            reader.GetBoolean(reader.GetOrdinal(table.RequireAuthenticodeColumn)),
            reader.GetInt32(reader.GetOrdinal(table.RollbackAfterMinutesColumn)));
    }

    private void AddWorkerUpdatePinParameters(DbCommand command, UpdateManifest pin, DateTimeOffset updatedAt)
    {
        Bind(command, ("@targetServiceName", Truncate(pin.TargetServiceName, 128)!));
        Bind(command, ("@version", Truncate(pin.Version, 32)!));
        Bind(command, ("@packageUrl", Truncate(pin.PackageUrl, 2048)!));
        Bind(command, ("@sha256", Truncate(pin.Sha256, 64)!));
        Bind(command, ("@requireAuthenticode", pin.RequireAuthenticode));
        Bind(command, ("@rollbackAfterMinutes", pin.RollbackAfterMinutes));
        Bind(command, ("@updatedAt", updatedAt));
    }

}
