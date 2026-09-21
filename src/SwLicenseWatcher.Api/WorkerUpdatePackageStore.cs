using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal sealed class WorkerUpdatePackageStore
{
    private readonly GitHubUpdateOptions _options;
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WorkerUpdatePackageStore(IOptions<GitHubUpdateOptions> options, IHostEnvironment environment)
    {
        _options = options.Value;
        _root = Path.IsPathRooted(_options.PackageDirectory)
            ? _options.PackageDirectory
            : Path.Combine(environment.ContentRootPath, _options.PackageDirectory);
    }

    public bool TryGetPackagePath(string version, out string path)
    {
        path = string.Empty;
        if (!TryNormalizeVersion(version, out var normalized))
        {
            return false;
        }

        var candidate = Path.Combine(_root, WorkerUpdatePackageNames.WorkerZipFileName(normalized));
        if (!File.Exists(candidate))
        {
            return false;
        }

        path = candidate;
        return true;
    }

    public async Task SavePackageAsync(string version, Stream source, CancellationToken cancellationToken)
    {
        if (!TryNormalizeVersion(version, out var normalized))
        {
            throw new GitHubReleaseImportException(
                System.Net.HttpStatusCode.BadRequest,
                "The Worker package version is not safe to store on disk.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_root);
            var destination = Path.Combine(_root, WorkerUpdatePackageNames.WorkerZipFileName(normalized));
            var temp = destination + ".tmp";
            try
            {
                await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await source.CopyToAsync(file, cancellationToken);
                }

                File.Move(temp, destination, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static bool TryNormalizeVersion(string? version, out string normalized)
    {
        normalized = WorkerUpdatePackageNames.NormalizeVersion(version ?? string.Empty);
        return WorkerUpdatePackageNames.IsSafeVersion(normalized);
    }
}
