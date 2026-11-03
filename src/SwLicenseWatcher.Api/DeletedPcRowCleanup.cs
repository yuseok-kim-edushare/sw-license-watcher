using SwLicenseWatcher.Infrastructure.Relational;

namespace SwLicenseWatcher.Api;

public sealed class DeletedPcRowCleanup(
    RelationalDataContext context,
    ILogger<DeletedPcRowCleanup> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _completed;

    public async Task<bool> TrySweepAsync(CancellationToken cancellationToken)
    {
        if (_completed)
        {
            return true;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_completed)
            {
                return true;
            }

            var removed = await context.RemoveRowsLeftByDeletedPcsAsync(cancellationToken);
            logger.LogInformation("Removed {RemovedCount} rows left for deleted PCs.", removed);
            _completed = true;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not remove rows left for deleted PCs.");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }
}
