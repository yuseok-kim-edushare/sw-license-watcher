using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

public sealed class SqlServerSchemaApplicator(
    SqlServerStorageOptions storage,
    IOptions<DatabaseOptions> database,
    SqlServerSchemaScriptBuilder schemaBuilder,
    ILogger<SqlServerSchemaApplicator> logger)
{
    private readonly object _gate = new();
    private SchemaApplySnapshot _last = SchemaApplySnapshot.Empty;

    public SchemaApplySnapshot Last
    {
        get
        {
            lock (_gate)
            {
                return _last;
            }
        }
    }

    public async Task ApplyIfEnabledAsync(CancellationToken cancellationToken)
    {
        if (!database.Value.ApplySchemaOnStartup)
        {
            return;
        }

        logger.LogInformation("Applying SQL Server schema because Database:ApplySchemaOnStartup is enabled.");
        try
        {
            var batchCount = await ApplyAsync(cancellationToken);
            logger.LogInformation(
                "SQL Server schema was applied successfully ({BatchCount} batch(es)).",
                batchCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogCritical(ex, "Failed to apply SQL Server schema on startup. The API will not start.");
            throw new InvalidOperationException(
                "Failed to apply SQL Server schema on startup. See logs for details.",
                ex);
        }
    }

    public async Task<int> ApplyAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storage.ConnectionString))
        {
            Record(succeeded: false, batchCount: 0, "Storage:SqlServer:ConnectionString is empty.");
            throw new InvalidOperationException("Storage:SqlServer:ConnectionString is required to apply the schema.");
        }

        var batches = BuildBatches();
        if (batches.Count == 0)
        {
            Record(succeeded: false, batchCount: 0, "The generated SQL Server schema script is empty.");
            throw new InvalidOperationException("The generated SQL Server schema script is empty.");
        }

        try
        {
            await using var connection = new SqlConnection(storage.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            for (var i = 0; i < batches.Count; i++)
            {
                await using var command = new SqlCommand(batches[i], connection)
                {
                    CommandTimeout = 120
                };
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            Record(succeeded: true, batches.Count, error: null);
            return batches.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Record(succeeded: false, batchCount: 0, ex.Message);
            throw;
        }
    }

    internal IReadOnlyList<string> BuildBatches() =>
        SqlScriptBatchSplitter.Split(schemaBuilder.Build(storage));

    private void Record(bool succeeded, int batchCount, string? error)
    {
        var utc = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            _last = new SchemaApplySnapshot(
                succeeded,
                batchCount,
                utc,
                succeeded ? utc : _last.LastSucceededUtc,
                error);
        }
    }
}

public readonly record struct SchemaApplySnapshot(
    bool? Succeeded,
    int BatchCount,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSucceededUtc,
    string? LastError)
{
    public static SchemaApplySnapshot Empty { get; } = new(null, 0, null, null, null);
}
