using System.Data.Common;
using MySqlConnector;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.Relational;

internal sealed class MySqlDialect : IRelationalDialect
{
    public static MySqlDialect Instance { get; } = new();

    public StorageProvider Provider => StorageProvider.MySql;

    public string EngineName => "MySQL";

    public DbConnection CreateConnection(string connectionString) =>
        new MySqlConnection(BuildConnectionString(connectionString, allowUserVariables: false));

    public DbConnection CreateSchemaConnection(string connectionString) =>
        new MySqlConnection(BuildConnectionString(connectionString, allowUserVariables: true));

    public string Quote(string identifier) =>
        "`" + identifier.Replace("`", "``", StringComparison.Ordinal) + "`";

    public string QuoteQualified(params string[] parts) =>
        parts.Length == 2 ? Quote(parts[1]) : string.Join('.', parts.Select(Quote));

    public string EmptyStringLiteral => "''";

    public string UnicodeLiteral(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    public string TrueLiteral => "1";

    public string PaginationClause => "LIMIT @take OFFSET @skip";

    public string TakePrefix(string takeExpression) => string.Empty;

    public string TakeSuffix(string takeExpression) => $" LIMIT {takeExpression}";

    public string LockFromClause => string.Empty;

    public string LockSuffix => " FOR UPDATE";

    public string LikeEscapeClause => " ESCAPE '!'";

    public string? ToContainsPattern(string? search) => RelationalLike.Escape(search);

    public object NormalizeParameterValue(object? value) => value switch
    {
        null or DBNull => DBNull.Value,
        DateTimeOffset timestamp => timestamp.UtcDateTime,
        _ => value
    };

    public void AssignNullParameter(DbParameter parameter, bool timestamp)
    {
    }

    public bool DmlReturnsRows => false;

    public bool IsUniqueViolation(DbException exception) =>
        exception is MySqlException mysql && mysql.Number == 1062;

    private static string BuildConnectionString(string connectionString, bool allowUserVariables)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            AllowUserVariables = allowUserVariables,
            TreatTinyAsBoolean = true,
            UseAffectedRows = false,
            DateTimeKind = MySqlDateTimeKind.Utc
        };
        return builder.ConnectionString;
    }
}
