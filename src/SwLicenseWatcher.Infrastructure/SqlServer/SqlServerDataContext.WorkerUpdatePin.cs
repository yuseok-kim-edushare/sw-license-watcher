using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.SqlClient;
using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

internal sealed partial class SqlServerDataContext
{
    public async Task<UpdateManifest?> GetWorkerUpdatePinAsync(string targetServiceName, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(BuildGetWorkerUpdatePinSql(), connection);
        command.Parameters.Add(new SqlParameter("@targetServiceName", targetServiceName));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadWorkerUpdatePin(reader);
    }

    public async Task<UpdateManifest> UpsertWorkerUpdatePinAsync(UpdateManifest pin, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(BuildUpsertWorkerUpdatePinSql(), connection);
        AddWorkerUpdatePinParameters(command, pin, DateTimeOffset.UtcNow);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The Worker update pin upsert did not return a row.");
        }

        return ReadWorkerUpdatePin(reader);
    }

    public async Task<bool> SeedWorkerUpdatePinIfEmptyAsync(UpdateManifest pin, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(BuildSeedWorkerUpdatePinSql(), connection);
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
                INSERT ({Name(table.TargetServiceNameColumn)}, {Name(table.VersionColumn)}, {Name(table.PackageUrlColumn)},
                    {Name(table.Sha256Column)}, {Name(table.RequireAuthenticodeColumn)}, {Name(table.RollbackAfterMinutesColumn)},
                    {Name(table.UpdatedAtUtcColumn)})
                VALUES (@targetServiceName, @version, @packageUrl, @sha256, @requireAuthenticode, @rollbackAfterMinutes, @updatedAt)
            OUTPUT {WorkerUpdatePinSelectList("INSERTED")};
            """;
    }

    internal string BuildSeedWorkerUpdatePinSql()
    {
        var table = options.WorkerUpdatePinTable;
        return $"""
            IF NOT EXISTS (
                SELECT 1 FROM {Name(options.SchemaName, table.TableName)}
                WHERE {Name(table.TargetServiceNameColumn)} = @targetServiceName)
            BEGIN
                INSERT INTO {Name(options.SchemaName, table.TableName)}
                ({Name(table.TargetServiceNameColumn)}, {Name(table.VersionColumn)}, {Name(table.PackageUrlColumn)},
                 {Name(table.Sha256Column)}, {Name(table.RequireAuthenticodeColumn)}, {Name(table.RollbackAfterMinutesColumn)},
                 {Name(table.UpdatedAtUtcColumn)})
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

    private UpdateManifest ReadWorkerUpdatePin(SqlDataReader reader)
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

    private static void AddWorkerUpdatePinParameters(SqlCommand command, UpdateManifest pin, DateTimeOffset updatedAt)
    {
        command.Parameters.Add(new SqlParameter("@targetServiceName", Truncate(pin.TargetServiceName, 128)!));
        command.Parameters.Add(new SqlParameter("@version", Truncate(pin.Version, 32)!));
        command.Parameters.Add(new SqlParameter("@packageUrl", Truncate(pin.PackageUrl, 2048)!));
        command.Parameters.Add(new SqlParameter("@sha256", Truncate(pin.Sha256, 64)!));
        command.Parameters.Add(new SqlParameter("@requireAuthenticode", pin.RequireAuthenticode));
        command.Parameters.Add(new SqlParameter("@rollbackAfterMinutes", pin.RollbackAfterMinutes));
        command.Parameters.Add(new SqlParameter("@updatedAt", updatedAt));
    }

}
