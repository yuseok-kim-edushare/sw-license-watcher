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
        options => Uri.TryCreate(options.ServerBaseUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
        "Watchdog:ServerBaseUrl must be an absolute http(s) URI. Set it in appsettings.json (or via the Watchdog__ServerBaseUrl environment variable / --Watchdog:ServerBaseUrl argument) to point the watchdog at a remote server.")
    .ValidateOnStart();
builder.Services.AddHttpClient<UpdateManifestClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<WatchdogOptions>>().Value;
    client.BaseAddress = new Uri(options.ServerBaseUrl);
});
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();
