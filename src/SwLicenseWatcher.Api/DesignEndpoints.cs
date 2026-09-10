using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;
using SwLicenseWatcher.Infrastructure.SqlServer;

namespace SwLicenseWatcher.Api;

internal static class DesignEndpoints
{
    internal static IEndpointRouteBuilder MapDesignEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", () => Results.Redirect("/api/design"));
        endpoints.MapGet("/api/schema", (
            IOptions<DatabaseOptions> database,
            SqlServerSchemaApplicator applicator) =>
        {
            var last = applicator.Last;
            return Results.Ok(new SchemaStatusResponse(
                database.Value.ApplySchemaOnStartup,
                database.Value.ApplySchemaInBackground,
                last.Succeeded,
                last.BatchCount,
                last.LastAttemptUtc,
                last.LastSucceededUtc,
                last.LastError));
        });
        endpoints.MapGet("/api/schema/sql", (
            IOptions<SqlServerStorageOptions> options,
            SqlServerSchemaScriptBuilder schemaBuilder) =>
            Results.Text(schemaBuilder.Build(options.Value), "text/plain"));
        endpoints.MapPost("/api/schema/sql", async (
            SqlServerSchemaApplicator applicator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var batchCount = await applicator.ApplyAsync(cancellationToken);
                return Results.Ok(new SchemaApplyResponse(true, batchCount, DateTimeOffset.UtcNow));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return TypedResults.Json(
                    new SchemaApplyResponse(false, 0, DateTimeOffset.UtcNow, ex.Message),
                    ApiJsonSerializerContext.Default.SchemaApplyResponse,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
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
