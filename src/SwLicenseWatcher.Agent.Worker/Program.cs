using Microsoft.Extensions.Options;
using SwLicenseWatcher.Agent.Worker;
using SwLicenseWatcher.Core;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "SwLicenseWatcher Agent Worker");
builder.Services.AddOptions<WorkerAgentOptions>()
    .Bind(builder.Configuration.GetSection("Agent"))
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
