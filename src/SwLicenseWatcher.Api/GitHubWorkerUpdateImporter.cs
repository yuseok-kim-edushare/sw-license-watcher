using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal sealed class GitHubWorkerUpdateImporter(
    GitHubReleaseClient gitHub,
    WorkerUpdatePackageStore packages,
    WorkerUpdatePinService workerPin,
    IOptions<GitHubUpdateOptions> options,
    ILogger<GitHubWorkerUpdateImporter> logger)
{
    public async Task<GitHubUpdateSourceResponse> GetSourceAsync(CancellationToken cancellationToken)
    {
        var configured = options.Value;
        if (!configured.IsConfigured)
        {
            return new GitHubUpdateSourceResponse(false, configured.Owner, configured.Repository, []);
        }

        try
        {
            var releases = await gitHub.ListAsync(cancellationToken);
            var summaries = new List<GitHubWorkerReleaseSummary>(releases.Count);
            foreach (var release in releases)
            {
                if (!WorkerUpdatePackageStore.TryNormalizeVersion(release.TagName, out var version))
                {
                    continue;
                }

                var zipName = WorkerUpdatePackageNames.WorkerZipFileName(version);
                summaries.Add(new GitHubWorkerReleaseSummary(
                    version,
                    release.PublishedAt,
                    HasNamedAsset(release, zipName),
                    HasNamedAsset(release, WorkerUpdatePackageNames.ChecksumAssetName),
                    packages.TryGetPackagePath(version, out _)));
            }

            return new GitHubUpdateSourceResponse(true, configured.Owner.Trim(), configured.Repository.Trim(), summaries);
        }
        catch (GitHubReleaseImportException ex)
        {
            logger.LogWarning(ex, "Could not list GitHub releases for Worker updates.");
            return new GitHubUpdateSourceResponse(
                true,
                configured.Owner.Trim(),
                configured.Repository.Trim(),
                [],
                ex.Message);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Could not list GitHub releases for Worker updates.");
            return new GitHubUpdateSourceResponse(
                true,
                configured.Owner.Trim(),
                configured.Repository.Trim(),
                [],
                "The API could not reach GitHub to list releases.");
        }
    }

    public async Task<GitHubWorkerUpdateImportResponse> ImportAsync(
        GitHubWorkerUpdateImportRequest request,
        CancellationToken cancellationToken)
    {
        var release = await ResolveReleaseAsync(request.Version, cancellationToken);
        if (!WorkerUpdatePackageStore.TryNormalizeVersion(release.TagName, out var version))
        {
            throw new GitHubReleaseImportException(
                System.Net.HttpStatusCode.BadGateway,
                "The GitHub release tag is not a safe Worker package version.");
        }

        var zipName = WorkerUpdatePackageNames.WorkerZipFileName(version);
        var zip = FindAsset(release, zipName) ??
            throw new GitHubReleaseImportException(
                System.Net.HttpStatusCode.NotFound,
                $"GitHub release {version} does not contain {zipName}.");
        var checksums = FindAsset(release, WorkerUpdatePackageNames.ChecksumAssetName) ??
            throw new GitHubReleaseImportException(
                System.Net.HttpStatusCode.NotFound,
                $"GitHub release {version} does not contain {WorkerUpdatePackageNames.ChecksumAssetName}.");

        var sha256 = await ReadExpectedSha256Async(checksums, zipName, cancellationToken);
        var tempPackage = Path.GetTempFileName();
        try
        {
            await using (var file = new FileStream(tempPackage, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await gitHub.DownloadAssetAsync(zip, file, cancellationToken);
            }

            await using (var file = new FileStream(tempPackage, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken)).ToLowerInvariant();
                if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new GitHubReleaseImportException(
                        System.Net.HttpStatusCode.BadGateway,
                        "The downloaded Worker package SHA-256 does not match SHA256SUMS.txt.");
                }
            }

            await using (var file = new FileStream(tempPackage, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await packages.SavePackageAsync(version, file, cancellationToken);
            }
        }
        finally
        {
            File.Delete(tempPackage);
        }

        var sourceUrl = zip.BrowserDownloadUrl ?? zip.ApiUrl ?? string.Empty;
        var pin = await workerPin.GetEffectiveAsync(cancellationToken);
        var updated = pin with
        {
            Version = version,
            PackageUrl = sourceUrl,
            Sha256 = sha256
        };
        var pinned = request.UpdatePin;
        if (pinned)
        {
            if (!UpdateManifestValidator.TryValidate(updated, out var validationError))
            {
                throw new GitHubReleaseImportException(System.Net.HttpStatusCode.BadRequest, validationError);
            }

            await workerPin.SaveAsync(updated, cancellationToken);
        }

        logger.LogInformation("Imported Worker update package {Version} from GitHub.", version);
        return new GitHubWorkerUpdateImportResponse(
            version,
            sourceUrl,
            sha256,
            sourceUrl,
            pinned,
            ServedByApi: true);
    }

    private async Task<GitHubReleaseDocument> ResolveReleaseAsync(string? version, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return await gitHub.GetLatestAsync(cancellationToken);
        }

        var requested = version.Trim();
        try
        {
            return await gitHub.GetByTagAsync(requested, cancellationToken);
        }
        catch (GitHubReleaseImportException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            var normalized = WorkerUpdatePackageNames.NormalizeVersion(requested);
            if (!string.Equals(normalized, requested, StringComparison.Ordinal))
            {
                return await gitHub.GetByTagAsync(normalized, cancellationToken);
            }

            throw;
        }
    }

    private async Task<string> ReadExpectedSha256Async(
        GitHubReleaseAssetDocument checksums,
        string zipName,
        CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        await gitHub.DownloadAssetAsync(checksums, buffer, cancellationToken);
        var text = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        if (!ReleaseChecksumParser.TryGetSha256(text, zipName, out var sha256))
        {
            throw new GitHubReleaseImportException(
                System.Net.HttpStatusCode.BadGateway,
                $"{WorkerUpdatePackageNames.ChecksumAssetName} does not contain a SHA-256 for {zipName}.");
        }

        return sha256;
    }

    private static bool HasNamedAsset(GitHubReleaseDocument release, string name) =>
        FindAsset(release, name) is not null;

    private static GitHubReleaseAssetDocument? FindAsset(GitHubReleaseDocument release, string name)
    {
        if (release.Assets is null)
        {
            return null;
        }

        foreach (var asset in release.Assets)
        {
            if (string.Equals(asset.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return asset;
            }
        }

        return null;
    }
}
