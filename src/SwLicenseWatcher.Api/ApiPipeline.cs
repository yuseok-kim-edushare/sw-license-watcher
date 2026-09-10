using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;
using SwLicenseWatcher.Infrastructure.SqlServer;

namespace SwLicenseWatcher.Api;

internal static class ApiPipeline
{
    internal static WebApplication UseApiPipeline(this WebApplication app)
    {
        app.Use(ExceptionHandlingMiddleware.InvokeAsync);
        app.Use(RequestPolicyMiddleware.InvokeAsync);
        app.UseAdminDashboard();
        return app;
    }

    internal static async Task InitializeApiAsync(this WebApplication app)
    {
        await app.Services.GetRequiredService<SqlServerSchemaApplicator>()
            .ApplyIfEnabledAsync(CancellationToken.None);
        await app.Services.GetRequiredService<WorkerUpdatePinService>()
            .SeedIfEmptyAsync(CancellationToken.None);
    }
}

internal static class ExceptionHandlingMiddleware
{
    internal static async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        try
        {
            await next();
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("UnhandledException");
            logger.LogError(ex, "Unhandled exception.");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(
                new ErrorResponse("An unexpected error occurred."),
                ApiJsonSerializerContext.Default.ErrorResponse);
        }
    }
}

internal static class RequestPolicyMiddleware
{
    internal static async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        if (EndpointPolicies.GetRequestBodySize(context.Request.Path) is { } maxRequestBodySize)
        {
            var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false })
            {
                feature.MaxRequestBodySize = maxRequestBodySize;
            }
        }

        var security = context.RequestServices.GetRequiredService<IOptions<ApiSecurityOptions>>().Value;
        if (security.RequireHttps && !context.Request.IsHttps &&
            (context.Connection.RemoteIpAddress is null ||
             !System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress)))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("HTTPS is required.");
            return;
        }

        if (PublicPaths.IsAnonymous(context.Request.Path))
        {
            await next();
            return;
        }

        var supplied = context.Request.Headers.Authorization.ToString();
        if (!BearerTokenAuthenticator.IsAuthorized(
                supplied,
                security,
                context.Request.Path,
                context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next();
    }
}
