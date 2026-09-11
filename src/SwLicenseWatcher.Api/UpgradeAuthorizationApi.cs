using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal static class UpgradeAuthorizationApi
{
    public static void MapUpgradeAuthorizations(this WebApplication app)
    {
        app.MapPost(AgentPaths.UpgradeAuthorizations, async (
            DeviceUpgradeAuthorizationRequest request,
            DeviceEnrollmentService enrollment,
            CancellationToken cancellationToken) =>
        {
            if (!UninstallQueryApi.TryValidateDeviceCode(request.DeviceCode, out var deviceCode, out var error))
            {
                return Results.BadRequest(error);
            }

            var (response, identityError) = await enrollment.AuthorizeUpgradeAsync(
                request with { DeviceCode = deviceCode },
                cancellationToken);
            if (identityError is not null)
            {
                return Results.BadRequest(identityError);
            }

            return response is null
                ? Results.NotFound("The device is not registered.")
                : Results.Ok(response);
        });
    }
}
