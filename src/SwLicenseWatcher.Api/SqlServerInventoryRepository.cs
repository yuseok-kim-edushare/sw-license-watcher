using System.Data;
using Microsoft.Data.SqlClient;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

public sealed class SqlServerInventoryRepository(SqlServerStorageOptions options)
{
    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);
    }

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
            $"DELETE FROM {Name(options.SchemaName, software.TableName)} WHERE {Name(software.PcForeignKeyColumn)} = @pcId",
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
                new("@pcId", pcId), new("@name", Truncate(entry.Name, 256)), new("@version", DbValue(Truncate(entry.Version, 64))),
                new("@publisher", DbValue(Truncate(entry.Publisher, 256))), new("@location", DbValue(Truncate(entry.InstallLocation, 512))),
                new("@scope", Truncate(entry.DiscoveryScope, 256)), new("@source", Truncate(entry.DiscoverySource, 64)),
                new("@classification", Truncate(match.StoredClassification, 32)!),
                new("@collectedAt", snapshot.CollectedAtUtc)
            ], cancellationToken);
        }

        var newViolations = await SyncViolationsAsync(connection, transaction, pcId, snapshot, matches, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new SnapshotSaveResult(true, previous, newViolations);
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

    public async Task<List<StalePcHeartbeat>> GetStaleHeartbeatsAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var table = options.PcTable;
        var sql = BuildGetStaleHeartbeatsSql();
        await using var command = new SqlCommand(sql, connection);
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

    public async Task<(int TotalCount, List<DeviceSummary> Items)> ListDevicesAsync(
        int skip,
        int take,
        string? search,
        int? staleAfterHours,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var table = options.PcTable;
        DateTimeOffset? staleCutoff = staleAfterHours is int hours
            ? DateTimeOffset.UtcNow - TimeSpan.FromHours(hours)
            : null;
        var sql = $"""
            SELECT
                COUNT(*) OVER() AS total_count,
                {Name(table.DeviceCodeColumn)}, {Name(table.HostNameColumn)}, {Name(table.DomainNameColumn)},
                {Name(table.OperatingSystemColumn)}, {Name(table.AgentVersionColumn)},
                {Name(table.LastHeartbeatUtcColumn)}, {Name(table.LastInventoryUtcColumn)}
            FROM {Name(options.SchemaName, table.TableName)}
            WHERE (@search IS NULL
                OR {Name(table.DeviceCodeColumn)} LIKE @search
                OR {Name(table.HostNameColumn)} LIKE @search)
              AND (@staleCutoff IS NULL
                OR {Name(table.LastHeartbeatUtcColumn)} IS NULL
                OR {Name(table.LastHeartbeatUtcColumn)} < @staleCutoff)
            ORDER BY {Name(table.HostNameColumn)}, {Name(table.DeviceCodeColumn)}
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@search", DbValue(ToContainsPattern(search))));
        command.Parameters.Add(new SqlParameter("@staleCutoff", DbValue(staleCutoff)));
        command.Parameters.Add(new SqlParameter("@skip", skip));
        command.Parameters.Add(new SqlParameter("@take", take));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<DeviceSummary>();
        var totalCount = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (items.Count == 0)
            {
                totalCount = reader.GetInt32(reader.GetOrdinal("total_count"));
            }

            items.Add(new DeviceSummary(
                reader.GetString(reader.GetOrdinal(table.DeviceCodeColumn)),
                reader.GetString(reader.GetOrdinal(table.HostNameColumn)),
                reader.GetString(reader.GetOrdinal(table.DomainNameColumn)),
                reader.GetString(reader.GetOrdinal(table.OperatingSystemColumn)),
                reader.GetString(reader.GetOrdinal(table.AgentVersionColumn)),
                ReadNullableDateTimeOffset(reader, table.LastHeartbeatUtcColumn),
                ReadNullableDateTimeOffset(reader, table.LastInventoryUtcColumn)));
        }

        return (totalCount, items);
    }

    public async Task<DeviceDetail?> GetDeviceAsync(
        string deviceCode,
        string? classification,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var table = options.PcTable;
        var sql = $"""
            SELECT {Name(table.PrimaryKeyColumn)}, {Name(table.DeviceCodeColumn)}, {Name(table.HostNameColumn)},
                   {Name(table.DomainNameColumn)}, {Name(table.OperatingSystemColumn)}, {Name(table.AgentVersionColumn)},
                   {Name(table.LastHeartbeatUtcColumn)}, {Name(table.LastInventoryUtcColumn)}
            FROM {Name(options.SchemaName, table.TableName)}
            WHERE {Name(table.DeviceCodeColumn)} = @deviceCode;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@deviceCode", deviceCode));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var pcId = reader.GetInt64(reader.GetOrdinal(table.PrimaryKeyColumn));
        var detail = new DeviceDetail(
            reader.GetString(reader.GetOrdinal(table.DeviceCodeColumn)),
            reader.GetString(reader.GetOrdinal(table.HostNameColumn)),
            reader.GetString(reader.GetOrdinal(table.DomainNameColumn)),
            reader.GetString(reader.GetOrdinal(table.OperatingSystemColumn)),
            reader.GetString(reader.GetOrdinal(table.AgentVersionColumn)),
            ReadNullableDateTimeOffset(reader, table.LastHeartbeatUtcColumn),
            ReadNullableDateTimeOffset(reader, table.LastInventoryUtcColumn),
            []);
        await reader.CloseAsync();

        var installed = await ReadInstalledSoftwareAsync(connection, transaction: null, pcId, classification, cancellationToken);
        return detail with { InstalledSoftware = installed };
    }

    public async Task<(int TotalCount, List<SoftwareAggregate> Items)> ListSoftwareAsync(
        int skip,
        int take,
        string? search,
        string? classification,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var software = options.InstalledSoftwareTable;
        var sql = $"""
            SELECT
                COUNT(*) OVER() AS total_count,
                {Name(software.DisplayNameColumn)}, {Name(software.DisplayVersionColumn)},
                {Name(software.ClassificationColumn)},
                COUNT(DISTINCT {Name(software.PcForeignKeyColumn)}) AS device_count
            FROM {Name(options.SchemaName, software.TableName)}
            WHERE (@search IS NULL OR {Name(software.DisplayNameColumn)} LIKE @search)
              AND (@classification IS NULL OR {Name(software.ClassificationColumn)} = @classification)
            GROUP BY {Name(software.DisplayNameColumn)}, {Name(software.DisplayVersionColumn)}, {Name(software.ClassificationColumn)}
            ORDER BY {Name(software.DisplayNameColumn)}, {Name(software.DisplayVersionColumn)}, {Name(software.ClassificationColumn)}
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@search", DbValue(ToContainsPattern(search))));
        command.Parameters.Add(new SqlParameter("@classification", DbValue(classification)));
        command.Parameters.Add(new SqlParameter("@skip", skip));
        command.Parameters.Add(new SqlParameter("@take", take));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<SoftwareAggregate>();
        var totalCount = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (items.Count == 0)
            {
                totalCount = reader.GetInt32(reader.GetOrdinal("total_count"));
            }

            items.Add(new SoftwareAggregate(
                reader.GetString(reader.GetOrdinal(software.DisplayNameColumn)),
                ReadNullableString(reader, software.DisplayVersionColumn),
                ReadClassification(reader, software.ClassificationColumn),
                reader.GetInt32(reader.GetOrdinal("device_count"))));
        }

        return (totalCount, items);
    }

    public async Task<(int TotalCount, List<SoftwareDevice> Items)> ListSoftwareDevicesAsync(
        string name,
        int skip,
        int take,
        string? classification,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var software = options.InstalledSoftwareTable;
        var pc = options.PcTable;
        var sql = $"""
            SELECT
                COUNT(*) OVER() AS total_count,
                p.{Name(pc.DeviceCodeColumn)}, p.{Name(pc.HostNameColumn)}, p.{Name(pc.DomainNameColumn)},
                p.{Name(pc.OperatingSystemColumn)}, p.{Name(pc.AgentVersionColumn)},
                p.{Name(pc.LastHeartbeatUtcColumn)}, p.{Name(pc.LastInventoryUtcColumn)},
                s.{Name(software.DisplayVersionColumn)}, s.{Name(software.PublisherColumn)},
                s.{Name(software.ClassificationColumn)}
            FROM {Name(options.SchemaName, software.TableName)} AS s
            INNER JOIN {Name(options.SchemaName, pc.TableName)} AS p
                ON p.{Name(pc.PrimaryKeyColumn)} = s.{Name(software.PcForeignKeyColumn)}
            WHERE s.{Name(software.DisplayNameColumn)} = @name
              AND (@classification IS NULL OR s.{Name(software.ClassificationColumn)} = @classification)
            ORDER BY p.{Name(pc.HostNameColumn)}, p.{Name(pc.DeviceCodeColumn)}
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@name", name));
        command.Parameters.Add(new SqlParameter("@classification", DbValue(classification)));
        command.Parameters.Add(new SqlParameter("@skip", skip));
        command.Parameters.Add(new SqlParameter("@take", take));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<SoftwareDevice>();
        var totalCount = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (items.Count == 0)
            {
                totalCount = reader.GetInt32(reader.GetOrdinal("total_count"));
            }

            items.Add(new SoftwareDevice(
                reader.GetString(reader.GetOrdinal(pc.DeviceCodeColumn)),
                reader.GetString(reader.GetOrdinal(pc.HostNameColumn)),
                reader.GetString(reader.GetOrdinal(pc.DomainNameColumn)),
                reader.GetString(reader.GetOrdinal(pc.OperatingSystemColumn)),
                reader.GetString(reader.GetOrdinal(pc.AgentVersionColumn)),
                ReadNullableDateTimeOffset(reader, pc.LastHeartbeatUtcColumn),
                ReadNullableDateTimeOffset(reader, pc.LastInventoryUtcColumn),
                ReadNullableString(reader, software.DisplayVersionColumn),
                ReadNullableString(reader, software.PublisherColumn),
                ReadClassification(reader, software.ClassificationColumn)));
        }

        return (totalCount, items);
    }

    public async Task<(int TotalCount, List<SoftwarePolicyEntry> Items)> ListPoliciesAsync(
        int skip,
        int take,
        string? search,
        string? classification,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(BuildListPoliciesSql(), connection);
        command.Parameters.Add(new SqlParameter("@search", DbValue(ToContainsPattern(search))));
        command.Parameters.Add(new SqlParameter("@classification", DbValue(classification)));
        command.Parameters.Add(new SqlParameter("@skip", skip));
        command.Parameters.Add(new SqlParameter("@take", take));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<SoftwarePolicyEntry>();
        var totalCount = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            var policy = ReadPolicy(reader);
            if (policy is null)
            {
                continue;
            }

            if (items.Count == 0)
            {
                totalCount = reader.GetInt32(reader.GetOrdinal("total_count"));
            }

            items.Add(policy);
        }

        return (totalCount, items);
    }

    public async Task<SoftwarePolicyEntry?> GetPolicyAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return await GetPolicyAsync(connection, transaction: null, id, cancellationToken);
    }

    public async Task<SoftwarePolicyEntry> CreatePolicyAsync(SoftwarePolicyWriteRequest request, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var table = options.SoftwarePolicyTable;
        var updatedAt = DateTimeOffset.UtcNow;
        var sql = $"""
            INSERT INTO {Name(options.SchemaName, table.TableName)}
            ({Name(table.ClassificationColumn)}, {Name(table.ProductNameColumn)}, {Name(table.PublisherColumn)},
             {Name(table.VersionPatternColumn)}, {Name(table.NotesColumn)}, {Name(table.EnabledColumn)},
             {Name(table.UpdatedAtUtcColumn)})
            OUTPUT {PolicySelectList("INSERTED")}
            VALUES (@classification, @productName, @publisher, @versionPattern, @notes, @enabled, @updatedAt);
            """;
        await using var command = new SqlCommand(sql, connection);
        AddPolicyWriteParameters(command, request, updatedAt);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The software policy insert did not return a row.");
        }

        return ReadPolicy(reader) ?? throw new InvalidOperationException("The software policy insert returned an invalid classification.");
    }

    public async Task<SoftwarePolicyEntry?> UpdatePolicyAsync(long id, SoftwarePolicyWriteRequest request, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var existing = await GetPolicyAsync(connection, transaction, id, cancellationToken);
        if (existing is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var table = options.SoftwarePolicyTable;
        var updatedAt = DateTimeOffset.UtcNow;
        var sql = $"""
            UPDATE {Name(options.SchemaName, table.TableName)}
            SET {Name(table.ClassificationColumn)} = @classification,
                {Name(table.ProductNameColumn)} = @productName,
                {Name(table.PublisherColumn)} = @publisher,
                {Name(table.VersionPatternColumn)} = @versionPattern,
                {Name(table.NotesColumn)} = @notes,
                {Name(table.EnabledColumn)} = @enabled,
                {Name(table.UpdatedAtUtcColumn)} = @updatedAt
            OUTPUT {PolicySelectList("INSERTED")}
            WHERE {Name(table.PrimaryKeyColumn)} = @id;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@id", id));
        AddPolicyWriteParameters(command, request, updatedAt);
        SoftwarePolicyEntry? updated;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            updated = ReadPolicy(reader);
        }

        if (updated is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("The software policy update returned an invalid classification.");
        }

        if (ShouldClearViolations(existing, updated))
        {
            await DeleteViolationsForPolicyAsync(connection, transaction, id, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return updated;
    }

    public async Task<bool> DeletePolicyAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var table = options.SoftwarePolicyTable;
        var sql = $"DELETE FROM {Name(options.SchemaName, table.TableName)} WHERE {Name(table.PrimaryKeyColumn)} = @id;";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@id", id));
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<(int TotalCount, List<SoftwareViolationEntry> Items)> ListViolationsAsync(
        int skip,
        int take,
        string? search,
        DateTimeOffset? since,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(BuildListViolationsSql(), connection);
        command.Parameters.Add(new SqlParameter("@search", DbValue(ToContainsPattern(search))));
        command.Parameters.Add(new SqlParameter("@since", DbValue(since)));
        command.Parameters.Add(new SqlParameter("@skip", skip));
        command.Parameters.Add(new SqlParameter("@take", take));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<SoftwareViolationEntry>();
        var totalCount = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!SoftwarePolicyClassificationNames.TryParse(reader.GetString(reader.GetOrdinal("classification")), out var classification))
            {
                continue;
            }

            if (items.Count == 0)
            {
                totalCount = reader.GetInt32(reader.GetOrdinal("total_count"));
            }

            items.Add(new SoftwareViolationEntry(
                reader.GetInt64(reader.GetOrdinal("id")),
                reader.GetString(reader.GetOrdinal("deviceCode")),
                reader.GetString(reader.GetOrdinal("hostName")),
                reader.GetString(reader.GetOrdinal("softwareName")),
                ReadNullableString(reader, "softwareVersion"),
                ReadNullableString(reader, "publisher"),
                reader.GetInt64(reader.GetOrdinal("policyId")),
                reader.GetString(reader.GetOrdinal("policyProductName")),
                classification,
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("detectedAtUtc")),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("lastSeenAtUtc"))));
        }

        return (totalCount, items);
    }

    private async Task<IReadOnlyList<NewBlacklistViolation>> SyncViolationsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long pcId,
        InventoryIngestionRequest snapshot,
        IReadOnlyList<SoftwarePolicyMatch> matches,
        CancellationToken cancellationToken)
    {
        var current = CollectCurrentViolations(matches);
        var existingNames = await ReadViolationSoftwareNamesAsync(connection, transaction, pcId, cancellationToken);
        var newlyDetected = FindNewlyDetectedViolations(current, existingNames);

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

    internal static Dictionary<string, NewBlacklistViolation> CollectCurrentViolations(
        IReadOnlyList<SoftwarePolicyMatch> matches)
    {
        var current = new Dictionary<string, NewBlacklistViolation>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in matches)
        {
            if (!match.IsBlacklisted || match.Policy is null)
            {
                continue;
            }

            var name = Truncate(match.Software.Name, 256);
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            current.TryAdd(name, new NewBlacklistViolation(match.Software, match.Policy));
        }

        return current;
    }

    internal static IReadOnlyList<NewBlacklistViolation> FindNewlyDetectedViolations(
        IReadOnlyDictionary<string, NewBlacklistViolation> current,
        IReadOnlyCollection<string> existingSoftwareNames)
    {
        var existing = new HashSet<string>(existingSoftwareNames, StringComparer.OrdinalIgnoreCase);
        var added = new List<NewBlacklistViolation>();
        foreach (var (name, violation) in current)
        {
            if (!existing.Contains(name))
            {
                added.Add(violation);
            }
        }

        return added;
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
            new("@name", Truncate(software.Name, 256)),
            new("@version", DbValue(Truncate(software.Version, 64))),
            new("@publisher", DbValue(Truncate(software.Publisher, 256))),
            new("@collectedAt", collectedAt)
        ], cancellationToken);
    }

    private async Task DeleteViolationsForPolicyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long policyId,
        CancellationToken cancellationToken)
    {
        var violation = options.SoftwareViolationTable;
        await ExecuteAsync(connection, transaction,
            $"DELETE FROM {Name(options.SchemaName, violation.TableName)} WHERE {Name(violation.PolicyForeignKeyColumn)} = @policyId;",
            [new("@policyId", policyId)],
            cancellationToken);
    }

    private static bool ShouldClearViolations(SoftwarePolicyEntry existing, SoftwarePolicyEntry updated)
    {
        if (updated.Classification != SoftwarePolicyClassification.Blacklist || !updated.Enabled)
        {
            return existing.Classification == SoftwarePolicyClassification.Blacklist && existing.Enabled;
        }

        return !string.Equals(existing.ProductName, updated.ProductName, StringComparison.Ordinal) ||
               !string.Equals(existing.Publisher, updated.Publisher, StringComparison.Ordinal) ||
               !string.Equals(existing.VersionPattern, updated.VersionPattern, StringComparison.Ordinal);
    }

    private async Task<List<SoftwarePolicyEntry>> ListEnabledPoliciesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken) =>
        await ListPoliciesAsync(connection, transaction, enabledOnly: true, cancellationToken);

    private async Task<List<SoftwarePolicyEntry>> ListPoliciesAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        bool enabledOnly,
        CancellationToken cancellationToken)
    {
        var table = options.SoftwarePolicyTable;
        var sql = $"""
            SELECT {PolicySelectList()}
            FROM {Name(options.SchemaName, table.TableName)}
            {(enabledOnly ? $"WHERE {Name(table.EnabledColumn)} = 1" : string.Empty)}
            ORDER BY {Name(table.ProductNameColumn)}, {Name(table.PrimaryKeyColumn)};
            """;
        await using var command = transaction is null
            ? new SqlCommand(sql, connection)
            : new SqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var policies = new List<SoftwarePolicyEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var policy = ReadPolicy(reader);
            if (policy is not null)
            {
                policies.Add(policy);
            }
        }

        return policies;
    }

    private async Task<SoftwarePolicyEntry?> GetPolicyAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        long id,
        CancellationToken cancellationToken)
    {
        var table = options.SoftwarePolicyTable;
        var sql = $"""
            SELECT {PolicySelectList()}
            FROM {Name(options.SchemaName, table.TableName)}
            WHERE {Name(table.PrimaryKeyColumn)} = @id;
            """;
        await using var command = transaction is null
            ? new SqlCommand(sql, connection)
            : new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@id", id));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPolicy(reader) : null;
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

    internal string BuildListPoliciesSql()
    {
        var table = options.SoftwarePolicyTable;
        return $"""
            SELECT
                COUNT(*) OVER() AS total_count,
                {PolicySelectList()}
            FROM {Name(options.SchemaName, table.TableName)}
            WHERE (@search IS NULL
                OR {Name(table.ProductNameColumn)} LIKE @search
                OR {Name(table.VersionPatternColumn)} LIKE @search
                OR {Name(table.PublisherColumn)} LIKE @search)
              AND (@classification IS NULL OR {Name(table.ClassificationColumn)} = @classification)
            ORDER BY {Name(table.ProductNameColumn)}, {Name(table.PrimaryKeyColumn)}
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            """;
    }

    internal string BuildListViolationsSql()
    {
        var violation = options.SoftwareViolationTable;
        var pc = options.PcTable;
        var policy = options.SoftwarePolicyTable;
        return $"""
            SELECT
                COUNT(*) OVER() AS total_count,
                v.{Name(violation.PrimaryKeyColumn)} AS id,
                p.{Name(pc.DeviceCodeColumn)} AS deviceCode,
                p.{Name(pc.HostNameColumn)} AS hostName,
                v.{Name(violation.DisplayNameColumn)} AS softwareName,
                v.{Name(violation.DisplayVersionColumn)} AS softwareVersion,
                v.{Name(violation.PublisherColumn)} AS publisher,
                v.{Name(violation.PolicyForeignKeyColumn)} AS policyId,
                pol.{Name(policy.ProductNameColumn)} AS policyProductName,
                pol.{Name(policy.ClassificationColumn)} AS classification,
                v.{Name(violation.DetectedAtUtcColumn)} AS detectedAtUtc,
                v.{Name(violation.LastSeenAtUtcColumn)} AS lastSeenAtUtc
            FROM {Name(options.SchemaName, violation.TableName)} AS v
            INNER JOIN {Name(options.SchemaName, pc.TableName)} AS p
                ON p.{Name(pc.PrimaryKeyColumn)} = v.{Name(violation.PcForeignKeyColumn)}
            INNER JOIN {Name(options.SchemaName, policy.TableName)} AS pol
                ON pol.{Name(policy.PrimaryKeyColumn)} = v.{Name(violation.PolicyForeignKeyColumn)}
            WHERE (@search IS NULL
                OR p.{Name(pc.DeviceCodeColumn)} LIKE @search
                OR p.{Name(pc.HostNameColumn)} LIKE @search
                OR v.{Name(violation.DisplayNameColumn)} LIKE @search)
              AND (@since IS NULL OR v.{Name(violation.DetectedAtUtcColumn)} >= @since)
            ORDER BY v.{Name(violation.LastSeenAtUtcColumn)} DESC, v.{Name(violation.PrimaryKeyColumn)} DESC
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            """;
    }

    private string PolicySelectList(string? qualifier = null)
    {
        var table = options.SoftwarePolicyTable;
        string Column(string name) => qualifier is null ? Name(name) : $"{qualifier}.{Name(name)}";
        return $"""
            {Column(table.PrimaryKeyColumn)}, {Column(table.ProductNameColumn)}, {Column(table.PublisherColumn)},
            {Column(table.VersionPatternColumn)}, {Column(table.ClassificationColumn)}, {Column(table.NotesColumn)},
            {Column(table.EnabledColumn)}, {Column(table.UpdatedAtUtcColumn)}
            """;
    }

    private SoftwarePolicyEntry? ReadPolicy(SqlDataReader reader)
    {
        var table = options.SoftwarePolicyTable;
        if (!SoftwarePolicyClassificationNames.TryParse(reader.GetString(reader.GetOrdinal(table.ClassificationColumn)), out var classification))
        {
            return null;
        }

        return new SoftwarePolicyEntry(
            reader.GetInt64(reader.GetOrdinal(table.PrimaryKeyColumn)),
            reader.GetString(reader.GetOrdinal(table.ProductNameColumn)),
            ReadNullableString(reader, table.PublisherColumn),
            ReadNullableString(reader, table.VersionPatternColumn),
            classification,
            ReadNullableString(reader, table.NotesColumn),
            reader.GetBoolean(reader.GetOrdinal(table.EnabledColumn)),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal(table.UpdatedAtUtcColumn)));
    }

    private static void AddPolicyWriteParameters(SqlCommand command, SoftwarePolicyWriteRequest request, DateTimeOffset updatedAt)
    {
        command.Parameters.Add(new SqlParameter("@classification", SoftwarePolicyClassificationNames.ToStorage(request.Classification!.Value)));
        command.Parameters.Add(new SqlParameter("@productName", Truncate(request.ProductName.Trim(), 256)));
        command.Parameters.Add(new SqlParameter("@publisher", DbValue(Truncate(NullIfWhiteSpace(request.Publisher), 256))));
        command.Parameters.Add(new SqlParameter("@versionPattern", DbValue(Truncate(NullIfWhiteSpace(request.VersionPattern), 64))));
        command.Parameters.Add(new SqlParameter("@notes", DbValue(Truncate(NullIfWhiteSpace(request.Notes), 1024))));
        command.Parameters.Add(new SqlParameter("@enabled", request.Enabled));
        command.Parameters.Add(new SqlParameter("@updatedAt", updatedAt));
    }

    private async Task<List<InstalledSoftwareEntry>> ReadInstalledSoftwareAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        long pcId,
        string? classification,
        CancellationToken cancellationToken)
    {
        var software = options.InstalledSoftwareTable;
        var sql = $"""
            SELECT {Name(software.DisplayNameColumn)}, {Name(software.DisplayVersionColumn)}, {Name(software.PublisherColumn)},
                   {Name(software.InstallLocationColumn)}, {Name(software.DiscoveryScopeColumn)}, {Name(software.DiscoverySourceColumn)},
                   {Name(software.ClassificationColumn)}
            FROM {Name(options.SchemaName, software.TableName)}
            WHERE {Name(software.PcForeignKeyColumn)} = @pcId
              AND (@classification IS NULL OR {Name(software.ClassificationColumn)} = @classification)
            ORDER BY {Name(software.DisplayNameColumn)}, {Name(software.PrimaryKeyColumn)};
            """;
        await using var command = transaction is null
            ? new SqlCommand(sql, connection)
            : new SqlCommand(sql, connection, transaction);
        command.Parameters.Add(new SqlParameter("@pcId", pcId));
        command.Parameters.Add(new SqlParameter("@classification", DbValue(classification)));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<InstalledSoftwareEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new InstalledSoftwareEntry(
                reader.GetString(reader.GetOrdinal(software.DisplayNameColumn)),
                ReadNullableString(reader, software.DisplayVersionColumn),
                ReadNullableString(reader, software.PublisherColumn),
                ReadNullableString(reader, software.InstallLocationColumn),
                reader.GetString(reader.GetOrdinal(software.DiscoveryScopeColumn)),
                reader.GetString(reader.GetOrdinal(software.DiscoverySourceColumn)),
                ReadClassification(reader, software.ClassificationColumn)));
        }

        return entries;
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
                {Name(table.AgentVersionColumn)} = CASE
                    WHEN @inventoryAt IS NOT NULL
                     AND {Name(table.LastHeartbeatUtcColumn)} > @inventoryAt
                    THEN {Name(table.AgentVersionColumn)}
                    ELSE @agentVersion
                END,
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

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    internal static string? ToContainsPattern(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var escaped = search.Trim()
            .Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    private static string ReadClassification(SqlDataReader reader, string column)
    {
        var stored = ReadNullableString(reader, column);
        return SoftwarePolicyClassificationNames.TryParseInstalledSoftware(stored, out var classification)
            ? classification
            : SoftwarePolicyClassificationNames.Unclassified;
    }

    private static string? ReadNullableString(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    internal static string Name(params string[] parts) =>
        string.Join('.', parts.Select(part => $"[{part.Replace("]", "]]", StringComparison.Ordinal)}]"));
}
