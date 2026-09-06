using System.Net;
using System.Net.Http.Headers;

namespace SwLicenseWatcher.Admin.Services;

public sealed class AdminAuthHandler(TokenStore tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(tokens.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.Token);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await tokens.ClearAsync();
        }

        return response;
    }
}
