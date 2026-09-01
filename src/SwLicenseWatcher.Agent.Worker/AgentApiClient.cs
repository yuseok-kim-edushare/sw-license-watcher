using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Worker;

public enum AgentPublishResult
{
    Succeeded,
    RetryableFailure,
    NonRetryableFailure
}

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

    public Task<AgentPublishResult> PublishSnapshotAsync(InventoryIngestionRequest snapshot, CancellationToken cancellationToken) =>
        PostAsync(_options.SnapshotPath, snapshot, InventoryJsonSerializerContext.Default.InventoryIngestionRequest, cancellationToken);

    public Task<AgentPublishResult> PublishHeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken) =>
        PostAsync(_options.HeartbeatPath, heartbeat, InventoryJsonSerializerContext.Default.AgentHeartbeat, cancellationToken);

    private async Task<AgentPublishResult> PostAsync<TPayload>(string path, TPayload payload, JsonTypeInfo<TPayload> typeInfo, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(payload, typeInfo)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return AgentPublishResult.Succeeded;
            }

            var statusCode = (int)response.StatusCode;
            if (response.StatusCode == HttpStatusCode.TooManyRequests || statusCode >= 500)
            {
                _logger.LogWarning(
                    "POST {Path} to {BaseAddress} returned {StatusCode}; the payload will be queued for retry.",
                    path,
                    _httpClient.BaseAddress,
                    statusCode);
                return AgentPublishResult.RetryableFailure;
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logger.LogWarning(
                    "POST {Path} to {BaseAddress} returned {StatusCode}. Check Agent:ApiToken and API Security:Token; the payload will not be queued for retry.",
                    path,
                    _httpClient.BaseAddress,
                    statusCode);
                return AgentPublishResult.NonRetryableFailure;
            }

            _logger.LogError(
                "POST {Path} to {BaseAddress} returned {StatusCode}. The payload was rejected and will not be queued for retry.",
                path,
                _httpClient.BaseAddress,
                statusCode);
            return AgentPublishResult.NonRetryableFailure;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Failed to POST {Path} to {BaseAddress}.", path, _httpClient.BaseAddress);
            return AgentPublishResult.RetryableFailure;
        }
    }
}
