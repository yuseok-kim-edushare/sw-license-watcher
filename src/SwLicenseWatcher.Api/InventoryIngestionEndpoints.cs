using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal static class InventoryIngestionEndpoints
{
    internal static IEndpointRouteBuilder MapInventoryIngestionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(AgentPaths.InventorySnapshots, async (
            InventoryIngestionRequest request,
            InventoryMemoryStore store,
            ISnapshotRepository repository,
            NotificationPublisher notifications,
            CancellationToken cancellationToken) =>
        {
            if (!InventorySnapshotValidator.TryValidate(request, out var validationError))
            {
                return Results.BadRequest(validationError);
            }

            var saveResult = await repository.SaveSnapshotAsync(request, cancellationToken);
            store.RecordSnapshot(request);
            notifications.EnqueueNewSoftwareIfNeeded(request, saveResult);
            notifications.EnqueueBlacklistViolationsIfNeeded(request, saveResult);
            return Results.Accepted($"/api/inventory/devices/{request.Pc.DeviceCode}", new SnapshotAcceptedResponse(
                request.Pc.DeviceCode,
                request.InstalledSoftware.Count,
                request.CollectedAtUtc));
        });
        endpoints.MapPost(AgentPaths.Heartbeats, async (
            AgentHeartbeat heartbeat,
            InventoryMemoryStore store,
            IHeartbeatRepository repository,
            CancellationToken cancellationToken) =>
        {
            if (!InventorySnapshotValidator.TryValidate(heartbeat, out var validationError))
            {
                return Results.BadRequest(validationError);
            }

            await repository.SaveHeartbeatAsync(heartbeat, cancellationToken);
            store.RecordHeartbeat(heartbeat);
            return Results.Accepted($"/api/agents/heartbeats/{heartbeat.DeviceCode}", heartbeat);
        });

        return endpoints;
    }
}
