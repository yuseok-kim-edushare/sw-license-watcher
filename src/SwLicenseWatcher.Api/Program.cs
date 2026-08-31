using Microsoft.Extensions.Options;
using SwLicenseWatcher.Api;
using SwLicenseWatcher.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptions<SqlServerStorageOptions>()
    .Bind(builder.Configuration.GetSection("Storage:SqlServer"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<UpdateManifestOptions>()
    .Bind(builder.Configuration.GetSection("Updates:Worker"))
    .ValidateDataAnnotations()
    .Validate(
        options => Uri.TryCreate(options.PackageUrl, UriKind.Absolute, out _),
        "Updates:Worker:PackageUrl must be an absolute URI.")
    .ValidateOnStart();
builder.Services.AddSingleton<SqlServerSchemaScriptBuilder>();
builder.Services.AddSingleton<InventoryMemoryStore>();

var app = builder.Build();

app.MapGet("/", () => Results.Redirect("/api/design"));
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", utc = DateTimeOffset.UtcNow }));
app.MapGet("/api/schema/sql", (IOptions<SqlServerStorageOptions> options, SqlServerSchemaScriptBuilder schemaBuilder) =>
    Results.Text(schemaBuilder.Build(options.Value), "text/plain"));
app.MapGet("/api/design", (
    IOptions<SqlServerStorageOptions> sqlOptions,
    IOptions<UpdateManifestOptions> updateOptions,
    SqlServerSchemaScriptBuilder schemaBuilder,
    InventoryMemoryStore store) =>
    Results.Ok(new
    {
        Architecture = new
        {
            Agent = "Worker + Watchdog two-process Windows service architecture",
            LocalState = "ESENT-backed agent metadata/checkpoints protected with DPAPI",
            InventoryCollection = "Registry uninstall keys only (HKLM 64-bit, HKLM 32-bit, HKCU). Win32_Product/WMI is intentionally not used.",
            UpdateSafety = "Jittered polling, SHA-256/Authenticode verification, staged backup, automatic rollback on heartbeat timeout"
        },
        SqlServer = new
        {
            HasConnectionStringConfigured = !string.IsNullOrWhiteSpace(sqlOptions.Value.ConnectionString),
            sqlOptions.Value.SchemaName,
            SchemaScript = schemaBuilder.Build(sqlOptions.Value)
        },
        LatestCounts = new
        {
            store.SnapshotCount,
            store.HeartbeatCount
        },
        WorkerManifest = updateOptions.Value.ToManifest()
    }));
app.MapGet("/api/updates/worker/manifest", (IOptions<UpdateManifestOptions> options) => Results.Ok(options.Value.ToManifest()));
app.MapPost("/api/inventory/snapshots", (InventoryIngestionRequest request, InventoryMemoryStore store) =>
{
    store.RecordSnapshot(request);
    return Results.Accepted($"/api/inventory/snapshots/{request.Pc.DeviceCode}", new
    {
        request.Pc.DeviceCode,
        InstalledSoftwareCount = request.InstalledSoftware.Count,
        request.CollectedAtUtc
    });
});
app.MapPost("/api/agents/heartbeats", (AgentHeartbeat heartbeat, InventoryMemoryStore store) =>
{
    store.RecordHeartbeat(heartbeat);
    return Results.Accepted($"/api/agents/heartbeats/{heartbeat.DeviceCode}", heartbeat);
});

app.Run();
