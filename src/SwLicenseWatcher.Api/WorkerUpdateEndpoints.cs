using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal static class WorkerUpdateEndpoints
{
    internal static IEndpointRouteBuilder MapWorkerUpdateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(AgentPaths.WorkerManifest, async (
            HttpContext http,
            WorkerUpdatePinService workerPin,
            WorkerUpdatePackageStore packages,
            CancellationToken cancellationToken) =>
        {
            var pin = await workerPin.GetEffectiveAsync(cancellationToken);
            return Results.Ok(WorkerUpdatePackageUrls.WithLocalPackageUrlIfCached(pin, http.Request, packages));
        });
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
        endpoints.MapGet(AgentPaths.WorkerPackage + "/{version}", (
            string version,
            WorkerUpdatePackageStore packages) =>
        {
            if (!packages.TryGetPackagePath(version, out var path))
            {
                return Results.NotFound();
            }

            return Results.File(
                path,
                contentType: "application/zip",
                fileDownloadName: Path.GetFileName(path),
                enableRangeProcessing: true);
        });
        endpoints.MapGet(AgentPaths.WorkerGitHub, async (
            GitHubWorkerUpdateImporter importer,
            CancellationToken cancellationToken) =>
            Results.Ok(await importer.GetSourceAsync(cancellationToken)));
        endpoints.MapPost(AgentPaths.WorkerGitHub, async (
            GitHubWorkerUpdateImportRequest? request,
            HttpContext http,
            GitHubWorkerUpdateImporter importer,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var imported = await importer.ImportAsync(
                    request ?? new GitHubWorkerUpdateImportRequest(),
                    version => WorkerUpdatePackageUrls.ForRequest(http.Request, version),
                    cancellationToken);
                return Results.Ok(imported);
            }
            catch (GitHubReleaseImportException ex)
            {
                return TypedResults.Json(
                    new ErrorResponse(ex.Message),
                    ApiJsonSerializerContext.Default.ErrorResponse,
                    statusCode: (int)ex.StatusCode);
            }
            catch (HttpRequestException)
            {
                return TypedResults.Json(
                    new ErrorResponse("The API could not reach GitHub to download the release."),
                    ApiJsonSerializerContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });

        return endpoints;
    }
}
