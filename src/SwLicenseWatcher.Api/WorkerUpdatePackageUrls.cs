using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal static class WorkerUpdatePackageUrls
{
    internal static string ForRequest(HttpRequest request, string version)
    {
        var builder = new UriBuilder
        {
            Scheme = request.Scheme,
            Host = request.Host.Host,
            Path = $"{request.PathBase}{AgentPaths.WorkerPackage}/{Uri.EscapeDataString(version)}"
        };
        if (request.Host.Port is { } port)
        {
            builder.Port = port;
        }

        return builder.Uri.AbsoluteUri;
    }

    internal static UpdateManifest WithLocalPackageUrlIfCached(
        UpdateManifest pin,
        HttpRequest request,
        WorkerUpdatePackageStore packages)
    {
        if (!packages.TryGetPackagePath(pin.Version, out _))
        {
            return pin;
        }

        return pin with { PackageUrl = ForRequest(request, WorkerUpdatePackageNames.NormalizeVersion(pin.Version)) };
    }
}
