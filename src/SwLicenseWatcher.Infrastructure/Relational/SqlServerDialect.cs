using System.Data.Common;
using Microsoft.Data.SqlClient;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.Relational;

internal sealed class SqlServerDialect : IRelationalDialect
{
    public static SqlServerDialect Instance { get; } = new();

    public StorageProvider Provider => StorageProvider.SqlServer;

    public string EngineName => "SQL Server";

    public DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);

    public DbConnection CreateSchemaConnection(string connectionString) => CreateConnection(connectionString);

    public string Quote(string identifier) =>
        "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";

    public string QuoteQualified(params string[] parts) =>
        string.Join('.', parts.Select(Quote));

    public string EmptyStringLiteral => "N''";

    public string UnicodeLiteral(string value) => "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    public string TrueLiteral => "1";

    public string PaginationClause => "OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";

    public string TakePrefix(string takeExpression) => $"TOP ({takeExpression}) ";

    public string TakeSuffix(string takeExpression) => string.Empty;

    public string LockFromClause => " WITH (UPDLOCK, HOLDLOCK)";

    public string LockSuffix => string.Empty;

    public string LikeEscapeClause => string.Empty;

    public string? ToContainsPattern(string? search)
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

    public object NormalizeParameterValue(object? value) => value switch
    {
        null or DBNull => DBNull.Value,
        _ => value
    };

    public void AssignNullParameter(DbParameter parameter, bool timestamp)
    {
    }

    public bool DmlReturnsRows => true;

    public bool IsUniqueViolation(DbException exception) =>
        exception is SqlException sql && sql.Number is 2601 or 2627;
}
