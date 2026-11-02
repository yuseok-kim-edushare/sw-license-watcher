using System.IO.Compression;

namespace SwLicenseWatcher.Api;

internal static class ServerUpdateZip
{
    public static void Extract(Stream zip, string destinationDirectory, long maxUncompressedBytes)
    {
        ArgumentNullException.ThrowIfNull(zip);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        if (maxUncompressedBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxUncompressedBytes));
        }

        Directory.CreateDirectory(destinationDirectory);
        var destinationFull = Path.GetFullPath(destinationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > 10_000)
        {
            throw new InvalidDataException("The server package contains too many entries.");
        }

        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var directoryOnly = string.IsNullOrEmpty(entry.Name) &&
                (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'));
            var target = GetSafeEntryPath(destinationFull, entry.FullName);
            if (directoryOnly)
            {
                Directory.CreateDirectory(target);
                continue;
            }

            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            using var input = entry.Open();
            using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > maxUncompressedBytes)
                {
                    throw new InvalidDataException("The server package expands beyond Updates:GitHub:MaxPackageBytes.");
                }

                output.Write(buffer, 0, read);
            }
        }
    }

    private static string GetSafeEntryPath(string destinationFull, string entryName)
    {
        var relative = entryName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var combined = Path.GetFullPath(Path.Combine(destinationFull, relative));
        if (!combined.StartsWith(destinationFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The zip entry '{entryName}' escapes the extract directory.");
        }

        return combined;
    }

    public static void CreateFromDirectory(string sourceDirectory, string destinationFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFile);
        var root = Path.GetFullPath(sourceDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"The package directory '{sourceDirectory}' does not exist.");
        }

        var parent = Path.GetDirectoryName(destinationFile);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (File.Exists(destinationFile))
        {
            File.Delete(destinationFile);
        }

        using var archive = ZipFile.Open(destinationFile, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(file);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"The file '{file}' escapes the package directory.");
            }

            var relative = full[root.Length..].Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
            using var input = File.OpenRead(full);
            using var output = entry.Open();
            input.CopyTo(output);
        }
    }
}
