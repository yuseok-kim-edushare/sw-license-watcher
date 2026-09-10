using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

internal sealed class SqlServerWorkerUpdatePinRepository(SqlServerDataContext context) : IWorkerUpdatePinStore
{
    public Task<UpdateManifest?> GetWorkerUpdatePinAsync(
        string targetServiceName, CancellationToken cancellationToken) =>
        context.GetWorkerUpdatePinAsync(targetServiceName, cancellationToken);

    public Task<UpdateManifest> UpsertWorkerUpdatePinAsync(
        UpdateManifest pin, CancellationToken cancellationToken) =>
        context.UpsertWorkerUpdatePinAsync(pin, cancellationToken);

    public Task<bool> SeedWorkerUpdatePinIfEmptyAsync(
        UpdateManifest pin, CancellationToken cancellationToken) =>
        context.SeedWorkerUpdatePinIfEmptyAsync(pin, cancellationToken);
}
