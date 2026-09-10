using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Watchdog;

public interface IPackageDownloader
{
    Task DownloadAsync(Uri uri, string path, CancellationToken cancellationToken);
}

public sealed class PackageDownloader(
    HttpClient httpClient,
    IOptions<WatchdogOptions> options) : IPackageDownloader
{
    private readonly WatchdogOptions _options = options.Value;

    public async Task DownloadAsync(Uri uri, string path, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > _options.MaxPackageBytes)
        {
            throw new InvalidDataException("The update package exceeds the configured download limit.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > _options.MaxPackageBytes)
            {
                throw new InvalidDataException("The update package exceeds the configured download limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
