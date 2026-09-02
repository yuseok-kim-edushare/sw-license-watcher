namespace SwLicenseWatcher.Api;

internal static class AdminStaticFiles
{
    internal const string ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'";

    internal static void UseAdminDashboard(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Use(async (context, next) =>
        {
            if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) &&
                PublicPaths.IsAdminIndex(context.Request.Path))
            {
                context.Request.Path = "/admin/index.html";
            }

            await next(context);
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                if (!PublicPaths.IsAdminAsset(context.Context.Request.Path))
                {
                    return;
                }

                ApplySecurityHeaders(context.Context.Response.Headers);
            }
        });
    }

    internal static void ApplySecurityHeaders(IHeaderDictionary headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        headers["Content-Security-Policy"] = ContentSecurityPolicy;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cache-Control"] = "no-store";
    }
}
