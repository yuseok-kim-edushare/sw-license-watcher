using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Api;
using SwLicenseWatcher.Core;

#if NATIVE_AOT
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseKestrelHttpsConfiguration();
builder.WebHost.UseWebRoot("wwwroot");
#else
var builder = WebApplication.CreateBuilder(args);
#endif
builder.Services.AddWindowsService(options => options.ServiceName = "SwLicenseWatcher.Api");
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
builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection("Database"));
builder.Services.AddOptions<ApiSecurityOptions>()
    .Bind(builder.Configuration.GetSection("Security"))
    .Validate(
        ApiSecurityOptionsValidator.HasAtLeastOneUsableToken,
        "At least one of Security:Token, Security:AgentToken, or Security:AdminToken must contain at least 32 characters.")
    .Validate(
        ApiSecurityOptionsValidator.HasValidConfiguredTokenLengths,
        "Every configured Security token (Token, AgentToken, AdminToken) must contain at least 32 characters.")
    .Validate(
        ApiSecurityOptionsValidator.HasDistinctRoleTokens,
        "Security:AgentToken must differ from Security:AdminToken and from Security:Token.")
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
builder.Services.AddOptions<NotificationOptions>()
    .Bind(builder.Configuration.GetSection("Notifications"))
    .Validate(
        options => !options.Webhook.Enabled
            || (Uri.TryCreate(options.Webhook.Url, UriKind.Absolute, out var webhookUri)
                && (webhookUri.Scheme == Uri.UriSchemeHttps || webhookUri.Scheme == Uri.UriSchemeHttp)),
        "Notifications:Webhook:Url must be an absolute HTTP or HTTPS URI when webhook notifications are enabled.")
    .Validate(
        options => !options.Webhook.Enabled || options.Webhook.Timeout > TimeSpan.Zero,
        "Notifications:Webhook:Timeout must be positive when webhook notifications are enabled.")
    .Validate(
        options => !options.Smtp.Enabled || !string.IsNullOrWhiteSpace(options.Smtp.Host),
        "Notifications:Smtp:Host is required when SMTP notifications are enabled.")
    .Validate(
        options => !options.Smtp.Enabled || options.Smtp.Port is >= 1 and <= 65535,
        "Notifications:Smtp:Port must be between 1 and 65535 when SMTP notifications are enabled.")
    .Validate(
        options => !options.Smtp.Enabled || !string.IsNullOrWhiteSpace(options.Smtp.From),
        "Notifications:Smtp:From is required when SMTP notifications are enabled.")
    .Validate(
        options => !options.Smtp.Enabled || options.Smtp.Recipients.Any(recipient => !string.IsNullOrWhiteSpace(recipient)),
        "Notifications:Smtp:Recipients must contain at least one address when SMTP notifications are enabled.")
    .Validate(
        options => options.StaleHeartbeatThreshold > TimeSpan.Zero,
        "Notifications:StaleHeartbeatThreshold must be positive.")
    .Validate(
        options => options.StaleHeartbeatCheckInterval > TimeSpan.Zero,
        "Notifications:StaleHeartbeatCheckInterval must be positive.")
    .ValidateOnStart();
builder.Services.AddSingleton<SqlServerSchemaScriptBuilder>();
builder.Services.AddSingleton<SqlServerSchemaApplicator>();
builder.Services.AddSingleton<InventoryMemoryStore>();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<SqlServerStorageOptions>>().Value);
builder.Services.AddSingleton<SqlServerInventoryRepository>();
builder.Services.AddSingleton<IStaleHeartbeatNotificationStore>(sp => sp.GetRequiredService<SqlServerInventoryRepository>());
builder.Services.AddHttpClient(WebhookNotificationSender.HttpClientName, (sp, client) =>
{
    var timeout = sp.GetRequiredService<IOptions<NotificationOptions>>().Value.Webhook.Timeout;
    client.Timeout = timeout > TimeSpan.Zero ? timeout : TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<INotificationSender, WebhookNotificationSender>();
builder.Services.AddSingleton<INotificationSender, SmtpNotificationSender>();
builder.Services.AddSingleton<NotificationPublisher>();
builder.Services.AddHostedService<NotificationDispatchService>();
builder.Services.AddHostedService<StaleHeartbeatMonitor>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    try
    {
        await next(context);
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

        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException");
        logger.LogError(ex, "Unhandled exception.");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(
            new ErrorResponse("An unexpected error occurred."),
            ApiJsonSerializerContext.Default.ErrorResponse);
    }
});

app.Use(async (context, next) =>
{
    if (RequestBodySizeLimits.Resolve(context.Request.Path) is { } maxRequestBodySize)
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
        await next(context);
        return;
    }

    var supplied = context.Request.Headers.Authorization.ToString();
    if (!BearerTokenAuthenticator.IsAuthorized(supplied, security, context.Request.Path))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    await next(context);
});

app.UseAdminDashboard();

app.MapGet("/", () => Results.Redirect("/api/design"));
app.MapGet("/health", async (
    SqlServerInventoryRepository repository,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    try
    {
        await repository.ProbeAsync(cancellationToken);
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
    NotificationPublisher notifications,
    CancellationToken cancellationToken) =>
{
    if (!InventorySnapshotValidator.TryValidate(request, out var validationError))
    {
        return Results.BadRequest(validationError);
    }

    var saveResult = await repository.SaveSnapshotAsync(request, cancellationToken);
    store.RecordSnapshot(request);
    notifications.EnqueueNewSoftwareIfNeeded(request, saveResult);
    notifications.EnqueueBlacklistViolationsIfNeeded(request, saveResult);
    return Results.Accepted($"/api/inventory/devices/{request.Pc.DeviceCode}", new SnapshotAcceptedResponse(
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
    if (!InventorySnapshotValidator.TryValidate(heartbeat, out var validationError))
    {
        return Results.BadRequest(validationError);
    }

    await repository.SaveHeartbeatAsync(heartbeat, cancellationToken);
    store.RecordHeartbeat(heartbeat);
    return Results.Accepted($"/api/agents/heartbeats/{heartbeat.DeviceCode}", heartbeat);
});

app.MapInventoryQuery();
app.MapPolicyQuery();

app.MapGet("/api/policies/{id:long}", async (long id, SqlServerInventoryRepository repository, CancellationToken cancellationToken) =>
{
    var policy = await repository.GetPolicyAsync(id, cancellationToken);
    return policy is null ? Results.NotFound() : Results.Ok(policy);
});
app.MapPost("/api/policies", async (
    SoftwarePolicyWriteRequest request,
    SqlServerInventoryRepository repository,
    CancellationToken cancellationToken) =>
{
    if (!SoftwarePolicyValidator.TryValidate(request, out var validationError))
    {
        return Results.BadRequest(validationError);
    }

    var created = await repository.CreatePolicyAsync(request, cancellationToken);
    return Results.Created($"/api/policies/{created.Id}", created);
});
app.MapPut("/api/policies/{id:long}", async (
    long id,
    SoftwarePolicyWriteRequest request,
    SqlServerInventoryRepository repository,
    CancellationToken cancellationToken) =>
{
    if (!SoftwarePolicyValidator.TryValidate(request, out var validationError))
    {
        return Results.BadRequest(validationError);
    }

    var updated = await repository.UpdatePolicyAsync(id, request, cancellationToken);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
});
app.MapDelete("/api/policies/{id:long}", async (long id, SqlServerInventoryRepository repository, CancellationToken cancellationToken) =>
    await repository.DeletePolicyAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound());

await app.Services.GetRequiredService<SqlServerSchemaApplicator>()
    .ApplyIfEnabledAsync(CancellationToken.None);

app.Run();
