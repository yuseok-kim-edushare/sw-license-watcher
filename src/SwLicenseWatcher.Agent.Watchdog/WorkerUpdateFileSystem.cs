namespace SwLicenseWatcher.Agent.Watchdog;

public sealed class WorkerUpdateFileSystem
{
    public string ResolveWorkerPayload(string extractedPath)
    {
        var executables = Directory.EnumerateFiles(
                extractedPath,
                "SwLicenseWatcher.Agent.Worker.exe",
                SearchOption.AllDirectories)
            .Take(2)
            .ToArray();
        if (executables.Length != 1)
        {
            throw new InvalidDataException("The package must contain exactly one Worker executable.");
        }

        return Path.GetDirectoryName(executables[0])!;
    }

    public void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), true);
        }
    }

    public bool IsProtectedConfigurationFile(string pathOrFileName)
    {
        var fileName = Path.GetFileName(pathOrFileName);
        return fileName.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase) &&
            fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    public void PreserveConfigurationFiles(string installDirectory, string destinationDirectory) =>
        CopyProtectedConfigurationFiles(installDirectory, destinationDirectory);

    public void RestoreConfigurationFiles(string sourceDirectory, string installDirectory) =>
        CopyProtectedConfigurationFiles(sourceDirectory, installDirectory);

    public void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    private void CopyProtectedConfigurationFiles(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "appsettings*.json", SearchOption.TopDirectoryOnly))
        {
            if (IsProtectedConfigurationFile(file))
            {
                File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), overwrite: true);
            }
        }
    }
}
