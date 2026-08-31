using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Watchdog;

public sealed class UpdateManifestClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UpdateManifestClient> _logger;
    private readonly WatchdogOptions _options;

    public UpdateManifestClient(ILogger<UpdateManifestClient> logger, IOptions<WatchdogOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_options.ServerBaseUrl, UriKind.Absolute)
        };
    }

    public async Task<UpdateManifest?> TryGetManifestAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<UpdateManifest>(_options.ManifestPath, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Failed to fetch update manifest from {ManifestPath}.", _options.ManifestPath);
            return null;
        }
    }
}
