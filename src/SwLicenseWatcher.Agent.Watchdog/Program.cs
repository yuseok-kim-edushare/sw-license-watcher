using Microsoft.Extensions.Options;
using SwLicenseWatcher.Agent.Watchdog;
using SwLicenseWatcher.Core;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "SwLicenseWatcher Agent Watchdog");
builder.Services.AddOptions<WatchdogOptions>()
    .Bind(builder.Configuration.GetSection("Watchdog"))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.DeviceCode),
        "Watchdog:DeviceCode is required.")
    .Validate(
        options => Uri.TryCreate(options.ServerBaseUrl, UriKind.Absolute, out _),
        "Watchdog:ServerBaseUrl must be an absolute URI.")
    .ValidateOnStart();
builder.Services.AddHttpClient<UpdateManifestClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<WatchdogOptions>>().Value;
    client.BaseAddress = new Uri(options.ServerBaseUrl);
});
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();
