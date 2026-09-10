using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.SqlClient;
using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

internal sealed partial class SqlServerDataContext
{
    public async Task<SnapshotSaveResult> SaveSnapshotAsync(InventoryIngestionRequest snapshot, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        if (await IsStaleSnapshotAsync(connection, transaction, snapshot, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new SnapshotSaveResult(false, [], []);
        }

        var pcId = await UpsertPcAsync(connection, transaction, snapshot.Pc, snapshot.CollectedAtUtc, null, cancellationToken);
        var previous = await ReadInstalledSoftwareAsync(connection, transaction, pcId, classification: null, cancellationToken);
        var software = options.InstalledSoftwareTable;
        await ExecuteAsync(connection, transaction,
            BuildDeleteInstalledSoftwareSql(),
            [new("@pcId", pcId)], cancellationToken);

        var policies = await ListEnabledPoliciesAsync(connection, transaction, cancellationToken);
        var matches = SoftwarePolicyMatcher.MatchAll(snapshot.InstalledSoftware, policies);
        foreach (var match in matches)
        {
            var entry = match.Software;
            var sql = $"""
                INSERT INTO {Name(options.SchemaName, software.TableName)}
                ({Name(software.PcForeignKeyColumn)}, {Name(software.DisplayNameColumn)}, {Name(software.DisplayVersionColumn)},
                 {Name(software.PublisherColumn)}, {Name(software.InstallLocationColumn)}, {Name(software.DiscoveryScopeColumn)},
                 {Name(software.DiscoverySourceColumn)}, {Name(software.ClassificationColumn)}, {Name(software.CollectedAtUtcColumn)})
                VALUES (@pcId, @name, @version, @publisher, @location, @scope, @source, @classification, @collectedAt)
                """;
            await ExecuteAsync(connection, transaction, sql,
            [
                new("@pcId", pcId), new("@name", Truncate(entry.Name.Trim(), 256)), new("@version", DbValue(Truncate(entry.Version, 64))),
                new("@publisher", DbValue(Truncate(entry.Publisher?.Trim(), 256))), new("@location", DbValue(Truncate(entry.InstallLocation, 512))),
                new("@scope", Truncate(entry.DiscoveryScope, 256)), new("@source", Truncate(entry.DiscoverySource, 64)),
                new("@classification", Truncate(match.StoredClassification, 32)!),
                new("@collectedAt", snapshot.CollectedAtUtc)
            ], cancellationToken);
        }

        var newViolations = await SyncViolationsAsync(connection, transaction, pcId, snapshot, matches, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new SnapshotSaveResult(true, previous, newViolations);
    }

    private async Task<IReadOnlyList<NewBlacklistViolation>> SyncViolationsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long pcId,
        InventoryIngestionRequest snapshot,
        IReadOnlyList<SoftwarePolicyMatch> matches,
        CancellationToken cancellationToken)
    {
        var current = InventoryDecisions.CollectCurrentViolations(matches);
        var existingNames = await ReadViolationSoftwareNamesAsync(connection, transaction, pcId, cancellationToken);
        var newlyDetected = InventoryDecisions.FindNewlyDetectedViolations(current, existingNames);

        foreach (var violation in current.Values)
        {
            await UpsertViolationAsync(connection, transaction, pcId, violation.Software, violation.Policy, snapshot.CollectedAtUtc, cancellationToken);
        }

        var table = options.SoftwareViolationTable;
        await ExecuteAsync(connection, transaction,
            $"""
            DELETE FROM {Name(options.SchemaName, table.TableName)}
            WHERE {Name(table.PcForeignKeyColumn)} = @pcId
              AND {Name(table.LastSeenAtUtcColumn)} < @collectedAt;
            """,
            [new("@pcId", pcId), new("@collectedAt", snapshot.CollectedAtUtc)],
            cancellationToken);

        return newlyDetected;
    }

    private async Task<List<string>> ReadViolationSoftwareNamesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long pcId,
        CancellationToken cancellationToken)
    {
        var violation = options.SoftwareViolationTable;
        var sql = $"""
            SELECT {Name(violation.DisplayNameColumn)}
            FROM {Name(options.SchemaName, violation.TableName)}
            WHERE {Name(violation.PcForeignKeyColumn)} = @pcId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@pcId", pcId));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var names = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private async Task UpsertViolationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long pcId,
        InstalledSoftwareEntry software,
        SoftwarePolicyEntry policy,
        DateTimeOffset collectedAt,
        CancellationToken cancellationToken)
    {
        var violation = options.SoftwareViolationTable;
        var sql = $"""
            UPDATE {Name(options.SchemaName, violation.TableName)}
            SET {Name(violation.PolicyForeignKeyColumn)} = @policyId,
                {Name(violation.DisplayVersionColumn)} = @version,
                {Name(violation.PublisherColumn)} = @publisher,
                {Name(violation.LastSeenAtUtcColumn)} = @collectedAt
            WHERE {Name(violation.PcForeignKeyColumn)} = @pcId
              AND {Name(violation.DisplayNameColumn)} = @name;
            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO {Name(options.SchemaName, violation.TableName)}
                ({Name(violation.PcForeignKeyColumn)}, {Name(violation.PolicyForeignKeyColumn)}, {Name(violation.DisplayNameColumn)},
                 {Name(violation.DisplayVersionColumn)}, {Name(violation.PublisherColumn)},
                 {Name(violation.DetectedAtUtcColumn)}, {Name(violation.LastSeenAtUtcColumn)})
                VALUES (@pcId, @policyId, @name, @version, @publisher, @collectedAt, @collectedAt);
            END;
            """;
        await ExecuteAsync(connection, transaction, sql,
        [
            new("@pcId", pcId),
            new("@policyId", policy.Id),
            new("@name", Truncate(software.Name.Trim(), 256)),
            new("@version", DbValue(Truncate(software.Version, 64))),
            new("@publisher", DbValue(Truncate(software.Publisher?.Trim(), 256))),
            new("@collectedAt", collectedAt)
        ], cancellationToken);
    }

    internal string BuildDeleteInstalledSoftwareSql()
    {
        var software = options.InstalledSoftwareTable;
        return $"DELETE FROM {Name(options.SchemaName, software.TableName)} WHERE {Name(software.PcForeignKeyColumn)} = @pcId";
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

}
