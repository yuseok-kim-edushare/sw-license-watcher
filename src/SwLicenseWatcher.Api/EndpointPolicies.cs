namespace SwLicenseWatcher.Api;

internal static class AgentPaths
{
    internal const string InventorySnapshots = "/api/inventory/snapshots";
    internal const string Heartbeats = "/api/agents/heartbeats";
    internal const string WorkerManifest = "/api/updates/worker/manifest";
    internal const string UninstallRequests = "/api/agents/uninstall-requests";
}

internal static class EndpointPolicies
{
    internal const long SnapshotRequestBodyBytes = 8 * 1024 * 1024;
    internal const long HeartbeatRequestBodyBytes = 64 * 1024;

    internal static bool IsAgentEndpoint(PathString path, string httpMethod)
    {
        if (path.Equals(AgentPaths.WorkerManifest, StringComparison.OrdinalIgnoreCase))
        {
            return HttpMethods.IsGet(httpMethod);
        }

        return path.Equals(AgentPaths.InventorySnapshots, StringComparison.OrdinalIgnoreCase) |
            path.Equals(AgentPaths.Heartbeats, StringComparison.OrdinalIgnoreCase) |
            path.StartsWithSegments(AgentPaths.UninstallRequests, StringComparison.OrdinalIgnoreCase);
    }

    internal static long? GetRequestBodySize(PathString path)
    {
        if (path.Equals(AgentPaths.InventorySnapshots, StringComparison.OrdinalIgnoreCase))
        {
            return SnapshotRequestBodyBytes;
        }

        if (path.Equals(AgentPaths.Heartbeats, StringComparison.OrdinalIgnoreCase))
        {
            return HeartbeatRequestBodyBytes;
        }

        return null;
    }
}
