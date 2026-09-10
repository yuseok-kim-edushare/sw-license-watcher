using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;
using SwLicenseWatcher.Infrastructure.SqlServer;

namespace SwLicenseWatcher.Api;

public sealed class SchemaReconcileService(
    SqlServerSchemaApplicator applicator,
    IOptions<DatabaseOptions> database,
    ILogger<SchemaReconcileService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!database.Value.ApplySchemaInBackground)
        {
            logger.LogInformation("Background SQL schema reconcile is disabled.");
            return;
        }

        var retry = database.Value.SchemaReconcileRetry;
        if (retry <= TimeSpan.Zero)
        {
            retry = TimeSpan.FromSeconds(30);
        }

        using var timer = new PeriodicTimer(retry);
        do
        {
            if (await TryApplyAsync(stoppingToken))
            {
                return;
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task<bool> TryApplyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var batchCount = await applicator.ApplyAsync(cancellationToken);
            logger.LogInformation(
                "Background SQL schema reconcile applied {BatchCount} batch(es).",
                batchCount);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Background SQL schema reconcile failed; it will retry.");
            return false;
        }
    }
}
