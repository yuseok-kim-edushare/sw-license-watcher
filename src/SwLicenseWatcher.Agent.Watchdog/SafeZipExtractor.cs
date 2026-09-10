using System.IO.Compression;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Watchdog;

public interface ISafeZipExtractor
{
    Task ExtractAsync(string archivePath, string destination, CancellationToken cancellationToken);
}

public sealed class SafeZipExtractor(IOptions<WatchdogOptions> options) : ISafeZipExtractor
{
    private readonly WatchdogOptions _options = options.Value;

    public async Task ExtractAsync(string archivePath, string destination, CancellationToken cancellationToken)
    {
        var destinationRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        long expandedSize = 0;
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The update archive contains an unsafe path.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var source = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                expandedSize = checked(expandedSize + read);
                if (expandedSize > _options.MaxExtractedBytes)
                {
                    throw new InvalidDataException("The update package exceeds the configured extraction limit.");
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
    }
}
