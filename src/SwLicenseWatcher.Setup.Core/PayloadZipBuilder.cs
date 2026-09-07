using System.IO.Compression;
using System.Text;

namespace SwLicenseWatcher.Setup.Core;

public static class PayloadZipBuilder
{
    public static byte[] Build(
        CompanySettings settings,
        string setupUiExePath,
        string workerDirectory,
        string watchdogDirectory)
    {
        ValidateSources(setupUiExePath, workerDirectory, watchdogDirectory);
        if (!CompanySettingsValidator.TryValidate(settings, out var error))
        {
            throw new ArgumentException(error);
        }

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, PayloadLayout.CompanyJson, Encoding.UTF8.GetBytes(CompanySettingsStore.Serialize(settings)));
            CopyFile(archive, setupUiExePath, PayloadLayout.SetupUiZipPath);
            CopyDirectory(archive, workerDirectory, PayloadLayout.WorkerRelative);
            CopyDirectory(archive, watchdogDirectory, PayloadLayout.WatchdogRelative);
        }

        return stream.ToArray();
    }

    public static void ValidateLayout(string payloadDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadDirectory);
        var companyPath = PayloadLayout.GetCompanyJsonPath(payloadDirectory);
        _ = CompanySettingsStore.Load(companyPath);

        var missing = new List<string>();
        RequireFile(PayloadLayout.GetSetupUiPath(payloadDirectory), missing);
        RequireFile(PayloadLayout.GetWorkerExePath(payloadDirectory), missing);
        RequireFile(PayloadLayout.GetWatchdogExePath(payloadDirectory), missing);
        if (missing.Count > 0)
        {
            throw new InvalidDataException("The payload is missing: " + string.Join(", ", missing));
        }
    }

    private static void ValidateSources(string setupUiExePath, string workerDirectory, string watchdogDirectory)
    {
        if (!File.Exists(setupUiExePath))
        {
            throw new FileNotFoundException("Setup UI executable was not found.", setupUiExePath);
        }

        var workerExe = Path.Combine(workerDirectory, PayloadLayout.WorkerExe);
        if (!File.Exists(workerExe))
        {
            throw new FileNotFoundException("Worker executable was not found.", workerExe);
        }

        var watchdogExe = Path.Combine(watchdogDirectory, PayloadLayout.WatchdogExe);
        if (!File.Exists(watchdogExe))
        {
            throw new FileNotFoundException("Watchdog executable was not found.", watchdogExe);
        }
    }

    private static void RequireFile(string path, List<string> missing)
    {
        if (!File.Exists(path))
        {
            missing.Add(path);
        }
    }

    private static void CopyDirectory(ZipArchive archive, string sourceDirectory, string zipPrefix)
    {
        var fullSource = Path.GetFullPath(sourceDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var file in Directory.EnumerateFiles(fullSource, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(fullSource, file).Replace('\\', '/');
            CopyFile(archive, file, zipPrefix + "/" + relative);
        }
    }

    private static void CopyFile(ZipArchive archive, string sourceFile, string zipPath)
    {
        var entry = archive.CreateEntry(zipPath.Replace('\\', '/'), CompressionLevel.SmallestSize);
        using var input = File.OpenRead(sourceFile);
        using var output = entry.Open();
        input.CopyTo(output);
    }

    private static void WriteEntry(ZipArchive archive, string zipPath, byte[] bytes)
    {
        var entry = archive.CreateEntry(zipPath, CompressionLevel.SmallestSize);
        using var output = entry.Open();
        output.Write(bytes);
    }
}
