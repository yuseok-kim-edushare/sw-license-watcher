namespace SwLicenseWatcher.Api;

internal static class PublicPaths
{
    internal static bool IsAnonymous(PathString path) =>
        path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
        IsAdminAsset(path) ||
        IsWorkerPackageDownload(path);

    internal static bool IsWorkerPackageDownload(PathString path) =>
        path.StartsWithSegments(AgentPaths.WorkerPackage, StringComparison.OrdinalIgnoreCase);

    internal static bool IsAdminAsset(PathString path) =>
        path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase);

    internal static bool IsAdminIndex(PathString path) =>
        path.Equals("/admin", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/admin/", StringComparison.OrdinalIgnoreCase);
}
