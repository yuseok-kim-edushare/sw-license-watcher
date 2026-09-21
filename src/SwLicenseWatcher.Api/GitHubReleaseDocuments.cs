using System.Text.Json.Serialization;

namespace SwLicenseWatcher.Api;

internal sealed record GitHubReleaseDocument(
    [property: JsonPropertyName("tag_name")] string? TagName,
    [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
    [property: JsonPropertyName("assets")] IReadOnlyList<GitHubReleaseAssetDocument>? Assets);

internal sealed record GitHubReleaseAssetDocument(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("url")] string? ApiUrl,
    [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl,
    [property: JsonPropertyName("size")] long Size);
