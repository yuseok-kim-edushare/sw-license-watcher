using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.Relational;

public sealed class RelationalSchemaApplicator(
    SqlServerStorageOptions storage,
    IOptions<DatabaseOptions> database,
    ISchemaScriptBuilder schemaBuilder,
    IRelationalDialect dialect,
    ILogger<RelationalSchemaApplicator> logger)
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

        logger.LogInformation("Applying {Engine} schema because Database:ApplySchemaOnStartup is enabled.", dialect.EngineName);
        try
        {
            var batchCount = await ApplyAsync(cancellationToken);
            logger.LogInformation(
                "{Engine} schema was applied successfully ({BatchCount} batch(es)).",
                dialect.EngineName,
                batchCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogCritical(ex, "Failed to apply {Engine} schema on startup. The API will not start.", dialect.EngineName);
            throw new InvalidOperationException(
                $"Failed to apply {dialect.EngineName} schema on startup. See logs for details.",
                ex);
        }
    }

    public async Task<int> ApplyAsync(CancellationToken cancellationToken)
    {
        var section = StorageProviders.SectionName(dialect.Provider);
        if (string.IsNullOrWhiteSpace(storage.ConnectionString))
        {
            Record(succeeded: false, batchCount: 0, $"Storage:{section}:ConnectionString is empty.");
            throw new InvalidOperationException($"Storage:{section}:ConnectionString is required to apply the schema.");
        }

        var batches = BuildBatches();
        if (batches.Count == 0)
        {
            Record(succeeded: false, batchCount: 0, $"The generated {dialect.EngineName} schema script is empty.");
            throw new InvalidOperationException($"The generated {dialect.EngineName} schema script is empty.");
        }

        try
        {
            await using var connection = dialect.CreateSchemaConnection(storage.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            for (var i = 0; i < batches.Count; i++)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = batches[i];
                command.CommandTimeout = 120;
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

    internal IReadOnlyList<string> BuildBatches() => schemaBuilder.BuildBatches(storage);

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
