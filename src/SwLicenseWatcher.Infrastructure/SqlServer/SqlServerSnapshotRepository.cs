using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

internal sealed class SqlServerSnapshotRepository(SqlServerDataContext context) : ISnapshotRepository
{
    public Task<SnapshotSaveResult> SaveSnapshotAsync(
        InventoryIngestionRequest snapshot,
        CancellationToken cancellationToken) =>
        context.SaveSnapshotAsync(snapshot, cancellationToken);
}
