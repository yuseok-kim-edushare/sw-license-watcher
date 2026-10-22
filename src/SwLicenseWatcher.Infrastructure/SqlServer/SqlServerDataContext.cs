using System.Data;
using System.Data.Common;
using System.Globalization;
using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.Relational;

public sealed partial class RelationalDataContext
{
    private readonly SqlServerStorageOptions options;
    private readonly IRelationalDialect dialect;

    public RelationalDataContext(SqlServerStorageOptions options)
        : this(options, SqlServerDialect.Instance)
    {
    }

    public RelationalDataContext(SqlServerStorageOptions options, IRelationalDialect dialect)
    {
        this.options = options;
        this.dialect = dialect;
    }

    private string Empty => dialect.EmptyStringLiteral;

    private string Pagination => dialect.PaginationClause;

    private string LikeEscape => dialect.LikeEscapeClause;

    private string LockFrom => dialect.LockFromClause;

    private string LockSuffix => dialect.LockSuffix;

    private string TrueSql => dialect.TrueLiteral;

    private string Lit(string value) => dialect.UnicodeLiteral(value);

    private string TakePrefix(string takeExpression) => dialect.TakePrefix(takeExpression);

    private string TakeSuffix(string takeExpression) => dialect.TakeSuffix(takeExpression);

    private DbConnection CreateConnection() => dialect.CreateConnection(options.ConnectionString);

    private static DbCommand CreateCommand(DbConnection connection, DbTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

        return command;
    }

    private void Bind(DbCommand command, params (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            if (value is TypedNull typed)
            {
                parameter.Value = DBNull.Value;
                dialect.AssignNullParameter(parameter, typed.Timestamp);
            }
            else
            {
                parameter.Value = dialect.NormalizeParameterValue(value is null ? DBNull.Value : value);
            }

            command.Parameters.Add(parameter);
        }
    }

    private async Task<long?> FindPcIdAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string deviceCode,
        CancellationToken cancellationToken)
    {
        var table = options.PcTable;
        var sql = $"""
            SELECT {TakePrefix("1")} {Name(table.PrimaryKeyColumn)}
            FROM {Name(options.SchemaName, table.TableName)} AS chosen
            WHERE {PcLookupPredicate("chosen")}
            ORDER BY {PreferredPcOrder("chosen")}
            {TakeSuffix("1")};
            """;
        await using var command = CreateCommand(connection, transaction, sql);
        Bind(command, ("@deviceCode", deviceCode));
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private async Task<List<InstalledSoftwareEntry>> ReadInstalledSoftwareAsync(
        DbConnection connection,
        DbTransaction? transaction,
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
        await using var command = CreateCommand(connection, transaction, sql);
        Bind(command, ("@pcId", pcId), ("@classification", DbValue(classification)));
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

    private async Task<long> UpsertPcAsync(
        DbConnection connection,
        DbTransaction transaction,
        PcIdentity pc,
        DateTimeOffset? inventoryAt,
        DateTimeOffset? heartbeatAt,
        CancellationToken cancellationToken)
    {
        var table = options.PcTable;
        var pcId = await FindPcIdForAgentAsync(connection, transaction, pc, cancellationToken);
        if (pcId is null)
        {
            var insert = InsertReturningId(
                Name(options.SchemaName, table.TableName),
                $"{Name(table.DeviceCodeColumn)}, {Name(table.HostNameColumn)}, {Name(table.DomainNameColumn)}, {Name(table.OperatingSystemColumn)}, {Name(table.AgentVersionColumn)}, {Name(table.LastHeartbeatUtcColumn)}, {Name(table.LastInventoryUtcColumn)}, {Name(table.DevicePublicKeyColumn)}, {Name(table.DeviceKeySha256Column)}",
                "@deviceCode, @hostName, @domainName, @operatingSystem, @agentVersion, @heartbeatAt, @inventoryAt, @devicePublicKey, @deviceKeySha256",
                table.PrimaryKeyColumn);
            var insertedId = await ExecuteInsertReturningIdAsync(
                connection,
                transaction,
                insert,
                cancellationToken,
                UpsertParameters(pc, heartbeatAt, inventoryAt));
            if (!string.IsNullOrWhiteSpace(pc.DevicePublicKey))
            {
                await SupersedeOtherGenerationsAsync(connection, transaction, pc.DeviceCode, insertedId, cancellationToken);
            }

            return insertedId;
        }

        var update = $"""
            UPDATE {Name(options.SchemaName, table.TableName)}
            SET {Name(table.HostNameColumn)} = @hostName,
                {Name(table.DomainNameColumn)} = CASE WHEN @domainName = {Empty} THEN {Name(table.DomainNameColumn)} ELSE @domainName END,
                {Name(table.OperatingSystemColumn)} = CASE WHEN @operatingSystem = {Empty} THEN {Name(table.OperatingSystemColumn)} ELSE @operatingSystem END,
                {Name(table.AgentVersionColumn)} = CASE
                    WHEN @inventoryAt IS NOT NULL
                     AND {Name(table.LastHeartbeatUtcColumn)} > @inventoryAt
                    THEN {Name(table.AgentVersionColumn)}
                    ELSE @agentVersion
                END,
                {Name(table.LastHeartbeatUtcColumn)} = COALESCE(@heartbeatAt, {Name(table.LastHeartbeatUtcColumn)}),
                {Name(table.LastInventoryUtcColumn)} = COALESCE(@inventoryAt, {Name(table.LastInventoryUtcColumn)}),
                {Name(table.DevicePublicKeyColumn)} = COALESCE({Name(table.DevicePublicKeyColumn)}, @devicePublicKey),
                {Name(table.DeviceKeySha256Column)} = COALESCE({Name(table.DeviceKeySha256Column)}, @deviceKeySha256)
            WHERE {Name(table.PrimaryKeyColumn)} = @pcId
             AND (@heartbeatAt IS NULL OR {Name(table.LastHeartbeatUtcColumn)} IS NULL OR @heartbeatAt >= {Name(table.LastHeartbeatUtcColumn)});
            """;
        await using var updateCommand = CreateCommand(connection, transaction, update);
        Bind(updateCommand, UpsertParameters(pc, heartbeatAt, inventoryAt));
        Bind(updateCommand, ("@pcId", pcId.Value));
        await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        return pcId.Value;
    }

    private string InsertReturningId(string tableSql, string columns, string values, string idColumn)
    {
        return dialect.Provider switch
        {
            StorageProvider.PostgreSql => $"""
                INSERT INTO {tableSql}
                ({columns})
                VALUES ({values})
                RETURNING {Name(idColumn)};
                """,
            StorageProvider.MySql => $"""
                INSERT INTO {tableSql}
                ({columns})
                VALUES ({values});
                """,
            _ => $"""
                INSERT INTO {tableSql}
                ({columns})
                VALUES ({values});
                SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
                """
        };
    }

    private async Task<long> ExecuteInsertReturningIdAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(connection, transaction, sql);
        Bind(command, parameters);
        if (dialect.DmlReturnsRows)
        {
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
        await using var idCommand = CreateCommand(connection, transaction, "SELECT LAST_INSERT_ID()");
        return Convert.ToInt64(await idCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private async Task<long?> FindPcIdForAgentAsync(
        DbConnection connection,
        DbTransaction transaction,
        PcIdentity pc,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(pc.DeviceId))
        {
            var byId = await FindPcIdByColumnAsync(connection, transaction, options.PcTable.DeviceIdColumn, pc.DeviceId, cancellationToken);
            if (byId is not null)
            {
                return byId;
            }
        }

        var hash = DeviceKeyFingerprint.Sha256Hex(pc.DevicePublicKey);
        if (hash is not null)
        {
            var byHash = await FindPcIdByColumnAsync(connection, transaction, options.PcTable.DeviceKeySha256Column, hash, cancellationToken);
            if (byHash is not null)
            {
                return byHash;
            }

            var byKey = await FindPcIdByColumnAsync(connection, transaction, options.PcTable.DevicePublicKeyColumn, pc.DevicePublicKey!.Trim(), cancellationToken);
            if (byKey is not null)
            {
                return byKey;
            }

            return await FindUnboundCurrentPcIdAsync(connection, transaction, pc.DeviceCode, cancellationToken);
        }

        return await FindPcIdAsync(connection, transaction, pc.DeviceCode, cancellationToken);
    }

    private async Task<long?> FindUnboundCurrentPcIdAsync(
        DbConnection connection,
        DbTransaction transaction,
        string deviceCode,
        CancellationToken cancellationToken)
    {
        var table = options.PcTable;
        var sql = $"""
            SELECT {TakePrefix("1")} {Name(table.PrimaryKeyColumn)}
            FROM {Name(options.SchemaName, table.TableName)}
            WHERE ({Name(table.DeviceCodeColumn)} = @deviceCode OR {Name(table.AssignedDeviceCodeColumn)} = @deviceCode)
              AND {Name(table.SupersededAtUtcColumn)} IS NULL
              AND {Name(table.DevicePublicKeyColumn)} IS NULL
            ORDER BY {Name(table.PrimaryKeyColumn)} DESC
            {TakeSuffix("1")};
            """;
        await using var command = CreateCommand(connection, transaction, sql);
        Bind(command, ("@deviceCode", deviceCode));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private async Task SupersedeOtherGenerationsAsync(
        DbConnection connection,
        DbTransaction transaction,
        string deviceCode,
        long pcId,
        CancellationToken cancellationToken)
    {
        var table = options.PcTable;
        var sql = $"""
            UPDATE {Name(options.SchemaName, table.TableName)}
            SET {Name(table.SupersededAtUtcColumn)} = @supersededAt
            WHERE {Name(table.DeviceCodeColumn)} = @deviceCode
              AND {Name(table.PrimaryKeyColumn)} <> @pcId
              AND {Name(table.SupersededAtUtcColumn)} IS NULL;
            """;
        await ExecuteAsync(
            connection,
            transaction,
            sql,
            [("@deviceCode", deviceCode), ("@pcId", pcId), ("@supersededAt", DateTimeOffset.UtcNow)],
            cancellationToken);
    }

    private async Task<long?> FindPcIdByColumnAsync(
        DbConnection connection,
        DbTransaction transaction,
        string column,
        string value,
        CancellationToken cancellationToken)
    {
        var table = options.PcTable;
        var sql = $"""
            SELECT {Name(table.PrimaryKeyColumn)}
            FROM {Name(options.SchemaName, table.TableName)}
            WHERE {Name(column)} = @value;
            """;
        await using var command = CreateCommand(connection, transaction, sql);
        Bind(command, ("@value", value));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static (string Name, object? Value)[] UpsertParameters(PcIdentity pc, DateTimeOffset? heartbeatAt, DateTimeOffset? inventoryAt) =>
    [
        ("@deviceCode", pc.DeviceCode), ("@hostName", pc.HostName), ("@domainName", pc.DomainName),
        ("@operatingSystem", pc.OperatingSystem), ("@agentVersion", pc.AgentVersion),
        ("@heartbeatAt", DbValue(heartbeatAt)), ("@inventoryAt", DbValue(inventoryAt)),
        ("@devicePublicKey", DbValue(NullIfWhiteSpace(pc.DevicePublicKey))),
        ("@deviceKeySha256", DbValue(DeviceKeyFingerprint.Sha256Hex(pc.DevicePublicKey)))
    ];

    private async Task<int> ExecuteAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        (string Name, object? Value)[] parameters,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, sql);
        Bind(command, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private readonly record struct TypedNull(bool Timestamp);

    private static object DbValue(string? value) => value ?? (object)new TypedNull(false);

    private static object DbValue(DateTimeOffset? value)
    {
        if (value is null)
        {
            return new TypedNull(true);
        }

        return value.Value;
    }

    private static object DbValue(object? value) => value switch
    {
        null or DBNull => DBNull.Value,
        TypedNull typed => typed,
        _ => value
    };

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    internal string? ToContainsPattern(string? search) => dialect.ToContainsPattern(search);

    private static string ReadOrigin(DbDataReader reader, string column)
    {
        var stored = ReadNullableString(reader, column);
        return string.Equals(stored, UninstallGrant.OriginAdmin, StringComparison.OrdinalIgnoreCase)
            ? UninstallGrant.OriginAdmin
            : UninstallGrant.OriginAgent;
    }

    private static string ReadClassification(DbDataReader reader, string column)
    {
        var stored = ReadNullableString(reader, column);
        return SoftwarePolicyClassificationNames.TryParseInstalledSoftware(stored, out var classification)
            ? classification
            : SoftwarePolicyClassificationNames.Unclassified;
    }

    private static string? ReadNullableString(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : ReadDateTimeOffset(reader, ordinal);
    }

    private static DateTimeOffset ReadDateTimeOffset(DbDataReader reader, int ordinal) =>
        CoerceDateTimeOffset(reader.GetValue(ordinal));

    internal static DateTimeOffset CoerceDateTimeOffset(object value) => value switch
    {
        DateTimeOffset timestamp => timestamp,
        DateTime date when date.Kind == DateTimeKind.Utc => new DateTimeOffset(date),
        DateTime date => new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)),
        _ => throw new InvalidCastException($"Cannot read {value.GetType().FullName} as a UTC timestamp.")
    };

    private static int ReadInt32(DbDataReader reader, string column) =>
        Convert.ToInt32(reader.GetValue(reader.GetOrdinal(column)), CultureInfo.InvariantCulture);

    internal string Name(params string[] parts) => dialect.QuoteQualified(parts);

    private static string OrderByColumn(string? expression, bool descending, string fallback) =>
        string.IsNullOrEmpty(expression)
            ? fallback
            : $"{expression} {(descending ? "DESC" : "ASC")}, {fallback}";

    internal string DisplayHostNameSql(string? tableAlias = null)
    {
        var table = options.PcTable;
        var prefix = string.IsNullOrEmpty(tableAlias) ? "" : tableAlias + ".";
        return $"COALESCE(NULLIF({prefix}{Name(table.AssignedHostNameColumn)}, {Empty}), {prefix}{Name(table.HostNameColumn)})";
    }

    internal string DisplayDeviceCodeSql(string? tableAlias = null)
    {
        var table = options.PcTable;
        var prefix = string.IsNullOrEmpty(tableAlias) ? "" : tableAlias + ".";
        return $"COALESCE(NULLIF({prefix}{Name(table.AssignedDeviceCodeColumn)}, {Empty}), {prefix}{Name(table.DeviceCodeColumn)})";
    }

    internal string PcLookupPredicate(string? tableAlias = null)
    {
        var table = options.PcTable;
        var prefix = string.IsNullOrEmpty(tableAlias) ? "" : tableAlias + ".";
        return $"""
            {prefix}{Name(table.DeviceCodeColumn)} = @deviceCode
            OR {prefix}{Name(table.AssignedDeviceCodeColumn)} = @deviceCode
            OR {prefix}{Name(table.DeviceIdColumn)} = @deviceCode
            """;
    }

    internal string PreferredPcOrder(string tableAlias)
    {
        var table = options.PcTable;
        var prefix = tableAlias + ".";
        return $"""
            CASE WHEN {prefix}{Name(table.DeviceIdColumn)} = @deviceCode THEN 0 ELSE 1 END,
            CASE WHEN {prefix}{Name(table.SupersededAtUtcColumn)} IS NULL THEN 0 ELSE 1 END,
            CASE WHEN {prefix}{Name(table.InstalledAtUtcColumn)} IS NULL THEN 1 ELSE 0 END,
            {prefix}{Name(table.InstalledAtUtcColumn)} DESC,
            {prefix}{Name(table.PrimaryKeyColumn)} DESC
            """;
    }

    internal string PreferredPcIdSql()
    {
        var table = options.PcTable;
        return $"""
            SELECT {TakePrefix("1")} chosen.{Name(table.PrimaryKeyColumn)}
            FROM {Name(options.SchemaName, table.TableName)} AS chosen
            WHERE {PcLookupPredicate("chosen")}
            ORDER BY {PreferredPcOrder("chosen")}
            {TakeSuffix("1")}
            """;
    }

    internal string CurrentInstallationSql(string? tableAlias = null)
    {
        var table = options.PcTable;
        var prefix = string.IsNullOrEmpty(tableAlias) ? "" : tableAlias + ".";
        return $"{prefix}{Name(table.SupersededAtUtcColumn)} IS NULL";
    }

    internal string HostNameSearchSql(string? tableAlias = null)
    {
        var table = options.PcTable;
        var prefix = string.IsNullOrEmpty(tableAlias) ? "" : tableAlias + ".";
        return $"""
            {prefix}{Name(table.DeviceCodeColumn)} LIKE @search{LikeEscape}
            OR {prefix}{Name(table.HostNameColumn)} LIKE @search{LikeEscape}
            OR {prefix}{Name(table.AssignedHostNameColumn)} LIKE @search{LikeEscape}
            OR {prefix}{Name(table.AssignedDeviceCodeColumn)} LIKE @search{LikeEscape}
            OR {prefix}{Name(table.DeviceIdColumn)} LIKE @search{LikeEscape}
            """;
    }
}

internal sealed class SoftwareAggregateKeyComparer : IEqualityComparer<(string Name, string? Version)>
{
    public static SoftwareAggregateKeyComparer Instance { get; } = new();

    public bool Equals((string Name, string? Version) left, (string Name, string? Version) right) =>
        string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Version, right.Version, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode((string Name, string? Version) value) =>
        HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Name),
            value.Version is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(value.Version));
}
