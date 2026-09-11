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
            DeviceEnrollmentService enrollment,
            CancellationToken cancellationToken) =>
        {
            if (!InventorySnapshotValidator.TryValidate(request, out var validationError))
            {
                return Results.BadRequest(validationError);
            }

            if (!enrollment.TryAccept(
                    request.Pc.DeviceId,
                    request.Pc.DevicePublicKey,
                    request.Pc.DeviceCertificate,
                    request.Pc.DeviceProof,
                    request.Pc.DeviceCode,
                    out var identityError))
            {
                return Results.BadRequest(identityError);
            }

            var saveResult = await repository.SaveSnapshotAsync(request, cancellationToken);
            store.RecordSnapshot(request);
            notifications.EnqueueNewSoftwareIfNeeded(request, saveResult);
            notifications.EnqueueBlacklistViolationsIfNeeded(request, saveResult);
            var assignment = await enrollment.CompleteAsync(
                request.Pc.DeviceCode, request.Pc.DeviceId, request.Pc.DevicePublicKey, cancellationToken);
            return Results.Accepted($"/api/inventory/devices/{assignment?.DeviceCode ?? request.Pc.DeviceCode}", new SnapshotAcceptedResponse(
                assignment?.DeviceCode ?? request.Pc.DeviceCode,
                request.InstalledSoftware.Count,
                request.CollectedAtUtc,
                assignment?.AssignedHostName,
                assignment?.DeviceCode,
                assignment?.DeviceId,
                assignment?.DeviceCertificate));
        });
        endpoints.MapPost(AgentPaths.Heartbeats, async (
            AgentHeartbeat heartbeat,
            InventoryMemoryStore store,
            IHeartbeatRepository repository,
            DeviceEnrollmentService enrollment,
            CancellationToken cancellationToken) =>
        {
            if (!InventorySnapshotValidator.TryValidate(heartbeat, out var validationError))
            {
                return Results.BadRequest(validationError);
            }

            if (!enrollment.TryAccept(
                    heartbeat.DeviceId,
                    heartbeat.DevicePublicKey,
                    heartbeat.DeviceCertificate,
                    heartbeat.DeviceProof,
                    heartbeat.DeviceCode,
                    out var identityError))
            {
                return Results.BadRequest(identityError);
            }

            await repository.SaveHeartbeatAsync(heartbeat, cancellationToken);
            store.RecordHeartbeat(heartbeat);
            var assignment = await enrollment.CompleteAsync(
                heartbeat.DeviceCode, heartbeat.DeviceId, heartbeat.DevicePublicKey, cancellationToken);
            return Results.Accepted($"/api/agents/heartbeats/{assignment?.DeviceCode ?? heartbeat.DeviceCode}", new AgentHeartbeatAcceptedResponse(
                assignment?.DeviceCode ?? heartbeat.DeviceCode,
                heartbeat.HostName,
                heartbeat.ServiceName,
                heartbeat.Version,
                heartbeat.ReportedAtUtc,
                heartbeat.Status,
                assignment?.AssignedHostName,
                assignment?.DeviceCode,
                assignment?.DeviceId,
                assignment?.DeviceCertificate));
        });

        return endpoints;
    }
}
