using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal static class HealthEndpoints
{
    internal static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", async (
            IHealthProbe healthProbe,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await healthProbe.ProbeAsync(cancellationToken);
                return Results.Ok(new HealthResponse("Healthy", DateTimeOffset.UtcNow));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("Health").LogError(ex, "The SQL Server health probe failed.");
                return TypedResults.Json(
                    new HealthResponse("Unhealthy", DateTimeOffset.UtcNow, "Database is unavailable."),
                    ApiJsonSerializerContext.Default.HealthResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        return endpoints;
    }
}
