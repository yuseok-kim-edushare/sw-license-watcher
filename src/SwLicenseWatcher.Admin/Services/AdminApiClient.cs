using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using SwLicenseWatcher.Admin.Models;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Admin.Services;

public sealed class AdminApiClient(HttpClient http, IJSRuntime js)
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<HealthResponse?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync("/health", cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.ServiceUnavailable)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<HealthResponse>(JsonOptions, cancellationToken);
    }

    public Task<DeviceListResponse> GetDevicesAsync(string query, CancellationToken cancellationToken = default) =>
        GetAsync<DeviceListResponse>($"/api/inventory/devices?{query}", cancellationToken);

    public Task<DeviceDetail> GetDeviceAsync(string deviceCode, string? classification, CancellationToken cancellationToken = default)
    {
        var query = string.IsNullOrEmpty(classification) ? "" : "?classification=" + Uri.EscapeDataString(classification);
        return GetAsync<DeviceDetail>($"/api/inventory/devices/{Uri.EscapeDataString(deviceCode)}{query}", cancellationToken);
    }

    public Task<SoftwareAggregateListResponse> GetSoftwareAsync(string query, CancellationToken cancellationToken = default) =>
        GetAsync<SoftwareAggregateListResponse>($"/api/inventory/software?{query}", cancellationToken);

    public Task<SoftwareDeviceListResponse> GetSoftwareDevicesAsync(string name, string query, CancellationToken cancellationToken = default) =>
        GetAsync<SoftwareDeviceListResponse>(
            $"/api/inventory/software/{Uri.EscapeDataString(name)}/devices?{query}",
            cancellationToken);

    public Task<SoftwarePolicyEntry> PutClassificationAsync(
        string name,
        SoftwareClassificationWriteRequest request,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync<SoftwarePolicyEntry>(
            HttpMethod.Put,
            $"/api/inventory/software/{Uri.EscapeDataString(name)}/classification",
            request,
            cancellationToken);

    public Task<SoftwareClassificationBatchResponse> PutClassificationsAsync(
        SoftwareClassificationBatchWriteRequest request,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync<SoftwareClassificationBatchResponse>(
            HttpMethod.Put,
            "/api/inventory/software/classifications",
            request,
            cancellationToken);

    public Task PutLicenseSourceAsync(
        string deviceCode,
        string softwareName,
        DeviceSoftwareLicenseSourceWriteRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync(
            HttpMethod.Put,
            $"/api/inventory/devices/{Uri.EscapeDataString(deviceCode)}/software/{Uri.EscapeDataString(softwareName)}/license-source",
            request,
            cancellationToken);

    public Task<ViolationListResponse> GetViolationsAsync(string query, CancellationToken cancellationToken = default) =>
        GetAsync<ViolationListResponse>($"/api/violations?{query}", cancellationToken);

    public Task<PolicyListResponse> GetPoliciesAsync(string query, CancellationToken cancellationToken = default) =>
        GetAsync<PolicyListResponse>($"/api/policies?{query}", cancellationToken);

    public Task<SoftwarePolicyEntry> CreatePolicyAsync(SoftwarePolicyWriteRequest request, CancellationToken cancellationToken = default) =>
        SendJsonAsync<SoftwarePolicyEntry>(HttpMethod.Post, "/api/policies", request, cancellationToken);

    public Task<SoftwarePolicyEntry> UpdatePolicyAsync(long id, SoftwarePolicyWriteRequest request, CancellationToken cancellationToken = default) =>
        SendJsonAsync<SoftwarePolicyEntry>(HttpMethod.Put, $"/api/policies/{id}", request, cancellationToken);

    public Task DeletePolicyAsync(long id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"/api/policies/{id}", body: null, cancellationToken);

    public Task<UninstallRequestListResponse> GetUninstallRequestsAsync(string query, CancellationToken cancellationToken = default) =>
        GetAsync<UninstallRequestListResponse>($"/api/uninstall-requests?{query}", cancellationToken);

    public Task ApproveUninstallAsync(long id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"/api/uninstall-requests/{id}/approve", body: null, cancellationToken);

    public Task DenyUninstallAsync(long id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"/api/uninstall-requests/{id}/deny", body: null, cancellationToken);

    public Task<UpdateManifest> GetWorkerUpdatePinAsync(CancellationToken cancellationToken = default) =>
        GetAsync<UpdateManifest>("/api/updates/worker/manifest", cancellationToken);

    public Task<UpdateManifest> PutWorkerUpdatePinAsync(UpdateManifest request, CancellationToken cancellationToken = default) =>
        SendJsonAsync<UpdateManifest>(HttpMethod.Put, "/api/updates/worker/manifest", request, cancellationToken);

    public async Task DownloadCsvAsync(string path, string fileName, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "text/csv";
        await js.InvokeVoidAsync("swlwDownload", cancellationToken, fileName, contentType, bytes);
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    private async Task<T> SendJsonAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(method, path, body, cancellationToken);
        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    private async Task SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(method, path, body, cancellationToken);
        await EnsureSuccessAsync(response);
    }

    private Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        return http.SendAsync(request, cancellationToken);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response);
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return value ?? throw new AdminApiException("응답 본문이 비어 있습니다.", (int)response.StatusCode);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NoContent)
        {
            return;
        }

        throw new AdminApiException(await ReadErrorAsync(response), (int)response.StatusCode);
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable)
        {
            return "데이터베이스에 연결할 수 없습니다";
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return "인증에 실패했습니다. 관리자 토큰을 다시 입력하세요.";
        }

        var text = (await response.Content.ReadAsStringAsync()).Trim();
        if (string.IsNullOrEmpty(text))
        {
            return $"요청에 실패했습니다 ({(int)response.StatusCode})";
        }

        try
        {
            using var parsed = JsonDocument.Parse(text);
            if (parsed.RootElement.ValueKind == JsonValueKind.String)
            {
                return parsed.RootElement.GetString() ?? text;
            }

            foreach (var name in new[] { "error", "Error", "detail", "Detail", "title", "Title", "reason", "Reason" })
            {
                if (parsed.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString() ?? text;
                }
            }
        }
        catch (JsonException)
        {
            /* plain text body */
        }

        return text;
    }
}
