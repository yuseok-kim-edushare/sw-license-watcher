using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal static class UninstallQueryApi
{
    public static void MapUninstallRequests(this WebApplication app)
    {
        app.MapPost("/api/agents/uninstall-requests", async (
            UninstallRequestCreateRequest request,
            IUninstallRequestStore repository,
            CancellationToken cancellationToken) =>
        {
            if (!TryValidateDeviceCode(request.DeviceCode, out var deviceCode, out var error))
            {
                return Results.BadRequest(error);
            }

            var created = await repository.CreateUninstallRequestAsync(deviceCode, cancellationToken);
            return created is null
                ? Results.NotFound("The device is not registered.")
                : Results.Created($"/api/agents/uninstall-requests/{created.Id}", created);
        });

        app.MapGet("/api/agents/uninstall-requests/{id:long}", async (
            long id,
            string? deviceCode,
            IUninstallRequestStore repository,
            CancellationToken cancellationToken) =>
        {
            if (!TryValidateDeviceCode(deviceCode, out var normalizedDeviceCode, out var error))
            {
                return Results.BadRequest(error);
            }

            var item = await repository.GetAgentUninstallRequestAsync(id, normalizedDeviceCode, cancellationToken);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        app.MapPost("/api/agents/uninstall-requests/{id:long}/consume", async (
            long id,
            UninstallRequestConsumeRequest request,
            IUninstallRequestStore repository,
            CancellationToken cancellationToken) =>
        {
            if (!TryValidateDeviceCode(request.DeviceCode, out var deviceCode, out var deviceError))
            {
                return Results.BadRequest(deviceError);
            }

            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return Results.BadRequest("code is required.");
            }

            return await repository.ConsumeUninstallRequestAsync(id, deviceCode, request.Code, cancellationToken)
                ? Results.NoContent()
                : Results.Conflict("The uninstall grant is not valid.");
        });

        app.MapGet("/api/uninstall-requests", async (
            IUninstallRequestStore repository,
            int? skip,
            int? take,
            string? search,
            CancellationToken cancellationToken) =>
        {
            if (!QueryList.TryValidateSearch(search, out var searchError))
            {
                return Results.BadRequest(searchError);
            }

            var (normalizedSkip, normalizedTake) = QueryList.NormalizePaging(skip, take, csv: false);
            var (totalCount, items) = await repository.ListUninstallRequestsAsync(
                normalizedSkip, normalizedTake, search, cancellationToken);
            return Results.Ok(new UninstallRequestListResponse(normalizedSkip, normalizedTake, totalCount, items));
        });

        app.MapPost("/api/uninstall-requests/{id:long}/approve", async (
            long id,
            IUninstallRequestStore repository,
            CancellationToken cancellationToken) =>
            await repository.ApproveUninstallRequestAsync(id, cancellationToken)
                ? Results.NoContent()
                : Results.Conflict("The uninstall request is not pending."));

        app.MapPost("/api/uninstall-requests/{id:long}/deny", async (
            long id,
            IUninstallRequestStore repository,
            CancellationToken cancellationToken) =>
            await repository.DenyUninstallRequestAsync(id, cancellationToken)
                ? Results.NoContent()
                : Results.Conflict("The uninstall request is not pending."));
    }

    internal static bool TryValidateDeviceCode(string? deviceCode, out string normalized, out string error)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            normalized = string.Empty;
            error = "deviceCode is required.";
            return false;
        }

        var trimmed = deviceCode.Trim();
        if (trimmed.Length > 128)
        {
            normalized = string.Empty;
            error = "deviceCode must be at most 128 characters.";
            return false;
        }

        normalized = trimmed;

        error = string.Empty;
        return true;
    }
}
