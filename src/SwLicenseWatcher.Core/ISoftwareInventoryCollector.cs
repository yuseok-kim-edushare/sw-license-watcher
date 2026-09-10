namespace SwLicenseWatcher.Core;

public interface ISoftwareInventoryCollector
{
    Task<IReadOnlyCollection<InstalledSoftwareEntry>> CollectAsync(CancellationToken cancellationToken);
}
