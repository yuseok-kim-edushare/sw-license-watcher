namespace SwLicenseWatcher.Api;

internal static class RequestBodySizeLimits
{
    internal const long SnapshotBytes = 8 * 1024 * 1024;
    internal const long HeartbeatBytes = 64 * 1024;

    internal static long? Resolve(PathString path)
    {
        if (path.Equals("/api/inventory/snapshots", StringComparison.OrdinalIgnoreCase))
        {
            return SnapshotBytes;
        }

        if (path.Equals("/api/agents/heartbeats", StringComparison.OrdinalIgnoreCase))
        {
            return HeartbeatBytes;
        }

        return null;
    }
}
