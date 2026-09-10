using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal static class WorkerUpdateEndpoints
{
    internal static IEndpointRouteBuilder MapWorkerUpdateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(AgentPaths.WorkerManifest, async (
            WorkerUpdatePinService workerPin,
            CancellationToken cancellationToken) =>
            Results.Ok(await workerPin.GetEffectiveAsync(cancellationToken)));
        endpoints.MapPut(AgentPaths.WorkerManifest, async (
            UpdateManifest request,
            WorkerUpdatePinService workerPin,
            CancellationToken cancellationToken) =>
        {
            var pin = request with { TargetServiceName = workerPin.Configured.TargetServiceName };
            if (!UpdateManifestValidator.TryValidate(pin, out var validationError))
            {
                return Results.BadRequest(validationError);
            }

            var saved = await workerPin.SaveAsync(pin, cancellationToken);
            return Results.Ok(saved);
        });

        return endpoints;
    }
}
