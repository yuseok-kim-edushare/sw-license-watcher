using Microsoft.Extensions.Options;
using SwLicenseWatcher.Agent.Worker;
using SwLicenseWatcher.Core;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "SwLicenseWatcher Agent Worker");
builder.Services.AddOptions<WorkerAgentOptions>()
    .Bind(builder.Configuration.GetSection("Agent"))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.DeviceCode),
        "Agent:DeviceCode is required.")
    .Validate(
        options => Uri.TryCreate(options.ServerBaseUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)),
        "Agent:ServerBaseUrl must use HTTPS (HTTP is allowed only for loopback diagnostics).")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ApiToken), "Agent:ApiToken is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.HealthFilePath), "Agent:HealthFilePath is required.")
    .ValidateOnStart();
builder.Services.AddOptions<LocalStateStoreOptions>()
    .Bind(builder.Configuration.GetSection("LocalState"))
    .Validate(options => !string.IsNullOrWhiteSpace(options.QueueDirectory),
        "LocalState:QueueDirectory is required.")
    .Validate(options => options.MaxQueuedSnapshots > 0, "LocalState:MaxQueuedSnapshots must be positive.")
    .Validate(options => options.MaxQueueBytes > 0, "LocalState:MaxQueueBytes must be positive.")
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<LocalStateStoreOptions>>().Value);
builder.Services.AddSingleton<ILocalStateProtector, DpapiLocalStateProtector>();
builder.Services.AddSingleton<ISoftwareInventoryCollector, RegistrySoftwareInventoryCollector>();
builder.Services.AddSingleton<LocalSnapshotQueue>();
builder.Services.AddHttpClient<AgentApiClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<WorkerAgentOptions>>().Value;
    client.BaseAddress = new Uri(options.ServerBaseUrl);
});
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();
