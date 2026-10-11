using System.Data.Common;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.Relational;

public interface IRelationalDialect
{
    StorageProvider Provider { get; }

    string EngineName { get; }

    DbConnection CreateConnection(string connectionString);

    DbConnection CreateSchemaConnection(string connectionString);

    string Quote(string identifier);

    string QuoteQualified(params string[] parts);

    string EmptyStringLiteral { get; }

    string UnicodeLiteral(string value);

    string TrueLiteral { get; }

    string PaginationClause { get; }

    string TakePrefix(string takeExpression);

    string TakeSuffix(string takeExpression);

    string LockFromClause { get; }

    string LockSuffix { get; }

    string LikeEscapeClause { get; }

    string? ToContainsPattern(string? search);

    object NormalizeParameterValue(object? value);

    void AssignNullParameter(DbParameter parameter, bool timestamp);

    bool DmlReturnsRows { get; }

    bool IsUniqueViolation(DbException exception);
}

public static class RelationalDialects
{
    public static IRelationalDialect Create(StorageProvider provider) => provider switch
    {
        StorageProvider.MySql => MySqlDialect.Instance,
        StorageProvider.PostgreSql => PostgreSqlDialect.Instance,
        _ => SqlServerDialect.Instance
    };

    public static IRelationalDialect Create(string? provider) =>
        Create(StorageProviders.Parse(provider));
}
