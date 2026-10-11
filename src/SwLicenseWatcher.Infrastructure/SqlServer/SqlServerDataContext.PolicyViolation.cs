using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.Relational;

public sealed partial class RelationalDataContext
{
    public async Task<(int TotalCount, List<SoftwarePolicyEntry> Items)> ListPoliciesAsync(
        int skip,
        int take,
        string? search,
        string? classification,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, null, BuildListPoliciesSql());
        Bind(command, ("@search", DbValue(ToContainsPattern(search))));
        Bind(command, ("@classification", DbValue(classification)));
        Bind(command, ("@skip", skip));
        Bind(command, ("@take", take));
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
                totalCount = ReadInt32(reader, "total_count");
            }

            items.Add(policy);
        }

        return (totalCount, items);
    }

    public async Task<SoftwarePolicyEntry?> GetPolicyAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await GetPolicyAsync(connection, transaction: null, id, cancellationToken);
    }

    public async Task<SoftwarePolicyEntry> CreatePolicyAsync(SoftwarePolicyWriteRequest request, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await InsertPolicyAsync(connection, transaction: null, request, cancellationToken);
    }

    public async Task<SoftwarePolicyEntry?> UpdatePolicyAsync(long id, SoftwarePolicyWriteRequest request, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

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
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var table = options.SoftwarePolicyTable;
        var sql = $"DELETE FROM {Name(options.SchemaName, table.TableName)} WHERE {Name(table.PrimaryKeyColumn)} = @id;";
        await using var command = CreateCommand(connection, null, sql);
        Bind(command, ("@id", id));
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

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
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
        DbConnection connection,
        DbTransaction transaction,
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
            Notes: request.Notes,
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
            write = write with
            {
                Notes = request.Notes is null ? existing.Notes : NullIfWhiteSpace(request.Notes),
                VersionPattern = existing.VersionPattern
            };
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
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

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
                [("@pcId", pcId.Value), ("@name", name)],
                cancellationToken);
        }
        else
        {
            await ExecuteAsync(
                connection,
                transaction,
                BuildUpsertSoftwareLicenseSql(),
                [
                    ("@pcId", pcId.Value),
                    ("@name", name),
                    ("@licenseSource", licenseSource),
                    ("@updatedAt", DateTimeOffset.UtcNow)
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
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, null, BuildListViolationsSql());
        Bind(command, ("@search", DbValue(ToContainsPattern(search))));
        Bind(command, ("@since", DbValue(since)));
        Bind(command, ("@skip", skip));
        Bind(command, ("@take", take));
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
                totalCount = ReadInt32(reader, "total_count");
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
                ReadDateTimeOffset(reader, reader.GetOrdinal("detectedAtUtc")),
                ReadDateTimeOffset(reader, reader.GetOrdinal("lastSeenAtUtc"))));
        }

        return (totalCount, items);
    }

    private async Task DeleteViolationsForPolicyAsync(
        DbConnection connection,
        DbTransaction transaction,
        long policyId,
        CancellationToken cancellationToken)
    {
        var violation = options.SoftwareViolationTable;
        await ExecuteAsync(connection, transaction,
            $"DELETE FROM {Name(options.SchemaName, violation.TableName)} WHERE {Name(violation.PolicyForeignKeyColumn)} = @policyId;",
            [("@policyId", policyId)],
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
        DbConnection connection,
        DbTransaction? transaction,
        CancellationToken cancellationToken) =>
        await ListPoliciesAsync(connection, transaction, enabledOnly: true, cancellationToken);

    private async Task<List<SoftwarePolicyEntry>> ListPoliciesAsync(
        DbConnection connection,
        DbTransaction? transaction,
        bool enabledOnly,
        CancellationToken cancellationToken)
    {
        var table = options.SoftwarePolicyTable;
        var sql = $"""
            SELECT {PolicySelectList()}
            FROM {Name(options.SchemaName, table.TableName)}
            {(enabledOnly ? $"WHERE {Name(table.EnabledColumn)} = {TrueSql}" : string.Empty)}
            ORDER BY {Name(table.ProductNameColumn)}, {Name(table.PrimaryKeyColumn)};
            """;
        await using var command = transaction is null
            ? CreateCommand(connection, null, sql)
            : CreateCommand(connection, transaction, sql);
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
        DbConnection connection,
        DbTransaction? transaction,
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
            ? CreateCommand(connection, null, sql)
            : CreateCommand(connection, transaction, sql);
        Bind(command, ("@id", id));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPolicy(reader) : null;
    }

    private async Task<SoftwarePolicyEntry> InsertPolicyAsync(
        DbConnection connection,
        DbTransaction? transaction,
        SoftwarePolicyWriteRequest request,
        CancellationToken cancellationToken)
    {
        var updatedAt = DateTimeOffset.UtcNow;
        await using (var command = CreateCommand(connection, transaction, BuildInsertPolicySql()))
        {
            AddPolicyWriteParameters(command, request, updatedAt);
            if (dialect.DmlReturnsRows)
            {
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidOperationException("The software policy insert did not return a row.");
                }

                return ReadPolicy(reader) ?? throw new InvalidOperationException("The software policy insert returned an invalid classification.");
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var table = options.SoftwarePolicyTable;
        var sql = $"""
            SELECT {PolicySelectList()}
            FROM {Name(options.SchemaName, table.TableName)}
            WHERE {Name(table.PrimaryKeyColumn)} = LAST_INSERT_ID();
            """;
        await using var inserted = CreateCommand(connection, transaction, sql);
        await using var insertedReader = await inserted.ExecuteReaderAsync(cancellationToken);
        if (!await insertedReader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("The software policy insert did not return a row.");
        }

        return ReadPolicy(insertedReader) ?? throw new InvalidOperationException("The software policy insert returned an invalid classification.");
    }

    private async Task<SoftwarePolicyEntry?> UpdatePolicyRowAsync(
        DbConnection connection,
        DbTransaction transaction,
        long id,
        SoftwarePolicyEntry existing,
        SoftwarePolicyWriteRequest request,
        CancellationToken cancellationToken)
    {
        var updatedAt = DateTimeOffset.UtcNow;
        await using var command = CreateCommand(connection, transaction, BuildUpdatePolicySql());
        Bind(command, ("@id", id));
        AddPolicyWriteParameters(command, request, updatedAt);
        SoftwarePolicyEntry? updated;
        if (!dialect.DmlReturnsRows)
        {
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                return null;
            }

            updated = await GetPolicyAsync(connection, transaction, id, cancellationToken);
        }
        else
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
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
        DbConnection connection,
        DbTransaction transaction,
        string productName,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, BuildFindEnabledExactNamePolicySql());
        Bind(command, ("@productName", productName));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPolicy(reader) : null;
    }

    private async Task RecolorInstalledSoftwareAsync(
        DbConnection connection,
        DbTransaction transaction,
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
        await using (var command = CreateCommand(connection, transaction, sql))
        {
            Bind(command, ("@name", productName));
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
                [("@id", id), ("@classification", Truncate(match.StoredClassification, 32)!)],
                cancellationToken);
        }
    }

    internal string BuildInsertPolicySql()
    {
        var table = options.SoftwarePolicyTable;
        var insert = $"""
            INSERT INTO {Name(options.SchemaName, table.TableName)}
            ({Name(table.ClassificationColumn)}, {Name(table.ProductNameColumn)}, {Name(table.PublisherColumn)},
             {Name(table.VersionPatternColumn)}, {Name(table.NotesColumn)}, {Name(table.EnabledColumn)},
             {Name(table.UpdatedAtUtcColumn)}, {Name(table.DefaultLicenseSourceColumn)})
            """;
        var values = "VALUES (@classification, @productName, @publisher, @versionPattern, @notes, @enabled, @updatedAt, @defaultLicenseSource)";
        return dialect.Provider switch
        {
            StorageProvider.PostgreSql => $"{insert}\n{values}\nRETURNING {PolicySelectList()};",
            StorageProvider.MySql => $"{insert}\n{values};",
            _ => $"{insert}\nOUTPUT {PolicySelectList("INSERTED")}\n{values};"
        };
    }

    internal string BuildUpdatePolicySql()
    {
        var table = options.SoftwarePolicyTable;
        var update = $"""
            UPDATE {Name(options.SchemaName, table.TableName)}
            SET {Name(table.ClassificationColumn)} = @classification,
                {Name(table.ProductNameColumn)} = @productName,
                {Name(table.PublisherColumn)} = @publisher,
                {Name(table.VersionPatternColumn)} = @versionPattern,
                {Name(table.NotesColumn)} = @notes,
                {Name(table.EnabledColumn)} = @enabled,
                {Name(table.UpdatedAtUtcColumn)} = @updatedAt,
                {Name(table.DefaultLicenseSourceColumn)} = @defaultLicenseSource
            """;
        var where = $"WHERE {Name(table.PrimaryKeyColumn)} = @id";
        return dialect.Provider switch
        {
            StorageProvider.PostgreSql => $"{update}\n{where}\nRETURNING {PolicySelectList()};",
            StorageProvider.MySql => $"{update}\n{where};",
            _ => $"{update}\nOUTPUT {PolicySelectList("INSERTED")}\n{where};"
        };
    }

    internal string BuildFindEnabledExactNamePolicySql()
    {
        var table = options.SoftwarePolicyTable;
        return $"""
            SELECT {PolicySelectList()}
            FROM {Name(options.SchemaName, table.TableName)}
            WHERE {Name(table.EnabledColumn)} = {TrueSql}
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
        var table = Name(options.SchemaName, license.TableName);
        if (dialect.Provider == StorageProvider.PostgreSql)
        {
            return $"""
                INSERT INTO {table}
                ({Name(license.PcForeignKeyColumn)}, {Name(license.SoftwareNameColumn)},
                 {Name(license.LicenseSourceColumn)}, {Name(license.UpdatedAtUtcColumn)})
                VALUES (@pcId, @name, @licenseSource, @updatedAt)
                ON CONFLICT ({Name(license.PcForeignKeyColumn)}, {Name(license.SoftwareNameColumn)}) DO UPDATE SET
                    {Name(license.LicenseSourceColumn)} = EXCLUDED.{Name(license.LicenseSourceColumn)},
                    {Name(license.UpdatedAtUtcColumn)} = EXCLUDED.{Name(license.UpdatedAtUtcColumn)};
                """;
        }

        if (dialect.Provider == StorageProvider.MySql)
        {
            return $"""
                INSERT INTO {table}
                ({Name(license.PcForeignKeyColumn)}, {Name(license.SoftwareNameColumn)},
                 {Name(license.LicenseSourceColumn)}, {Name(license.UpdatedAtUtcColumn)})
                VALUES (@pcId, @name, @licenseSource, @updatedAt)
                ON DUPLICATE KEY UPDATE
                    {Name(license.LicenseSourceColumn)} = VALUES({license.LicenseSourceColumn}),
                    {Name(license.UpdatedAtUtcColumn)} = VALUES({license.UpdatedAtUtcColumn});
                """;
        }

        return $"""
            UPDATE {table}
            SET {Name(license.LicenseSourceColumn)} = @licenseSource,
                {Name(license.UpdatedAtUtcColumn)} = @updatedAt
            WHERE {Name(license.PcForeignKeyColumn)} = @pcId
              AND {Name(license.SoftwareNameColumn)} = @name;
            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO {table}
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
                OR {Name(table.ProductNameColumn)} LIKE @search{LikeEscape}
                OR {Name(table.VersionPatternColumn)} LIKE @search{LikeEscape}
                OR {Name(table.PublisherColumn)} LIKE @search{LikeEscape})
              AND (@classification IS NULL OR {Name(table.ClassificationColumn)} = @classification)
            ORDER BY {Name(table.ProductNameColumn)}, {Name(table.PrimaryKeyColumn)}
            {Pagination};
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
                p.{Name(pc.DeviceCodeColumn)} AS {Name("deviceCode")},
                {DisplayHostNameSql("p")} AS {Name("hostName")},
                v.{Name(violation.DisplayNameColumn)} AS {Name("softwareName")},
                v.{Name(violation.DisplayVersionColumn)} AS {Name("softwareVersion")},
                v.{Name(violation.PublisherColumn)} AS publisher,
                v.{Name(violation.PolicyForeignKeyColumn)} AS {Name("policyId")},
                pol.{Name(policy.ProductNameColumn)} AS {Name("policyProductName")},
                pol.{Name(policy.ClassificationColumn)} AS classification,
                v.{Name(violation.DetectedAtUtcColumn)} AS {Name("detectedAtUtc")},
                v.{Name(violation.LastSeenAtUtcColumn)} AS {Name("lastSeenAtUtc")}
            FROM {Name(options.SchemaName, violation.TableName)} AS v
            INNER JOIN {Name(options.SchemaName, pc.TableName)} AS p
                ON p.{Name(pc.PrimaryKeyColumn)} = v.{Name(violation.PcForeignKeyColumn)}
            INNER JOIN {Name(options.SchemaName, policy.TableName)} AS pol
                ON pol.{Name(policy.PrimaryKeyColumn)} = v.{Name(violation.PolicyForeignKeyColumn)}
            WHERE (@search IS NULL
                OR {HostNameSearchSql("p")}
                OR v.{Name(violation.DisplayNameColumn)} LIKE @search{LikeEscape})
              AND (@since IS NULL OR v.{Name(violation.DetectedAtUtcColumn)} >= @since)
            ORDER BY v.{Name(violation.LastSeenAtUtcColumn)} DESC, v.{Name(violation.PrimaryKeyColumn)} DESC
            {Pagination};
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

    private SoftwarePolicyEntry? ReadPolicy(DbDataReader reader)
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
            ReadDateTimeOffset(reader, reader.GetOrdinal(table.UpdatedAtUtcColumn)),
            LicenseSourceNames.ForManagedPolicy(classification, defaultLicenseSource));
    }

    private void AddPolicyWriteParameters(DbCommand command, SoftwarePolicyWriteRequest request, DateTimeOffset updatedAt)
    {
        LicenseSourceNames.TryParse(request.DefaultLicenseSource, out var defaultLicenseSource);
        Bind(command, ("@classification", SoftwarePolicyClassificationNames.ToStorage(request.Classification!.Value)));
        Bind(command, ("@productName", Truncate(request.ProductName.Trim(), 256)));
        Bind(command, ("@publisher", DbValue(Truncate(NullIfWhiteSpace(request.Publisher), 256))));
        Bind(command, ("@versionPattern", DbValue(Truncate(NullIfWhiteSpace(request.VersionPattern), 64))));
        Bind(command, ("@notes", DbValue(Truncate(NullIfWhiteSpace(request.Notes), 1024))));
        Bind(command, ("@enabled", request.Enabled));
        Bind(command, ("@updatedAt", updatedAt));
        Bind(command, (
            "@defaultLicenseSource",
            DbValue(LicenseSourceNames.ForManagedPolicy(request.Classification.Value, defaultLicenseSource))));
    }

}
