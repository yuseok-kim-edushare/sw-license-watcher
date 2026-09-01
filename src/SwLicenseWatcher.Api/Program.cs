using Microsoft.Extensions.Options;
using SwLicenseWatcher.Api;
using SwLicenseWatcher.Core;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonSerializerContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Insert(1, InventoryJsonSerializerContext.Default);
});
builder.Services.AddOptions<SqlServerStorageOptions>()
    .Bind(builder.Configuration.GetSection("Storage:SqlServer"))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.ConnectionString),
        "Storage:SqlServer:ConnectionString is required.")
    .Validate(options =>
    {
        try
        {
            SqlIdentifierValidator.Validate(options);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }, "All SQL identifiers must use letters, digits, and underscores, start with a letter or underscore, and be at most 128 characters.")
    .ValidateOnStart();
builder.Services.AddOptions<ApiSecurityOptions>()
    .Bind(builder.Configuration.GetSection("Security"))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Token) && options.Token.Length >= 32,
        "Security:Token must contain at least 32 characters.")
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
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<SqlServerStorageOptions>>().Value);
builder.Services.AddSingleton<SqlServerInventoryRepository>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    var security = context.RequestServices.GetRequiredService<IOptions<ApiSecurityOptions>>().Value;
    if (security.RequireHttps && !context.Request.IsHttps &&
        (context.Connection.RemoteIpAddress is null ||
         !System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress)))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("HTTPS is required.");
        return;
    }

    if (context.Request.Path == "/health")
    {
        await next(context);
        return;
    }

    var supplied = context.Request.Headers.Authorization.ToString();
    var expected = string.Concat("Bearer ", security.Token);
    var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
    var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
    if (!CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    await next(context);
});

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
            LocalState: "DPAPI-protected durable store-and-forward snapshot queue",
            InventoryCollection: "Registry uninstall keys only (HKLM 64-bit, HKLM 32-bit, HKCU, loaded HKEY_USERS profiles). Win32_Product/WMI is intentionally not used.",
            UpdateSafety: "Jittered polling, HTTPS download, SHA-256/Authenticode verification, safe ZIP extraction, staged backup, health check, and automatic rollback"),
        new DesignSqlServer(
            HasConnectionStringConfigured: !string.IsNullOrWhiteSpace(sqlOptions.Value.ConnectionString),
            SchemaName: sqlOptions.Value.SchemaName,
            SchemaScript: schemaBuilder.Build(sqlOptions.Value)),
        new DesignCounts(store.SnapshotCount, store.HeartbeatCount),
        updateOptions.Value.ToManifest())));
app.MapGet("/api/updates/worker/manifest", (IOptions<UpdateManifestOptions> options) => Results.Ok(options.Value.ToManifest()));
app.MapPost("/api/inventory/snapshots", async (
    InventoryIngestionRequest request,
    InventoryMemoryStore store,
    SqlServerInventoryRepository repository,
    CancellationToken cancellationToken) =>
{
    await repository.SaveSnapshotAsync(request, cancellationToken);
    store.RecordSnapshot(request);
    return Results.Accepted($"/api/inventory/snapshots/{request.Pc.DeviceCode}", new SnapshotAcceptedResponse(
        request.Pc.DeviceCode,
        request.InstalledSoftware.Count,
        request.CollectedAtUtc));
});
app.MapPost("/api/agents/heartbeats", async (
    AgentHeartbeat heartbeat,
    InventoryMemoryStore store,
    SqlServerInventoryRepository repository,
    CancellationToken cancellationToken) =>
{
    await repository.SaveHeartbeatAsync(heartbeat, cancellationToken);
    store.RecordHeartbeat(heartbeat);
    return Results.Accepted($"/api/agents/heartbeats/{heartbeat.DeviceCode}", heartbeat);
});

app.Run();
