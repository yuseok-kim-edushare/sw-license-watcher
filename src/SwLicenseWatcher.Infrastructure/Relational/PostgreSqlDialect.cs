using System.Data.Common;
using Npgsql;
using NpgsqlTypes;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.Relational;

internal sealed class PostgreSqlDialect : IRelationalDialect
{
    public static PostgreSqlDialect Instance { get; } = new();

    public StorageProvider Provider => StorageProvider.PostgreSql;

    public string EngineName => "PostgreSQL";

    public DbConnection CreateConnection(string connectionString) => new NpgsqlConnection(Normalize(connectionString));

    public DbConnection CreateSchemaConnection(string connectionString) => CreateConnection(connectionString);

    public string Quote(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    public string QuoteQualified(params string[] parts) =>
        string.Join('.', parts.Select(Quote));

    public string EmptyStringLiteral => "''";

    public string UnicodeLiteral(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    public string TrueLiteral => "TRUE";

    public string PaginationClause => "OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";

    public string TakePrefix(string takeExpression) => string.Empty;

    public string TakeSuffix(string takeExpression) => $" LIMIT {takeExpression}";

    public string LockFromClause => string.Empty;

    public string LockSuffix => " FOR UPDATE";

    public string LikeEscapeClause => " ESCAPE '!'";

    public string? ToContainsPattern(string? search) => RelationalLike.Escape(search);

    public object NormalizeParameterValue(object? value) => value switch
    {
        null or DBNull => DBNull.Value,
        DateTimeOffset timestamp => timestamp.ToUniversalTime(),
        _ => value
    };

    public void AssignNullParameter(DbParameter parameter, bool timestamp)
    {
        if (parameter is NpgsqlParameter npgsql)
        {
            npgsql.NpgsqlDbType = timestamp ? NpgsqlDbType.TimestampTz : NpgsqlDbType.Text;
        }
    }

    public bool DmlReturnsRows => true;

    public bool IsUniqueViolation(DbException exception) =>
        exception is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation;

    private static string Normalize(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.Timezone))
        {
            builder.Timezone = "UTC";
        }

        return builder.ConnectionString;
    }
}

internal static class RelationalLike
{
    public static string? Escape(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var escaped = search.Trim()
            .Replace("!", "!!", StringComparison.Ordinal)
            .Replace("%", "!%", StringComparison.Ordinal)
            .Replace("_", "!_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }
}
