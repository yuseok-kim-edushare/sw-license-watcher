using Microsoft.Extensions.Options;
using SwLicenseWatcher.Agent.Watchdog;
using SwLicenseWatcher.Core;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "SwLicenseWatcher Agent Watchdog");
builder.Services.AddOptions<WatchdogOptions>()
    .Bind(builder.Configuration.GetSection("Watchdog"))
    .ValidateOnStart();
builder.Services.AddHttpClient<UpdateManifestClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<WatchdogOptions>>().Value;
    client.BaseAddress = new Uri(options.ServerBaseUrl);
});
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();
