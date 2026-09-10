using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal static class DesignEndpoints
{
    internal static IEndpointRouteBuilder MapDesignEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", () => Results.Redirect("/api/design"));
        endpoints.MapGet("/api/schema/sql", (
            IOptions<SqlServerStorageOptions> options,
            SqlServerSchemaScriptBuilder schemaBuilder) =>
            Results.Text(schemaBuilder.Build(options.Value), "text/plain"));
        endpoints.MapGet("/api/design", async (
            IOptions<SqlServerStorageOptions> sqlOptions,
            WorkerUpdatePinService workerPin,
            SqlServerSchemaScriptBuilder schemaBuilder,
            InventoryMemoryStore store,
            CancellationToken cancellationToken) =>
            Results.Ok(new DesignResponse(
                new DesignArchitecture(
                    Agent: "Worker + Watchdog two-process Windows service architecture",
                    LocalState: "DPAPI-protected durable store-and-forward snapshot queue",
                    InventoryCollection: "Registry uninstall keys only (HKLM 64-bit, HKLM 32-bit, HKCU, loaded HKEY_USERS profiles). Win32_Product/WMI is intentionally not used.",
                    UpdateSafety: "Jittered polling, HTTPS download, SHA-256/Authenticode verification, safe ZIP extraction, staged backup, health check, and automatic rollback"),
                new DesignSqlServer(
                    HasConnectionStringConfigured: !string.IsNullOrWhiteSpace(sqlOptions.Value.ConnectionString),
                    SchemaName: sqlOptions.Value.SchemaName,
                    SchemaScript: schemaBuilder.Build(sqlOptions.Value)),
                new DesignCounts(store.SnapshotCount, store.HeartbeatCount),
                await workerPin.GetEffectiveAsync(cancellationToken))));

        return endpoints;
    }
}
