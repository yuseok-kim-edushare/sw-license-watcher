using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Worker;

public sealed class AgentApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AgentApiClient> _logger;
    private readonly WorkerAgentOptions _options;

    public AgentApiClient(HttpClient httpClient, ILogger<AgentApiClient> logger, IOptions<WorkerAgentOptions> options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    public Task PublishSnapshotAsync(InventoryIngestionRequest snapshot, CancellationToken cancellationToken) =>
        PostAsync(_options.SnapshotPath, snapshot, cancellationToken);

    public Task PublishHeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken) =>
        PostAsync(_options.HeartbeatPath, heartbeat, cancellationToken);

    private async Task PostAsync<TPayload>(string path, TPayload payload, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(path, payload, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to POST {Path} to {BaseAddress}.", path, _httpClient.BaseAddress);
        }
    }
}
