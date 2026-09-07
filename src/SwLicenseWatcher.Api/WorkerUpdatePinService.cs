using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

public sealed class WorkerUpdatePinService(
    SqlServerInventoryRepository repository,
    IOptions<UpdateManifestOptions> options,
    IOptions<SqlServerStorageOptions> storage,
    ILogger<WorkerUpdatePinService> logger)
{
    public UpdateManifest Configured => options.Value.ToManifest();

    public async Task<UpdateManifest> GetEffectiveAsync(CancellationToken cancellationToken)
    {
        var configured = Configured;
        if (string.IsNullOrWhiteSpace(storage.Value.ConnectionString))
        {
            return configured;
        }

        try
        {
            var stored = await repository.GetWorkerUpdatePinAsync(configured.TargetServiceName, cancellationToken);
            return stored ?? configured;
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "Worker update pin is unavailable; serving appsettings Updates:Worker.");
            return configured;
        }
    }

    public async Task SeedIfEmptyAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storage.Value.ConnectionString))
        {
            return;
        }

        var configured = Configured;
        try
        {
            if (await repository.SeedWorkerUpdatePinIfEmptyAsync(configured, cancellationToken))
            {
                logger.LogInformation(
                    "Seeded Worker update pin {Version} from appsettings Updates:Worker.",
                    configured.Version);
            }
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "Could not seed Worker update pin from appsettings.");
        }
    }

    public Task<UpdateManifest> SaveAsync(UpdateManifest pin, CancellationToken cancellationToken) =>
        repository.UpsertWorkerUpdatePinAsync(pin, cancellationToken);
}
