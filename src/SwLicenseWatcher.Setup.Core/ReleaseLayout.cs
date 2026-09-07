using System.IO.Compression;

namespace SwLicenseWatcher.Setup.Core;

public static class ReleaseLayout
{
    public static string? FindDirectory(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return null;
        }

        var direct = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(direct))
        {
            return direct;
        }

        foreach (var child in Directory.EnumerateDirectories(root))
        {
            var nested = Path.Combine(child, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(nested))
            {
                return nested;
            }
        }

        return null;
    }

    public static string? FindFile(string root, string fileName)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return null;
        }

        var direct = Path.Combine(root, fileName);
        if (File.Exists(direct))
        {
            return direct;
        }

        foreach (var child in Directory.EnumerateDirectories(root))
        {
            var nested = Path.Combine(child, fileName);
            if (File.Exists(nested))
            {
                return nested;
            }
        }

        return null;
    }

    public static bool TryFindAgents(string root, out string workerDirectory, out string watchdogDirectory)
    {
        workerDirectory = FindDirectory(root, PayloadLayout.WorkerRelative) ?? string.Empty;
        watchdogDirectory = FindDirectory(root, PayloadLayout.WatchdogRelative) ?? string.Empty;
        return workerDirectory.Length > 0
            && watchdogDirectory.Length > 0
            && File.Exists(Path.Combine(workerDirectory, PayloadLayout.WorkerExe))
            && File.Exists(Path.Combine(watchdogDirectory, PayloadLayout.WatchdogExe));
    }

    public static string ReadVersion(string workerDirectory)
    {
        var versionFile = Path.Combine(workerDirectory, PayloadLayout.VersionFileName);
        if (File.Exists(versionFile))
        {
            var version = File.ReadAllText(versionFile).Trim();
            if (version.Length > 0)
            {
                return version;
            }
        }

        return "0.0.0";
    }

    public static string ExtractAgentsFromReleaseZip(string zipPath, string destinationDirectory)
    {
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("Release zip was not found.", zipPath);
        }

        Directory.CreateDirectory(destinationDirectory);
        using var stream = File.OpenRead(zipPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var extractedAny = false;
        foreach (var entry in archive.Entries)
        {
            var normalized = entry.FullName.Replace('\\', '/');
            var workerIndex = normalized.IndexOf(PayloadLayout.WorkerRelative, StringComparison.OrdinalIgnoreCase);
            var watchdogIndex = normalized.IndexOf(PayloadLayout.WatchdogRelative, StringComparison.OrdinalIgnoreCase);
            string? prefix = null;
            int index = -1;
            if (workerIndex >= 0)
            {
                prefix = PayloadLayout.WorkerRelative;
                index = workerIndex;
            }
            else if (watchdogIndex >= 0)
            {
                prefix = PayloadLayout.WatchdogRelative;
                index = watchdogIndex;
            }

            if (prefix is null || index < 0)
            {
                continue;
            }

            var tail = normalized[(index + prefix.Length)..].TrimStart('/');
            var relative = prefix + (tail.Length == 0 ? string.Empty : "/" + tail);
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(Path.Combine(destinationDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));
                continue;
            }

            var target = Path.GetFullPath(Path.Combine(destinationDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));
            var destRoot = Path.GetFullPath(destinationDirectory)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"The zip entry '{entry.FullName}' escapes the extract directory.");
            }

            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            entry.ExtractToFile(target, overwrite: true);
            extractedAny = true;
        }

        if (!extractedAny || !TryFindAgents(destinationDirectory, out _, out _))
        {
            throw new InvalidDataException("The zip does not contain agent-worker/win-x64 and agent-watchdog/win-x64.");
        }

        return destinationDirectory;
    }
}
