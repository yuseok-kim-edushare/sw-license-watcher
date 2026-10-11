using SwLicenseWatcher.Application;
using SwLicenseWatcher.Infrastructure.Relational;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

internal sealed class SqlServerSnapshotRepository(RelationalDataContext context) : ISnapshotRepository
{
    public Task<SnapshotSaveResult> SaveSnapshotAsync(
        InventoryIngestionRequest snapshot,
        CancellationToken cancellationToken) =>
        context.SaveSnapshotAsync(snapshot, cancellationToken);
}
