using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal sealed class GitHubReleaseClient(
    HttpClient httpClient,
    IOptions<GitHubUpdateOptions> options)
{
    private readonly GitHubUpdateOptions _options = options.Value;

    public Task<GitHubReleaseDocument> GetLatestAsync(CancellationToken cancellationToken) =>
        GetReleaseAsync($"repos/{RepositoryPath()}/releases/latest", cancellationToken);

    public Task<GitHubReleaseDocument> GetByTagAsync(string tag, CancellationToken cancellationToken) =>
        GetReleaseAsync($"repos/{RepositoryPath()}/releases/tags/{Uri.EscapeDataString(tag)}", cancellationToken);

    public async Task<IReadOnlyList<GitHubReleaseDocument>> ListAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var limit = Math.Clamp(_options.ReleaseListLimit, 1, 100);
        using var response = await httpClient.GetAsync(
            $"repos/{RepositoryPath()}/releases?per_page={limit}",
            cancellationToken);
        await EnsureSuccessAsync(response, "list GitHub releases");
        var releases = await response.Content.ReadFromJsonAsync(
            GitHubJsonSerializerContext.Default.GitHubReleaseDocumentArray,
            cancellationToken);
        return releases ?? [];
    }

    public async Task DownloadAssetAsync(
        GitHubReleaseAssetDocument asset,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var useApiUrl = !string.IsNullOrWhiteSpace(_options.Token) &&
            !string.IsNullOrWhiteSpace(asset.ApiUrl);
        var url = useApiUrl ? asset.ApiUrl! : asset.BrowserDownloadUrl;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new GitHubReleaseImportException(
                HttpStatusCode.BadGateway,
                "The GitHub release asset is missing a download URL.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (useApiUrl)
        {
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        }

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        await EnsureSuccessAsync(response, $"download GitHub asset {asset.Name}");
        if (response.Content.Headers.ContentLength > _options.MaxPackageBytes)
        {
            throw new GitHubReleaseImportException(
                HttpStatusCode.BadGateway,
                "The GitHub release asset exceeds Updates:GitHub:MaxPackageBytes.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > _options.MaxPackageBytes)
            {
                throw new GitHubReleaseImportException(
                    HttpStatusCode.BadGateway,
                    "The GitHub release asset exceeds Updates:GitHub:MaxPackageBytes.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private async Task<GitHubReleaseDocument> GetReleaseAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var response = await httpClient.GetAsync(relativeUrl, cancellationToken);
        await EnsureSuccessAsync(response, "read GitHub release");
        var release = await response.Content.ReadFromJsonAsync(
            GitHubJsonSerializerContext.Default.GitHubReleaseDocument,
            cancellationToken);
        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
        {
            throw new GitHubReleaseImportException(
                HttpStatusCode.BadGateway,
                "GitHub returned an empty release payload.");
        }

        return release;
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new GitHubReleaseImportException(
                HttpStatusCode.BadRequest,
                "Updates:GitHub:Owner and Updates:GitHub:Repository must be set to import GitHub releases.");
        }
    }

    private string RepositoryPath() =>
        $"{Uri.EscapeDataString(_options.Owner.Trim())}/{Uri.EscapeDataString(_options.Repository.Trim())}";

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new GitHubReleaseImportException(
                HttpStatusCode.NotFound,
                "The requested GitHub release was not found.");
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new GitHubReleaseImportException(
                HttpStatusCode.BadGateway,
                "GitHub refused the request. Check Updates:GitHub:Token for a private repository or rate limits.");
        }

        throw new GitHubReleaseImportException(
            HttpStatusCode.BadGateway,
            $"Failed to {action} ({(int)response.StatusCode}).");
    }
}

internal sealed class GitHubReleaseImportException(HttpStatusCode statusCode, string message)
    : InvalidOperationException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
