using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.SqlClient;
using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

internal sealed partial class SqlServerDataContext
{
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
        return await InsertPolicyAsync(connection, transaction: null, request, cancellationToken);
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

        var updated = await UpdatePolicyRowAsync(connection, transaction, id, existing, request, cancellationToken);
        if (updated is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
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

    public async Task<SoftwarePolicyEntry> UpsertSoftwareClassificationAsync(
        string productName,
        SoftwareClassificationWriteRequest request,
        CancellationToken cancellationToken)
    {
        var saved = await UpsertSoftwareClassificationsAsync([(productName, request)], cancellationToken);
        return saved[0];
    }

    public async Task<IReadOnlyList<SoftwarePolicyEntry>> UpsertSoftwareClassificationsAsync(
        IReadOnlyList<(string Name, SoftwareClassificationWriteRequest Request)> items,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);

        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var saved = new List<SoftwarePolicyEntry>(items.Count);
        try
        {
            foreach (var (productName, request) in items)
            {
                saved.Add(await UpsertSoftwareClassificationInTransactionAsync(
                    connection, transaction, productName, request, cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return saved;
    }

    private async Task<SoftwarePolicyEntry> UpsertSoftwareClassificationInTransactionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string productName,
        SoftwareClassificationWriteRequest request,
        CancellationToken cancellationToken)
    {
        LicenseSourceNames.TryParse(request.DefaultLicenseSource, out var defaultLicenseSource);
        var write = new SoftwarePolicyWriteRequest(
            productName.Trim(),
            request.Publisher,
            VersionPattern: null,
            request.Classification,
            Notes: null,
            Enabled: true,
            defaultLicenseSource);

        var existing = await FindEnabledExactNamePolicyAsync(connection, transaction, write.ProductName, cancellationToken);
        SoftwarePolicyEntry saved;
        if (existing is null)
        {
            saved = await InsertPolicyAsync(connection, transaction, write, cancellationToken);
        }
        else
        {
            write = write with { Notes = existing.Notes, VersionPattern = existing.VersionPattern };
            saved = await UpdatePolicyRowAsync(connection, transaction, existing.Id, existing, write, cancellationToken)
                ?? throw new InvalidOperationException("The software classification update did not return a row.");
        }

        await RecolorInstalledSoftwareAsync(connection, transaction, write.ProductName, cancellationToken);
        return saved;
    }

    public async Task<bool> SetDeviceSoftwareLicenseSourceAsync(
        string deviceCode,
        string softwareName,
        string? licenseSource,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var pcId = await FindPcIdAsync(connection, transaction, deviceCode, cancellationToken);
        if (pcId is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var name = Truncate(softwareName.Trim(), 256)!;
        if (licenseSource is null)
        {
            await ExecuteAsync(
                connection,
                transaction,
                BuildDeleteSoftwareLicenseSql(),
                [new("@pcId", pcId.Value), new("@name", name)],
                cancellationToken);
        }
        else
        {
            await ExecuteAsync(
                connection,
                transaction,
                BuildUpsertSoftwareLicenseSql(),
                [
                    new("@pcId", pcId.Value),
                    new("@name", name),
                    new("@licenseSource", licenseSource),
                    new("@updatedAt", DateTimeOffset.UtcNow)
                ],
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
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
        SqlTransaction? transaction,
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

    private async Task<SoftwarePolicyEntry> InsertPolicyAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        SoftwarePolicyWriteRequest request,
        CancellationToken cancellationToken)
    {
        var updatedAt = DateTimeOffset.UtcNow;
        await using var command = CreateCommand(connection, transaction, BuildInsertPolicySql());
        AddPolicyWriteParameters(command, request, updatedAt);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The software policy insert did not return a row.");
        }

        return ReadPolicy(reader) ?? throw new InvalidOperationException("The software policy insert returned an invalid classification.");
    }

    private async Task<SoftwarePolicyEntry?> UpdatePolicyRowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long id,
        SoftwarePolicyEntry existing,
        SoftwarePolicyWriteRequest request,
        CancellationToken cancellationToken)
    {
        var updatedAt = DateTimeOffset.UtcNow;
        await using var command = new SqlCommand(BuildUpdatePolicySql(), connection, transaction);
        command.Parameters.Add(new SqlParameter("@id", id));
        AddPolicyWriteParameters(command, request, updatedAt);
        SoftwarePolicyEntry? updated;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            updated = ReadPolicy(reader);
        }

        if (updated is null)
        {
            throw new InvalidOperationException("The software policy update returned an invalid classification.");
        }

        if (ShouldClearViolations(existing, updated))
        {
            await DeleteViolationsForPolicyAsync(connection, transaction, id, cancellationToken);
        }

        return updated;
    }

    private async Task<SoftwarePolicyEntry?> FindEnabledExactNamePolicyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string productName,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(BuildFindEnabledExactNamePolicySql(), connection, transaction);
        command.Parameters.Add(new SqlParameter("@productName", productName));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPolicy(reader) : null;
    }

    private async Task RecolorInstalledSoftwareAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string productName,
        CancellationToken cancellationToken)
    {
        var policies = await ListEnabledPoliciesAsync(connection, transaction, cancellationToken);
        var software = options.InstalledSoftwareTable;
        var sql = $"""
            SELECT {Name(software.PrimaryKeyColumn)}, {Name(software.DisplayNameColumn)}, {Name(software.DisplayVersionColumn)},
                   {Name(software.PublisherColumn)}, {Name(software.InstallLocationColumn)},
                   {Name(software.DiscoveryScopeColumn)}, {Name(software.DiscoverySourceColumn)}
            FROM {Name(options.SchemaName, software.TableName)}
            WHERE {Name(software.DisplayNameColumn)} = @name;
            """;
        var rows = new List<(long Id, InstalledSoftwareEntry Entry)>();
        await using (var command = new SqlCommand(sql, connection, transaction))
        {
            command.Parameters.Add(new SqlParameter("@name", productName));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((
                    reader.GetInt64(reader.GetOrdinal(software.PrimaryKeyColumn)),
                    new InstalledSoftwareEntry(
                        reader.GetString(reader.GetOrdinal(software.DisplayNameColumn)),
                        ReadNullableString(reader, software.DisplayVersionColumn),
                        ReadNullableString(reader, software.PublisherColumn),
                        ReadNullableString(reader, software.InstallLocationColumn),
                        reader.GetString(reader.GetOrdinal(software.DiscoveryScopeColumn)),
                        reader.GetString(reader.GetOrdinal(software.DiscoverySourceColumn)))));
            }
        }

        foreach (var (id, entry) in rows)
        {
            var match = SoftwarePolicyMatcher.Match(entry, policies);
            await ExecuteAsync(
                connection,
                transaction,
                BuildUpdateInstalledClassificationSql(),
                [new("@id", id), new("@classification", Truncate(match.StoredClassification, 32)!)],
                cancellationToken);
        }
    }

    internal string BuildInsertPolicySql()
    {
        var table = options.SoftwarePolicyTable;
        return $"""
            INSERT INTO {Name(options.SchemaName, table.TableName)}
            ({Name(table.ClassificationColumn)}, {Name(table.ProductNameColumn)}, {Name(table.PublisherColumn)},
             {Name(table.VersionPatternColumn)}, {Name(table.NotesColumn)}, {Name(table.EnabledColumn)},
             {Name(table.UpdatedAtUtcColumn)}, {Name(table.DefaultLicenseSourceColumn)})
            OUTPUT {PolicySelectList("INSERTED")}
            VALUES (@classification, @productName, @publisher, @versionPattern, @notes, @enabled, @updatedAt, @defaultLicenseSource);
            """;
    }

    internal string BuildUpdatePolicySql()
    {
        var table = options.SoftwarePolicyTable;
        return $"""
            UPDATE {Name(options.SchemaName, table.TableName)}
            SET {Name(table.ClassificationColumn)} = @classification,
                {Name(table.ProductNameColumn)} = @productName,
                {Name(table.PublisherColumn)} = @publisher,
                {Name(table.VersionPatternColumn)} = @versionPattern,
                {Name(table.NotesColumn)} = @notes,
                {Name(table.EnabledColumn)} = @enabled,
                {Name(table.UpdatedAtUtcColumn)} = @updatedAt,
                {Name(table.DefaultLicenseSourceColumn)} = @defaultLicenseSource
            OUTPUT {PolicySelectList("INSERTED")}
            WHERE {Name(table.PrimaryKeyColumn)} = @id;
            """;
    }

    internal string BuildFindEnabledExactNamePolicySql()
    {
        var table = options.SoftwarePolicyTable;
        return $"""
            SELECT {PolicySelectList()}
            FROM {Name(options.SchemaName, table.TableName)}
            WHERE {Name(table.EnabledColumn)} = 1
              AND {Name(table.ProductNameColumn)} = @productName
            ORDER BY {Name(table.PrimaryKeyColumn)};
            """;
    }

    internal string BuildUpdateInstalledClassificationSql()
    {
        var software = options.InstalledSoftwareTable;
        return $"""
            UPDATE {Name(options.SchemaName, software.TableName)}
            SET {Name(software.ClassificationColumn)} = @classification
            WHERE {Name(software.PrimaryKeyColumn)} = @id;
            """;
    }

    internal string BuildUpsertSoftwareLicenseSql()
    {
        var license = options.SoftwareLicenseTable;
        return $"""
            UPDATE {Name(options.SchemaName, license.TableName)}
            SET {Name(license.LicenseSourceColumn)} = @licenseSource,
                {Name(license.UpdatedAtUtcColumn)} = @updatedAt
            WHERE {Name(license.PcForeignKeyColumn)} = @pcId
              AND {Name(license.SoftwareNameColumn)} = @name;
            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO {Name(options.SchemaName, license.TableName)}
                ({Name(license.PcForeignKeyColumn)}, {Name(license.SoftwareNameColumn)},
                 {Name(license.LicenseSourceColumn)}, {Name(license.UpdatedAtUtcColumn)})
                VALUES (@pcId, @name, @licenseSource, @updatedAt);
            END;
            """;
    }

    internal string BuildDeleteSoftwareLicenseSql()
    {
        var license = options.SoftwareLicenseTable;
        return $"""
            DELETE FROM {Name(options.SchemaName, license.TableName)}
            WHERE {Name(license.PcForeignKeyColumn)} = @pcId
              AND {Name(license.SoftwareNameColumn)} = @name;
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
            {Column(table.EnabledColumn)}, {Column(table.UpdatedAtUtcColumn)}, {Column(table.DefaultLicenseSourceColumn)}
            """;
    }

    private SoftwarePolicyEntry? ReadPolicy(SqlDataReader reader)
    {
        var table = options.SoftwarePolicyTable;
        if (!SoftwarePolicyClassificationNames.TryParse(reader.GetString(reader.GetOrdinal(table.ClassificationColumn)), out var classification))
        {
            return null;
        }

        LicenseSourceNames.TryParse(ReadNullableString(reader, table.DefaultLicenseSourceColumn), out var defaultLicenseSource);
        return new SoftwarePolicyEntry(
            reader.GetInt64(reader.GetOrdinal(table.PrimaryKeyColumn)),
            reader.GetString(reader.GetOrdinal(table.ProductNameColumn)),
            ReadNullableString(reader, table.PublisherColumn),
            ReadNullableString(reader, table.VersionPatternColumn),
            classification,
            ReadNullableString(reader, table.NotesColumn),
            reader.GetBoolean(reader.GetOrdinal(table.EnabledColumn)),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal(table.UpdatedAtUtcColumn)),
            LicenseSourceNames.ForManagedPolicy(classification, defaultLicenseSource));
    }

    private static void AddPolicyWriteParameters(SqlCommand command, SoftwarePolicyWriteRequest request, DateTimeOffset updatedAt)
    {
        LicenseSourceNames.TryParse(request.DefaultLicenseSource, out var defaultLicenseSource);
        command.Parameters.Add(new SqlParameter("@classification", SoftwarePolicyClassificationNames.ToStorage(request.Classification!.Value)));
        command.Parameters.Add(new SqlParameter("@productName", Truncate(request.ProductName.Trim(), 256)));
        command.Parameters.Add(new SqlParameter("@publisher", DbValue(Truncate(NullIfWhiteSpace(request.Publisher), 256))));
        command.Parameters.Add(new SqlParameter("@versionPattern", DbValue(Truncate(NullIfWhiteSpace(request.VersionPattern), 64))));
        command.Parameters.Add(new SqlParameter("@notes", DbValue(Truncate(NullIfWhiteSpace(request.Notes), 1024))));
        command.Parameters.Add(new SqlParameter("@enabled", request.Enabled));
        command.Parameters.Add(new SqlParameter("@updatedAt", updatedAt));
        command.Parameters.Add(new SqlParameter(
            "@defaultLicenseSource",
            DbValue(LicenseSourceNames.ForManagedPolicy(request.Classification.Value, defaultLicenseSource))));
    }

}
