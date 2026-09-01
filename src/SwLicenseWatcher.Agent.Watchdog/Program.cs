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
            && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback),
        "Watchdog:ServerBaseUrl must use HTTPS (HTTP is allowed only for loopback diagnostics).")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ApiToken), "Watchdog:ApiToken is required.")
    .ValidateOnStart();
builder.Services.AddHttpClient<UpdateManifestClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<WatchdogOptions>>().Value;
    client.BaseAddress = new Uri(options.ServerBaseUrl);
});
builder.Services.AddHttpClient<WorkerUpdateManager>();
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();
