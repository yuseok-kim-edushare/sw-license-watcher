using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal interface IServerUpdateEnvironment
{
    ServerUpdateHostKind? HostKind { get; }

    string ContentRootPath { get; }

    string CurrentVersion { get; }

    string? AppPoolName { get; }
}

internal interface IServerUpdateTrigger
{
    Task TriggerAsync(ServerUpdateHelperConfig config, CancellationToken cancellationToken);
}

internal sealed class ServerUpdateException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

internal sealed class RuntimeServerUpdateEnvironment(IHostEnvironment environment) : IServerUpdateEnvironment
{
    public ServerUpdateHostKind? HostKind =>
        ServerUpdateHosts.TryDetect(
            OperatingSystem.IsWindows(),
            OperatingSystem.IsLinux(),
            Environment.GetEnvironmentVariable("APP_POOL_ID"),
            Environment.GetEnvironmentVariable("ASPNETCORE_IIS_PHYSICAL_PATH"),
            out var kind)
            ? kind
            : null;

    public string ContentRootPath => environment.ContentRootPath;

    public string? AppPoolName => Environment.GetEnvironmentVariable("APP_POOL_ID");

    public string CurrentVersion
    {
        get
        {
            var path = Path.Combine(environment.ContentRootPath, ".version");
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path).Trim();
                if (text.Length is > 0 and <= 64 && !text.Contains('\n') && !text.Contains('\r'))
                {
                    return text;
                }
            }

            var informational = Assembly.GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(informational))
            {
                return "0.0.0";
            }

            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return (plus > 0 ? informational[..plus] : informational).Trim();
        }
    }
}

internal sealed class ProcessServerUpdateTrigger : IServerUpdateTrigger
{
    public async Task TriggerAsync(ServerUpdateHelperConfig config, CancellationToken cancellationToken)
    {
        var start = config.HostKind == ServerUpdateHostKind.Linux
            ? new ProcessStartInfo
            {
                FileName = "sudo",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
            : new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        if (config.HostKind == ServerUpdateHostKind.Linux)
        {
            start.ArgumentList.Add("-n");
            start.ArgumentList.Add("systemctl");
            start.ArgumentList.Add("start");
            start.ArgumentList.Add(config.UpdateUnitName);
        }
        else
        {
            start.ArgumentList.Add("/Run");
            start.ArgumentList.Add("/TN");
            start.ArgumentList.Add(config.TaskName);
        }

        using var process = Process.Start(start) ??
            throw new ServerUpdateException(StatusCodes.Status502BadGateway, "The update helper could not be started.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode == 0)
        {
            return;
        }

        var error = (await process.StandardError.ReadToEndAsync(cancellationToken)).Trim();
        if (string.IsNullOrWhiteSpace(error))
        {
            error = (await process.StandardOutput.ReadToEndAsync(cancellationToken)).Trim();
        }

        throw new ServerUpdateException(
            StatusCodes.Status502BadGateway,
            string.IsNullOrWhiteSpace(error)
                ? "The update helper did not start. Run the helper installer on this server, then try again."
                : error);
    }
}

internal sealed class ServerUpdateCoordinator(
    GitHubReleaseClient gitHub,
    IOptions<GitHubUpdateOptions> gitHubOptions,
    IOptions<ServerUpdateOptions> serverOptions,
    IServerUpdateEnvironment environment,
    IServerUpdateTrigger trigger,
    ILogger<ServerUpdateCoordinator> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ServerUpdateStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        var kind = environment.HostKind;
        var packageName = kind is null ? "" : ServerUpdatePackageNames.ZipFileName(kind.Value, "{version}");
        var (helper, helperError) = ReadHelper(kind);
        string? stagedVersion = null;
        string? lastStatus = null;
        string? lastMessage = null;
        if (helper is not null && Directory.Exists(helper.StagingPath))
        {
            stagedVersion = ReadStateValue(StatePath(helper, ServerUpdateLayout.ReadyFileName), ServerUpdateConfigFile.VersionKey);
            lastStatus = ReadStateValue(StatePath(helper, ServerUpdateLayout.ResultFileName), ServerUpdateConfigFile.StatusKey);
            lastMessage = ReadStateValue(StatePath(helper, ServerUpdateLayout.ResultFileName), ServerUpdateConfigFile.MessageKey);
        }

        var configured = gitHubOptions.Value;
        if (kind is null)
        {
            return EmptyStatus(packageName, false, null, "This operating system is not a supported API host.");
        }

        if (!configured.IsConfigured)
        {
            return new ServerUpdateStatusResponse(
                environment.CurrentVersion,
                ServerUpdateHosts.ToName(kind.Value),
                packageName,
                helper is not null,
                helper is null ? ServerUpdateConfigFile.InstallHint(kind.Value, environment.ContentRootPath, environment.AppPoolName) : null,
                false,
                configured.Owner,
                configured.Repository,
                [],
                stagedVersion,
                lastStatus,
                lastMessage,
                helperError);
        }

        try
        {
            var releases = await gitHub.ListAsync(cancellationToken);
            var summaries = new List<ServerUpdateReleaseSummary>(releases.Count);
            foreach (var release in releases)
            {
                if (!WorkerUpdatePackageStore.TryNormalizeVersion(release.TagName, out var version))
                {
                    continue;
                }

                var zipName = ServerUpdatePackageNames.ZipFileName(kind.Value, version);
                summaries.Add(new ServerUpdateReleaseSummary(
                    version,
                    release.PublishedAt,
                    HasNamedAsset(release, zipName),
                    HasNamedAsset(release, WorkerUpdatePackageNames.ChecksumAssetName),
                    string.Equals(stagedVersion, version, StringComparison.Ordinal)));
            }

            return new ServerUpdateStatusResponse(
                environment.CurrentVersion,
                ServerUpdateHosts.ToName(kind.Value),
                packageName,
                helper is not null,
                helper is null ? ServerUpdateConfigFile.InstallHint(kind.Value, environment.ContentRootPath, environment.AppPoolName) : null,
                true,
                configured.Owner.Trim(),
                configured.Repository.Trim(),
                summaries,
                stagedVersion,
                lastStatus,
                lastMessage,
                helperError);
        }
        catch (GitHubReleaseImportException ex)
        {
            logger.LogWarning(ex, "Could not list GitHub releases for the API server update.");
            return new ServerUpdateStatusResponse(
                environment.CurrentVersion,
                ServerUpdateHosts.ToName(kind.Value),
                packageName,
                helper is not null,
                helper is null ? ServerUpdateConfigFile.InstallHint(kind.Value, environment.ContentRootPath, environment.AppPoolName) : null,
                true,
                configured.Owner.Trim(),
                configured.Repository.Trim(),
                [],
                stagedVersion,
                lastStatus,
                lastMessage,
                JoinErrors(helperError, ex.Message));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Could not list GitHub releases for the API server update.");
            return new ServerUpdateStatusResponse(
                environment.CurrentVersion,
                ServerUpdateHosts.ToName(kind.Value),
                packageName,
                helper is not null,
                helper is null ? ServerUpdateConfigFile.InstallHint(kind.Value, environment.ContentRootPath, environment.AppPoolName) : null,
                true,
                configured.Owner.Trim(),
                configured.Repository.Trim(),
                [],
                stagedVersion,
                lastStatus,
                lastMessage,
                JoinErrors(helperError, "The API could not reach GitHub to list releases."));
        }
    }

    public async Task<ServerUpdateImportResponse> ImportAsync(ServerUpdateImportRequest request, CancellationToken cancellationToken)
    {
        var helper = RequireHelper();
        var kind = helper.HostKind;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var release = await ResolveReleaseAsync(request.Version, cancellationToken);
            if (!WorkerUpdatePackageStore.TryNormalizeVersion(release.TagName, out var version))
            {
                throw new ServerUpdateException(
                    StatusCodes.Status502BadGateway,
                    "The GitHub release tag is not a safe server package version.");
            }

            var zipName = ServerUpdatePackageNames.ZipFileName(kind, version);
            var zip = FindAsset(release, zipName) ??
                throw new ServerUpdateException(
                    StatusCodes.Status404NotFound,
                    $"GitHub release {version} does not contain {zipName}.");
            var checksums = FindAsset(release, WorkerUpdatePackageNames.ChecksumAssetName) ??
                throw new ServerUpdateException(
                    StatusCodes.Status404NotFound,
                    $"GitHub release {version} does not contain {WorkerUpdatePackageNames.ChecksumAssetName}.");
            var sha256 = await ReadExpectedSha256Async(checksums, zipName, cancellationToken);
            Directory.CreateDirectory(helper.StagingPath);
            var packagePath = Path.Combine(helper.StagingPath, zipName);
            var tempPackage = packagePath + ".tmp";
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
                        throw new ServerUpdateException(
                            StatusCodes.Status502BadGateway,
                            "The downloaded server package SHA-256 does not match SHA256SUMS.txt.");
                    }
                }

                File.Move(tempPackage, packagePath, overwrite: true);
                var payload = Path.Combine(helper.StagingPath, ServerUpdateLayout.PayloadDirectoryName);
                if (Directory.Exists(payload))
                {
                    Directory.Delete(payload, recursive: true);
                }

                await using (var file = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    ServerUpdateZip.Extract(file, payload, gitHubOptions.Value.MaxPackageBytes);
                }

                await File.WriteAllTextAsync(
                    StatePath(helper, ServerUpdateLayout.ReadyFileName),
                    ServerUpdateConfigFile.FormatState(new Dictionary<string, string>
                    {
                        [ServerUpdateConfigFile.VersionKey] = version,
                        [ServerUpdateConfigFile.Sha256Key] = sha256
                    }),
                    cancellationToken);
            }
            finally
            {
                if (File.Exists(tempPackage))
                {
                    File.Delete(tempPackage);
                }
            }

            logger.LogInformation("Staged API server package {Version} for {HostKind}.", version, ServerUpdateHosts.ToName(kind));
            return new ServerUpdateImportResponse(version, sha256, zipName);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ServerUpdateApplyResponse> ApplyAsync(ServerUpdateApplyRequest request, CancellationToken cancellationToken)
    {
        var helper = RequireHelper();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var readyPath = StatePath(helper, ServerUpdateLayout.ReadyFileName);
            if (!File.Exists(readyPath) ||
                !ServerUpdateConfigFile.TryParseState(await File.ReadAllTextAsync(readyPath, cancellationToken), out var ready, out _) ||
                !ready.TryGetValue(ServerUpdateConfigFile.VersionKey, out var staged) ||
                !WorkerUpdatePackageNames.IsSafeVersion(staged))
            {
                throw new ServerUpdateException(
                    StatusCodes.Status409Conflict,
                    "Stage a server package before applying it.");
            }

            var requested = string.IsNullOrWhiteSpace(request.Version)
                ? staged
                : WorkerUpdatePackageNames.NormalizeVersion(request.Version);
            if (!string.Equals(requested, staged, StringComparison.Ordinal))
            {
                throw new ServerUpdateException(
                    StatusCodes.Status409Conflict,
                    $"Staged server package is {staged}. Import that version before applying {requested}.");
            }

            var payload = Path.Combine(helper.StagingPath, ServerUpdateLayout.PayloadDirectoryName);
            if (!Directory.Exists(payload))
            {
                throw new ServerUpdateException(StatusCodes.Status409Conflict, "The staged server package is missing.");
            }

            var resultPath = StatePath(helper, ServerUpdateLayout.ResultFileName);
            if (File.Exists(resultPath))
            {
                File.Delete(resultPath);
            }

            await File.WriteAllTextAsync(
                StatePath(helper, ServerUpdateLayout.RequestFileName),
                ServerUpdateConfigFile.FormatState(new Dictionary<string, string>
                {
                    [ServerUpdateConfigFile.VersionKey] = staged
                }),
                cancellationToken);
            await trigger.TriggerAsync(helper, cancellationToken);
            logger.LogInformation("Started the API update helper for version {Version}.", staged);
            return new ServerUpdateApplyResponse(
                staged,
                "The update helper is applying the staged package and will restart this server.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private ServerUpdateHelperConfig RequireHelper()
    {
        var kind = environment.HostKind ??
            throw new ServerUpdateException(StatusCodes.Status400BadRequest, "This operating system is not a supported API host.");
        var (helper, error) = ReadHelper(kind);
        if (helper is null)
        {
            throw new ServerUpdateException(
                StatusCodes.Status409Conflict,
                error ??
                "Install the update helper on this server before staging a package. " +
                ServerUpdateConfigFile.InstallHint(kind, environment.ContentRootPath, environment.AppPoolName));
        }

        return helper;
    }

    private (ServerUpdateHelperConfig? Helper, string? Error) ReadHelper(ServerUpdateHostKind? kind)
    {
        if (kind is null)
        {
            return (null, null);
        }

        var path = string.IsNullOrWhiteSpace(serverOptions.Value.HelperConfigPath)
            ? ServerUpdateLayout.DefaultConfigPath(kind.Value)
            : serverOptions.Value.HelperConfigPath;
        if (!File.Exists(path))
        {
            return (null, null);
        }

        var text = File.ReadAllText(path);
        if (!ServerUpdateConfigFile.TryParse(text, out var config, out var error) || config is null)
        {
            return (null, error);
        }

        if (config.HostKind != kind.Value)
        {
            return (null, "The installed update helper is configured for a different host.");
        }

        if (!ServerUpdateConfigFile.PathsEqual(config.InstallPath, environment.ContentRootPath))
        {
            return (null, "The update helper install path does not match this API directory.");
        }

        return (config, null);
    }

    private ServerUpdateStatusResponse EmptyStatus(string packageName, bool configured, string? helperError, string? error) =>
        new(
            environment.CurrentVersion,
            environment.HostKind is null ? "unsupported" : ServerUpdateHosts.ToName(environment.HostKind.Value),
            packageName,
            false,
            environment.HostKind is null
                ? null
                : ServerUpdateConfigFile.InstallHint(environment.HostKind.Value, environment.ContentRootPath, environment.AppPoolName),
            configured,
            gitHubOptions.Value.Owner,
            gitHubOptions.Value.Repository,
            [],
            null,
            null,
            null,
            JoinErrors(helperError, error));

    private static string StatePath(ServerUpdateHelperConfig helper, string fileName) =>
        Path.Combine(helper.StagingPath, fileName);

    private static string? ReadStateValue(string path, string key)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        if (!ServerUpdateConfigFile.TryParseState(File.ReadAllText(path), out var pairs, out _))
        {
            return null;
        }

        return pairs.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
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
        catch (GitHubReleaseImportException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
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
            throw new ServerUpdateException(
                StatusCodes.Status502BadGateway,
                $"{WorkerUpdatePackageNames.ChecksumAssetName} does not contain a SHA-256 for {zipName}.");
        }

        return sha256;
    }

    private static bool HasNamedAsset(GitHubReleaseDocument release, string name) => FindAsset(release, name) is not null;

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

    private static string? JoinErrors(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return string.IsNullOrWhiteSpace(right) ? null : right;
        }

        if (string.IsNullOrWhiteSpace(right))
        {
            return left;
        }

        return left + " " + right;
    }
}
