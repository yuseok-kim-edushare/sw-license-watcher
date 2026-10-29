using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal static class ServerUpdateEndpoints
{
    internal static IEndpointRouteBuilder MapServerUpdateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(AgentPaths.ServerUpdate, async (
            ServerUpdateCoordinator updates,
            CancellationToken cancellationToken) =>
            Results.Ok(await updates.GetStatusAsync(cancellationToken)));

        endpoints.MapPost(AgentPaths.ServerUpdateGitHub, async (
            ServerUpdateImportRequest? request,
            ServerUpdateCoordinator updates,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await updates.ImportAsync(request ?? new ServerUpdateImportRequest(), cancellationToken));
            }
            catch (Exception ex) when (ex is ServerUpdateException or GitHubReleaseImportException or HttpRequestException or InvalidDataException)
            {
                return ServerUpdateResults.From(ex);
            }
        });

        endpoints.MapPost(AgentPaths.ServerUpdateApply, async (
            ServerUpdateApplyRequest? request,
            ServerUpdateCoordinator updates,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Json(
                    await updates.ApplyAsync(request ?? new ServerUpdateApplyRequest(), cancellationToken),
                    ApiJsonSerializerContext.Default.ServerUpdateApplyResponse,
                    statusCode: StatusCodes.Status202Accepted);
            }
            catch (Exception ex) when (ex is ServerUpdateException or GitHubReleaseImportException or HttpRequestException or InvalidDataException)
            {
                return ServerUpdateResults.From(ex);
            }
        });

        return endpoints;
    }
}

internal static class ServerUpdateResults
{
    public static IResult From(Exception exception)
    {
        var (status, message) = exception switch
        {
            ServerUpdateException update => (update.StatusCode, update.Message),
            GitHubReleaseImportException github => ((int)github.StatusCode, github.Message),
            InvalidDataException invalid => (StatusCodes.Status502BadGateway, invalid.Message),
            HttpRequestException => (StatusCodes.Status502BadGateway, "The API could not reach GitHub to download the release."),
            _ => (StatusCodes.Status500InternalServerError, "The server update could not be completed.")
        };
        return TypedResults.Json(
            new ErrorResponse(message),
            ApiJsonSerializerContext.Default.ErrorResponse,
            statusCode: status);
    }
}
