using Microsoft.Extensions.Options;
using SwLicenseWatcher.Agent.Watchdog;
using SwLicenseWatcher.Core;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "SwLicenseWatcher.Agent.Watchdog");
builder.Services.AddOptions<WatchdogOptions>()
    .Bind(builder.Configuration.GetSection("Watchdog"))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.DeviceCode),
        "Watchdog:DeviceCode is required.")
    .Validate(
        options => Uri.TryCreate(options.ServerBaseUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)),
        "Watchdog:ServerBaseUrl must use HTTPS (HTTP is allowed only for loopback diagnostics).")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ApiToken), "Watchdog:ApiToken is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.WorkerHealthFilePath),
        "Watchdog:WorkerHealthFilePath is required to verify Worker health after an update.")
    .Validate(WatchdogOptionsValidator.HasSafeDirectories,
        "Watchdog staging, backup, and Worker install directories must be distinct, non-root, and non-overlapping.")
    .Validate(options => options.MaxPackageBytes > 0 && options.MaxExtractedBytes >= options.MaxPackageBytes,
        "Watchdog package limits must be positive and MaxExtractedBytes must be at least MaxPackageBytes.")
    .ValidateOnStart();
builder.Services.AddHttpClient<UpdateManifestClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<WatchdogOptions>>().Value;
    client.BaseAddress = new Uri(options.ServerBaseUrl);
});
builder.Services.AddHttpClient<WorkerUpdateManager>();
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();
