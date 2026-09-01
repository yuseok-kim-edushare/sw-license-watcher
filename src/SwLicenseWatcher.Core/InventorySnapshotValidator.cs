namespace SwLicenseWatcher.Core;

public static class InventorySnapshotValidator
{
    public static bool TryValidate(InventoryIngestionRequest? snapshot, out string error)
    {
        if (snapshot is null)
        {
            error = "The snapshot payload is required.";
            return false;
        }

        if (snapshot.Pc is null ||
            string.IsNullOrWhiteSpace(snapshot.Pc.DeviceCode) ||
            string.IsNullOrWhiteSpace(snapshot.Pc.HostName) ||
            snapshot.Pc.DomainName is null ||
            snapshot.Pc.OperatingSystem is null ||
            string.IsNullOrWhiteSpace(snapshot.Pc.AgentVersion) ||
            snapshot.CollectedAtUtc == default)
        {
            error = "The snapshot is missing required identity fields.";
            return false;
        }

        if (snapshot.InstalledSoftware is null)
        {
            error = "The snapshot is missing the installed software collection.";
            return false;
        }

        foreach (var entry in snapshot.InstalledSoftware)
        {
            if (entry is null ||
                string.IsNullOrWhiteSpace(entry.Name) ||
                string.IsNullOrWhiteSpace(entry.DiscoveryScope) ||
                string.IsNullOrWhiteSpace(entry.DiscoverySource))
            {
                error = "The snapshot contains an invalid installed software entry.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }
}
