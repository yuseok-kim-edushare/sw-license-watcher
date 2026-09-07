using System.IO.Compression;

namespace SwLicenseWatcher.Setup.Core;

public static class ZipExtractor
{
    public static void ExtractToDirectory(byte[] zip, string destinationDirectory)
    {
        ArgumentNullException.ThrowIfNull(zip);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        using var stream = new MemoryStream(zip, writable: false);
        ExtractToDirectory(stream, destinationDirectory);
    }

    public static void ExtractToDirectory(Stream zip, string destinationDirectory)
    {
        ArgumentNullException.ThrowIfNull(zip);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        Directory.CreateDirectory(destinationDirectory);
        var destinationFull = Path.GetFullPath(destinationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith('/'))
            {
                var directory = GetSafeEntryPath(destinationFull, entry.FullName);
                Directory.CreateDirectory(directory);
                continue;
            }

            var target = GetSafeEntryPath(destinationFull, entry.FullName);
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static string GetSafeEntryPath(string destinationFull, string entryName)
    {
        var relative = entryName.Replace('/', Path.DirectorySeparatorChar);
        var combined = Path.GetFullPath(Path.Combine(destinationFull, relative));
        if (!combined.StartsWith(destinationFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The zip entry '{entryName}' escapes the extract directory.");
        }

        return combined;
    }
}
