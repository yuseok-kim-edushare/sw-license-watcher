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
            IDeviceQuery devices,
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
            var assignedHostName = await devices.GetAssignedHostNameAsync(request.Pc.DeviceCode, cancellationToken);
            return Results.Accepted($"/api/inventory/devices/{request.Pc.DeviceCode}", new SnapshotAcceptedResponse(
                request.Pc.DeviceCode,
                request.InstalledSoftware.Count,
                request.CollectedAtUtc,
                assignedHostName));
        });
        endpoints.MapPost(AgentPaths.Heartbeats, async (
            AgentHeartbeat heartbeat,
            InventoryMemoryStore store,
            IHeartbeatRepository repository,
            IDeviceQuery devices,
            CancellationToken cancellationToken) =>
        {
            if (!InventorySnapshotValidator.TryValidate(heartbeat, out var validationError))
            {
                return Results.BadRequest(validationError);
            }

            await repository.SaveHeartbeatAsync(heartbeat, cancellationToken);
            store.RecordHeartbeat(heartbeat);
            var assignedHostName = await devices.GetAssignedHostNameAsync(heartbeat.DeviceCode, cancellationToken);
            return Results.Accepted($"/api/agents/heartbeats/{heartbeat.DeviceCode}", new AgentHeartbeatAcceptedResponse(
                heartbeat.DeviceCode,
                heartbeat.HostName,
                heartbeat.ServiceName,
                heartbeat.Version,
                heartbeat.ReportedAtUtc,
                heartbeat.Status,
                assignedHostName));
        });

        return endpoints;
    }
}
