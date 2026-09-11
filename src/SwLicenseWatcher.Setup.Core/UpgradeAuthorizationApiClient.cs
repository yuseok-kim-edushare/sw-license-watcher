using System.Net;
using System.Text;
using System.Text.Json;

namespace SwLicenseWatcher.Setup.Core;

public sealed class UpgradeAuthorizationApiClient(HttpClient httpClient) : IUpgradeAuthorizationClient
{
    public async Task<UpgradeAuthorizationResponse> AuthorizeAsync(
        UpgradeAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(request, SetupJsonContext.Default.UpgradeAuthorizationRequest),
            Encoding.UTF8,
            "application/json");
        using var response = await httpClient.PostAsync(
            "/api/agents/upgrade-authorizations",
            content,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                "서버에 등록된 장치를 찾지 못했습니다. 먼저 수집이 끝난 뒤 업그레이드하세요.");
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(body)
                    ? "서버 장치 키 확인에 실패했습니다."
                    : "서버 장치 키 확인에 실패했습니다. " + body);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            throw new InvalidOperationException(
                $"The API returned HTTP {(int)response.StatusCode}. {body}".Trim());
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var keys = await JsonSerializer.DeserializeAsync(
            stream,
            SetupJsonContext.Default.UpgradeAuthorizationResponse,
            cancellationToken);
        if (keys is null || string.IsNullOrWhiteSpace(keys.DeviceCode))
        {
            throw new InvalidOperationException("The API returned an empty upgrade authorization.");
        }

        return keys;
    }
}
