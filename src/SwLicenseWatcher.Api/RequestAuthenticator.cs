using Microsoft.AspNetCore.Http;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal static class RequestAuthenticator
{
    internal static async Task<bool> IsAuthorizedAsync(
        HttpContext context,
        string? authorizationHeader,
        ApiSecurityOptions security,
        IJwtAccessTokenAuthenticator jwt,
        CancellationToken cancellationToken)
    {
        var path = context.Request.Path;
        var method = context.Request.Method;
        if (BearerTokenAuthenticator.IsAuthorized(authorizationHeader, security, path, method))
        {
            return true;
        }

        if (!EndpointPolicies.IsAdminEndpoint(path, method))
        {
            return false;
        }

        var jwtResult = await jwt.AuthenticateAdminAsync(authorizationHeader, cancellationToken);
        if (!jwtResult.Succeeded)
        {
            return false;
        }

        if (jwtResult.Principal is not null)
        {
            context.User = jwtResult.Principal;
        }

        return true;
    }
}
