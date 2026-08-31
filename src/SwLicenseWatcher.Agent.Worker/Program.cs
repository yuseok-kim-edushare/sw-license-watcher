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
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
        "Agent:ServerBaseUrl must be an absolute http(s) URI. Set it in appsettings.json (or via the Agent__ServerBaseUrl environment variable / --Agent:ServerBaseUrl argument) to point the agent at a remote server.")
    .ValidateOnStart();
builder.Services.AddOptions<LocalStateStoreOptions>()
    .Bind(builder.Configuration.GetSection("LocalState"))
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<LocalStateStoreOptions>>().Value);
builder.Services.AddSingleton<ILocalStateProtector, DpapiLocalStateProtector>();
builder.Services.AddSingleton<ISoftwareInventoryCollector, RegistrySoftwareInventoryCollector>();
builder.Services.AddHttpClient<AgentApiClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<WorkerAgentOptions>>().Value;
    client.BaseAddress = new Uri(options.ServerBaseUrl);
});
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();
