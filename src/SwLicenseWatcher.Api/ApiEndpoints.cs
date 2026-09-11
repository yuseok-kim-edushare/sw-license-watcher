namespace SwLicenseWatcher.Api;

internal static class ApiEndpoints
{
    internal static WebApplication MapApiEndpoints(this WebApplication app)
    {
        app.MapHealthEndpoints();
        app.MapDesignEndpoints();
        app.MapWorkerUpdateEndpoints();
        app.MapInventoryIngestionEndpoints();
        app.MapInventoryQuery();
        app.MapPolicyQuery();
        app.MapPolicyCrudEndpoints();
        app.MapUninstallRequests();
        app.MapUpgradeAuthorizations();
        return app;
    }
}
