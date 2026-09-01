using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

public sealed class SqlServerSchemaApplicator(
    SqlServerStorageOptions storage,
    IOptions<DatabaseOptions> database,
    SqlServerSchemaScriptBuilder schemaBuilder,
    ILogger<SqlServerSchemaApplicator> logger)
{
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
        var batches = BuildBatches();
        if (batches.Count == 0)
        {
            throw new InvalidOperationException("The generated SQL Server schema script is empty.");
        }

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

        return batches.Count;
    }

    internal IReadOnlyList<string> BuildBatches() =>
        SqlScriptBatchSplitter.Split(schemaBuilder.Build(storage));
}
