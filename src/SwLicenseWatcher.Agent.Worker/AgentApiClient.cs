using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;
using System.Net.Http.Headers;

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

    public Task<bool> PublishSnapshotAsync(InventoryIngestionRequest snapshot, CancellationToken cancellationToken) =>
        PostAsync(_options.SnapshotPath, snapshot, InventoryJsonSerializerContext.Default.InventoryIngestionRequest, cancellationToken);

    public Task<bool> PublishHeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken) =>
        PostAsync(_options.HeartbeatPath, heartbeat, InventoryJsonSerializerContext.Default.AgentHeartbeat, cancellationToken);

    private async Task<bool> PostAsync<TPayload>(string path, TPayload payload, JsonTypeInfo<TPayload> typeInfo, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(payload, typeInfo)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to POST {Path} to {BaseAddress}.", path, _httpClient.BaseAddress);
            return false;
        }
    }
}
