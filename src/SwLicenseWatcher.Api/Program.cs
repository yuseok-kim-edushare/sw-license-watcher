using Microsoft.Extensions.Options;
using SwLicenseWatcher.Api;
using SwLicenseWatcher.Core;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonSerializerContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Insert(1, InventoryJsonSerializerContext.Default);
});
builder.Services.AddOptions<SqlServerStorageOptions>()
    .Bind(builder.Configuration.GetSection("Storage:SqlServer"))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.SchemaName),
        "Storage:SqlServer:SchemaName is required.")
    .ValidateOnStart();
builder.Services.AddOptions<UpdateManifestOptions>()
    .Bind(builder.Configuration.GetSection("Updates:Worker"))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.PackageUrl),
        "Updates:Worker:PackageUrl is required.")
    .Validate(
        options => Uri.TryCreate(options.PackageUrl, UriKind.Absolute, out _),
        "Updates:Worker:PackageUrl must be an absolute URI.")
    .ValidateOnStart();
builder.Services.AddSingleton<SqlServerSchemaScriptBuilder>();
builder.Services.AddSingleton<InventoryMemoryStore>();

var app = builder.Build();

app.MapGet("/", () => Results.Redirect("/api/design"));
app.MapGet("/health", () => Results.Ok(new HealthResponse("Healthy", DateTimeOffset.UtcNow)));
app.MapGet("/api/schema/sql", (IOptions<SqlServerStorageOptions> options, SqlServerSchemaScriptBuilder schemaBuilder) =>
    Results.Text(schemaBuilder.Build(options.Value), "text/plain"));
app.MapGet("/api/design", (
    IOptions<SqlServerStorageOptions> sqlOptions,
    IOptions<UpdateManifestOptions> updateOptions,
    SqlServerSchemaScriptBuilder schemaBuilder,
    InventoryMemoryStore store) =>
    Results.Ok(new DesignResponse(
        new DesignArchitecture(
            Agent: "Worker + Watchdog two-process Windows service architecture",
            LocalState: "ESENT-backed agent metadata/checkpoints protected with DPAPI",
            InventoryCollection: "Registry uninstall keys only (HKLM 64-bit, HKLM 32-bit, HKCU). Win32_Product/WMI is intentionally not used.",
            UpdateSafety: "Jittered polling, SHA-256/Authenticode verification, staged backup, automatic rollback on heartbeat timeout"),
        new DesignSqlServer(
            HasConnectionStringConfigured: !string.IsNullOrWhiteSpace(sqlOptions.Value.ConnectionString),
            SchemaName: sqlOptions.Value.SchemaName,
            SchemaScript: schemaBuilder.Build(sqlOptions.Value)),
        new DesignCounts(store.SnapshotCount, store.HeartbeatCount),
        updateOptions.Value.ToManifest())));
app.MapGet("/api/updates/worker/manifest", (IOptions<UpdateManifestOptions> options) => Results.Ok(options.Value.ToManifest()));
app.MapPost("/api/inventory/snapshots", (InventoryIngestionRequest request, InventoryMemoryStore store) =>
{
    store.RecordSnapshot(request);
    return Results.Accepted($"/api/inventory/snapshots/{request.Pc.DeviceCode}", new SnapshotAcceptedResponse(
        request.Pc.DeviceCode,
        request.InstalledSoftware.Count,
        request.CollectedAtUtc));
});
app.MapPost("/api/agents/heartbeats", (AgentHeartbeat heartbeat, InventoryMemoryStore store) =>
{
    store.RecordHeartbeat(heartbeat);
    return Results.Accepted($"/api/agents/heartbeats/{heartbeat.DeviceCode}", heartbeat);
});

app.Run();
