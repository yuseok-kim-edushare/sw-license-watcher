using SwLicenseWatcher.Agent.Watchdog;
using SwLicenseWatcher.Core;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "SwLicenseWatcher Agent Watchdog");
builder.Services.AddOptions<WatchdogOptions>()
    .Bind(builder.Configuration.GetSection("Watchdog"))
    .ValidateOnStart();
builder.Services.AddHttpClient<UpdateManifestClient>();
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();
