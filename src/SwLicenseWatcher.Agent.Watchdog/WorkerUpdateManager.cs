using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Runtime.Versioning;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Watchdog;

public sealed class WorkerUpdateManager(
    HttpClient httpClient,
    IOptions<WatchdogOptions> options,
    ILogger<WorkerUpdateManager> logger)
{
    private readonly WatchdogOptions _options = options.Value;

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

        ValidateManifest(manifest);
        var operationDirectory = Path.Combine(_options.StagingDirectory, Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(operationDirectory, "worker.zip");
        var extractedPath = Path.Combine(operationDirectory, "extracted");
        Directory.CreateDirectory(extractedPath);

        try
        {
            await DownloadAsync(new Uri(manifest.PackageUrl), archivePath, cancellationToken);
            await VerifyHashAsync(archivePath, manifest.Sha256, cancellationToken);
            await ExtractSafelyAsync(archivePath, extractedPath, cancellationToken);
            if (manifest.RequireAuthenticode)
            {
                VerifyAuthenticode(extractedPath);
            }

            await ReplaceAndVerifyAsync(
                ResolveWorkerPayload(extractedPath),
                manifest.Version,
                TimeSpan.FromMinutes(manifest.RollbackAfterMinutes),
                cancellationToken);
        }
        finally
        {
            TryDeleteDirectory(operationDirectory);
        }
    }

    private static void ValidateManifest(UpdateManifest manifest)
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

    private async Task DownloadAsync(Uri uri, string path, CancellationToken cancellationToken)
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

    private static async Task VerifyHashAsync(string path, string expected, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual),
                Convert.FromHexString(expected)))
        {
            throw new CryptographicException("The update package SHA-256 digest does not match the manifest.");
        }
    }

    private async Task ExtractSafelyAsync(string archivePath, string destination, CancellationToken cancellationToken)
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

    private static void VerifyAuthenticode(string directory)
    {
        var binaries = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (binaries.Length == 0)
        {
            throw new CryptographicException("The update package contains no signed binaries.");
        }

        foreach (var binary in binaries)
        {
            if (!AuthenticodeVerifier.IsTrusted(binary))
            {
                throw new CryptographicException($"Authenticode verification failed for {Path.GetFileName(binary)}.");
            }
        }
    }

    private static string ResolveWorkerPayload(string extractedPath)
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

    [SupportedOSPlatform("windows")]
    private async Task ReplaceAndVerifyAsync(
        string source,
        string version,
        TimeSpan healthTimeout,
        CancellationToken cancellationToken)
    {
        var backup = Path.Combine(_options.BackupDirectory, "worker-previous");
        using var service = new ServiceController(_options.WorkerServiceName);
        await SetServiceStateAsync(service, start: false, cancellationToken);
        var installReplaced = false;
        try
        {
            TryDeleteDirectory(backup);
            if (Directory.Exists(_options.WorkerInstallDirectory))
            {
                CopyDirectory(_options.WorkerInstallDirectory, backup);
            }
            TryDeleteDirectory(_options.WorkerInstallDirectory);
            installReplaced = true;
            CopyDirectory(source, _options.WorkerInstallDirectory);
            await File.WriteAllTextAsync(Path.Combine(_options.WorkerInstallDirectory, ".version"), version, cancellationToken);
            await SetServiceStateAsync(service, start: true, cancellationToken);
            await WaitForHealthAsync(service, healthTimeout, cancellationToken);
            logger.LogInformation("Worker service updated successfully to {Version}.", version);
        }
        catch
        {
            logger.LogError("Worker update failed; restoring backup.");
            await SetServiceStateAsync(service, start: false, CancellationToken.None);
            if (installReplaced)
            {
                TryDeleteDirectory(_options.WorkerInstallDirectory);
                if (Directory.Exists(backup))
                {
                    CopyDirectory(backup, _options.WorkerInstallDirectory);
                }
            }
            await SetServiceStateAsync(service, start: true, CancellationToken.None);
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    private async Task WaitForHealthAsync(
        ServiceController service,
        TimeSpan healthTimeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + healthTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            service.Refresh();
            if (service.Status == ServiceControllerStatus.Running)
            {
                try
                {
                    using var response = await httpClient.GetAsync(_options.WorkerHealthUrl, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
        throw new System.TimeoutException("Worker health was not restored before the rollback deadline.");
    }

    [SupportedOSPlatform("windows")]
    private static Task SetServiceStateAsync(ServiceController service, bool start, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            service.Refresh();
            if (start && service.Status != ServiceControllerStatus.Running)
            {
                service.Start();
                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromMinutes(2));
            }
            else if (!start && service.Status != ServiceControllerStatus.Stopped)
            {
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromMinutes(2));
            }
        }, cancellationToken);

    private static void CopyDirectory(string source, string destination)
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

    private static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}

internal static class AuthenticodeVerifier
{
    private static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public static bool IsTrusted(string path)
    {
        using var fileInfo = new WinTrustFileInfo(path);
        using var trustData = new WinTrustData(fileInfo);
        return WinVerifyTrust(IntPtr.Zero, ActionGenericVerifyV2, trustData) == 0;
    }

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [In] Guid actionId, [In] WinTrustData trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustFileInfo : IDisposable
    {
        private readonly uint StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
        private IntPtr FilePath;
        private readonly IntPtr FileHandle = IntPtr.Zero;
        private readonly IntPtr KnownSubject = IntPtr.Zero;

        public WinTrustFileInfo(string path) => FilePath = Marshal.StringToCoTaskMemUni(path);
        public void Dispose() => Marshal.FreeCoTaskMem(FilePath);
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class WinTrustData : IDisposable
    {
        private readonly uint StructSize = (uint)Marshal.SizeOf<WinTrustData>();
        private readonly IntPtr PolicyCallbackData = IntPtr.Zero;
        private readonly IntPtr SipClientData = IntPtr.Zero;
        private readonly uint UiChoice = 2;
        private readonly uint RevocationChecks = 1;
        private readonly uint UnionChoice = 1;
        private IntPtr FileInfo;
        private readonly uint StateAction = 0;
        private readonly IntPtr StateData = IntPtr.Zero;
        private readonly IntPtr UrlReference = IntPtr.Zero;
        private readonly uint ProviderFlags = 0x00000080;
        private readonly uint UiContext = 0;

        public WinTrustData(WinTrustFileInfo fileInfo)
        {
            FileInfo = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, FileInfo, false);
        }
        public void Dispose() => Marshal.FreeCoTaskMem(FileInfo);
    }
}
