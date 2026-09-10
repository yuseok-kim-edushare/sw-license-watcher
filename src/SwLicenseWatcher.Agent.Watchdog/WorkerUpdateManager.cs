using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Watchdog;

public sealed class WorkerUpdateManager(
    IPackageDownloader packageDownloader,
    IUpdatePackageVerifier packageVerifier,
    ISafeZipExtractor zipExtractor,
    IWorkerDeploymentManager deploymentManager,
    WorkerUpdateFileSystem fileSystem,
    IOptions<WatchdogOptions> options,
    ILogger<WorkerUpdateManager> logger)
{
    private readonly WatchdogOptions _options = options.Value;
    private bool _placeholderLogged;

    public async Task ApplyAsync(UpdateManifest manifest, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            logger.LogWarning("Updates can only be applied on Windows.");
            return;
        }

        if (!string.Equals(manifest.TargetServiceName, _options.WorkerServiceName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The manifest targets a different service.");
        }

        var currentVersionFile = Path.Combine(_options.WorkerInstallDirectory, ".version");
        if (File.Exists(currentVersionFile) &&
            string.Equals((await File.ReadAllTextAsync(currentVersionFile, cancellationToken)).Trim(), manifest.Version, StringComparison.Ordinal))
        {
            return;
        }

        if (IsPlaceholderManifest(manifest))
        {
            if (_placeholderLogged)
            {
                logger.LogDebug("Update manifest is not ready yet (placeholder or incomplete values); skipping update check.");
            }
            else
            {
                logger.LogInformation("Update manifest is not ready yet (placeholder or incomplete values); skipping update check.");
                _placeholderLogged = true;
            }

            return;
        }

        ValidateManifest(manifest);
        var operationDirectory = Path.Combine(_options.StagingDirectory, Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(operationDirectory, "worker.zip");
        var extractedPath = Path.Combine(operationDirectory, "extracted");
        Directory.CreateDirectory(extractedPath);

        try
        {
            await packageDownloader.DownloadAsync(new Uri(manifest.PackageUrl), archivePath, cancellationToken);
            await packageVerifier.VerifyHashAsync(archivePath, manifest.Sha256, cancellationToken);
            await zipExtractor.ExtractAsync(archivePath, extractedPath, cancellationToken);
            if (manifest.RequireAuthenticode)
            {
                packageVerifier.VerifyAuthenticode(extractedPath);
            }

            await deploymentManager.DeployAndVerifyAsync(
                fileSystem.ResolveWorkerPayload(extractedPath),
                manifest.Version,
                TimeSpan.FromMinutes(manifest.RollbackAfterMinutes),
                cancellationToken);
        }
        finally
        {
            fileSystem.TryDeleteDirectory(operationDirectory);
        }
    }

    internal static bool IsPlaceholderManifest(UpdateManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Version) ||
            string.IsNullOrWhiteSpace(manifest.Sha256) ||
            manifest.Sha256.Length != 64 ||
            !manifest.Sha256.All(Uri.IsHexDigit))
        {
            return true;
        }

        if (ContainsPlaceholderToken(manifest.Version) ||
            ContainsPlaceholderToken(manifest.Sha256) ||
            ContainsPlaceholderToken(manifest.PackageUrl))
        {
            return true;
        }

        return !Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out var packageUri) ||
            packageUri.Scheme != Uri.UriSchemeHttps;
    }

    private static bool ContainsPlaceholderToken(string? value) =>
        !string.IsNullOrEmpty(value) &&
        (value.Contains("REPLACE_ME", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("REPLACE_WITH", StringComparison.OrdinalIgnoreCase));

    internal static void ValidateManifest(UpdateManifest manifest)
    {
        if (!Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out var packageUri) ||
            packageUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Update packages must use an absolute HTTPS URL.");
        }

        if (manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException("The manifest must contain a 64-character SHA-256 digest.");
        }
        if (manifest.RollbackAfterMinutes is < 1 or > 60)
        {
            throw new InvalidOperationException("The rollback timeout must be between 1 and 60 minutes.");
        }
    }
}

internal static class WatchdogOptionsValidator
{
    public static bool HasSafeDirectories(WatchdogOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.StagingDirectory) ||
            string.IsNullOrWhiteSpace(options.BackupDirectory) ||
            string.IsNullOrWhiteSpace(options.WorkerInstallDirectory))
        {
            return false;
        }

        try
        {
            var directories = new[]
            {
                Normalize(options.StagingDirectory),
                Normalize(options.BackupDirectory),
                Normalize(options.WorkerInstallDirectory)
            };
            return directories.All(path => !IsRoot(path)) &&
                directories.Distinct(StringComparer.OrdinalIgnoreCase).Count() == directories.Length &&
                directories.All(first => directories.All(second =>
                    ReferenceEquals(first, second) || !IsSameOrChildPath(first, second)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsRoot(string path) =>
        string.Equals(path, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(path)!), StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrChildPath(string path, string parent) =>
        path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(parent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
