using System.Text.Json;
using System.Text.Json.Serialization;

namespace SwLicenseWatcher.Api;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(GitHubReleaseDocument))]
[JsonSerializable(typeof(GitHubReleaseDocument[]))]
[JsonSerializable(typeof(GitHubReleaseAssetDocument))]
[JsonSerializable(typeof(IReadOnlyList<GitHubReleaseAssetDocument>))]
[JsonSerializable(typeof(IReadOnlyList<GitHubReleaseDocument>))]
internal sealed partial class GitHubJsonSerializerContext : JsonSerializerContext;
