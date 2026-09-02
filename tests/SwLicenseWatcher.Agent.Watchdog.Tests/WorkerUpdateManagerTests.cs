using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Agent.Watchdog;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Watchdog.Tests;

public class WorkerUpdateManagerTests : IDisposable
{
    private const string ValidSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string WorkerExeName = "SwLicenseWatcher.Agent.Worker.exe";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "slw-watchdog-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ValidateManifest_accepts_an_https_url_64_character_sha_and_in_range_rollback()
    {
        WorkerUpdateManager.ValidateManifest(Manifest());
    }

    [Fact]
    public void ValidateManifest_accepts_lowercase_and_uppercase_sha256_hex()
    {
        WorkerUpdateManager.ValidateManifest(Manifest(sha256: ValidSha256.ToUpperInvariant()));
        WorkerUpdateManager.ValidateManifest(Manifest(sha256: ValidSha256.ToLowerInvariant()));
    }

    [Theory]
    [InlineData("http://example.local/worker.zip")]
    [InlineData("ftp://example.local/worker.zip")]
    [InlineData("/relative/worker.zip")]
    [InlineData("worker.zip")]
    public void ValidateManifest_rejects_package_urls_that_are_not_absolute_https(string packageUrl)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => WorkerUpdateManager.ValidateManifest(Manifest(packageUrl: packageUrl)));
        Assert.Equal("Update packages must use an absolute HTTPS URL.", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdeg")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcd g")]
    public void ValidateManifest_rejects_sha256_values_that_are_not_64_hex_characters(string sha256)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => WorkerUpdateManager.ValidateManifest(Manifest(sha256: sha256)));
        Assert.Equal("The manifest must contain a 64-character SHA-256 digest.", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    [InlineData(-1)]
    [InlineData(120)]
    public void ValidateManifest_rejects_rollback_timeouts_outside_1_to_60_minutes(int rollbackAfterMinutes)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => WorkerUpdateManager.ValidateManifest(Manifest(rollbackAfterMinutes: rollbackAfterMinutes)));
        Assert.Equal("The rollback timeout must be between 1 and 60 minutes.", ex.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    public void ValidateManifest_accepts_rollback_timeouts_on_the_allowed_boundaries(int rollbackAfterMinutes)
    {
        WorkerUpdateManager.ValidateManifest(Manifest(rollbackAfterMinutes: rollbackAfterMinutes));
    }

    [Fact]
    public async Task ApplyAsync_rejects_a_manifest_that_targets_a_different_service()
    {
        var manager = CreateManager();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.ApplyAsync(Manifest(targetServiceName: "Other.Service"), CancellationToken.None));

        Assert.Equal("The manifest targets a different service.", ex.Message);
    }

    [Fact]
    public async Task ApplyAsync_skips_download_when_the_installed_version_already_matches()
    {
        var watchdogOptions = CreateWatchdogOptions();
        Directory.CreateDirectory(watchdogOptions.WorkerInstallDirectory);
        await File.WriteAllTextAsync(Path.Combine(watchdogOptions.WorkerInstallDirectory, ".version"), "  1.2.3  ");
        var handler = new StaticHandler("unused"u8.ToArray());
        var manager = CreateManager(handler);

        await manager.ApplyAsync(Manifest(version: "1.2.3", packageUrl: "http://example.local/worker.zip"), CancellationToken.None);

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ApplyAsync_validates_the_manifest_when_the_installed_version_differs()
    {
        var watchdogOptions = CreateWatchdogOptions();
        Directory.CreateDirectory(watchdogOptions.WorkerInstallDirectory);
        await File.WriteAllTextAsync(Path.Combine(watchdogOptions.WorkerInstallDirectory, ".version"), "1.0.0");
        var manager = CreateManager();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.ApplyAsync(Manifest(version: "1.2.3", packageUrl: "http://example.local/worker.zip"), CancellationToken.None));

        Assert.Equal("Update packages must use an absolute HTTPS URL.", ex.Message);
    }

    [Fact]
    public async Task VerifyHashAsync_accepts_a_matching_sha256_digest()
    {
        var path = Path.Combine(_root, "package.bin");
        Directory.CreateDirectory(_root);
        var payload = "worker-package"u8.ToArray();
        await File.WriteAllBytesAsync(path, payload);

        await WorkerUpdateManager.VerifyHashAsync(path, Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), CancellationToken.None);
    }

    [Fact]
    public async Task VerifyHashAsync_rejects_a_mismatched_sha256_digest()
    {
        var path = Path.Combine(_root, "package.bin");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(path, "worker-package"u8.ToArray());

        await Assert.ThrowsAsync<CryptographicException>(
            () => WorkerUpdateManager.VerifyHashAsync(path, ValidSha256, CancellationToken.None));
    }

    [Fact]
    public async Task ApplyAsync_rejects_a_downloaded_package_whose_sha256_does_not_match()
    {
        var package = CreateZip(("payload/readme.txt", "ok"u8.ToArray()));
        var handler = new StaticHandler(package);
        var manager = CreateManager(handler);

        var ex = await Assert.ThrowsAsync<CryptographicException>(
            () => manager.ApplyAsync(Manifest(sha256: ValidSha256), CancellationToken.None));

        Assert.Equal("The update package SHA-256 digest does not match the manifest.", ex.Message);
        Assert.Equal(1, handler.RequestCount);
        Assert.Empty(Directory.Exists(CreateWatchdogOptions().StagingDirectory) ? Directory.GetDirectories(CreateWatchdogOptions().StagingDirectory) : []);
    }

    [Fact]
    public async Task ApplyAsync_rejects_a_package_that_exceeds_the_download_limit()
    {
        var handler = new StaticHandler(new byte[32]);
        var manager = CreateManager(handler, options => options.MaxPackageBytes = 16);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => manager.ApplyAsync(Manifest(sha256: Convert.ToHexString(SHA256.HashData(new byte[32]))), CancellationToken.None));

        Assert.Equal("The update package exceeds the configured download limit.", ex.Message);
    }

    [Fact]
    public async Task ExtractSafelyAsync_extracts_nested_files_inside_the_destination()
    {
        var archivePath = Path.Combine(_root, "safe.zip");
        var destination = Path.Combine(_root, "extracted");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(archivePath, CreateZip(
            ("payload/", Array.Empty<byte>()),
            ("payload/readme.txt", "hello"u8.ToArray()),
            ("payload/nested/data.bin", "data"u8.ToArray())));

        await CreateManager().ExtractSafelyAsync(archivePath, destination, CancellationToken.None);

        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(destination, "payload", "readme.txt")));
        Assert.Equal("data", await File.ReadAllTextAsync(Path.Combine(destination, "payload", "nested", "data.bin")));
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("sub/../../evil.txt")]
    public async Task ExtractSafelyAsync_rejects_relative_path_traversal_entries(string entryName)
    {
        var archivePath = Path.Combine(_root, "slip.zip");
        var destination = Path.Combine(_root, "extracted");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(archivePath, CreateZip((entryName, "pwned"u8.ToArray())));

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => CreateManager().ExtractSafelyAsync(archivePath, destination, CancellationToken.None));

        Assert.Equal("The update archive contains an unsafe path.", ex.Message);
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
    }

    [Fact]
    public async Task ExtractSafelyAsync_rejects_an_absolute_path_entry()
    {
        var archivePath = Path.Combine(_root, "absolute.zip");
        var destination = Path.Combine(_root, "extracted");
        var absoluteTarget = Path.Combine(_root, "outside", "evil.txt");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(archivePath, CreateZip((absoluteTarget, "pwned"u8.ToArray())));

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => CreateManager().ExtractSafelyAsync(archivePath, destination, CancellationToken.None));

        Assert.Equal("The update archive contains an unsafe path.", ex.Message);
        Assert.False(File.Exists(absoluteTarget));
    }

    [Fact]
    public async Task ApplyAsync_rejects_a_zip_slip_package_before_replacing_the_install()
    {
        var package = CreateZip(("../evil.txt", "pwned"u8.ToArray()));
        var handler = new StaticHandler(package);
        var manager = CreateManager(handler);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => manager.ApplyAsync(Manifest(sha256: Convert.ToHexString(SHA256.HashData(package))), CancellationToken.None));

        Assert.Equal("The update archive contains an unsafe path.", ex.Message);
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
    }

    [Fact]
    public async Task ExtractSafelyAsync_rejects_an_archive_that_exceeds_the_extraction_limit()
    {
        var archivePath = Path.Combine(_root, "large.zip");
        var destination = Path.Combine(_root, "extracted");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(archivePath, CreateZip(("payload.bin", new byte[64])));

        var ex = await Assert.ThrowsAsync<InvalidDataException>(
            () => CreateManager(configure: options => options.MaxExtractedBytes = 32)
                .ExtractSafelyAsync(archivePath, destination, CancellationToken.None));

        Assert.Equal("The update package exceeds the configured extraction limit.", ex.Message);
    }

    [Fact]
    public void ResolveWorkerPayload_returns_the_directory_that_contains_the_single_worker_executable()
    {
        var extracted = Path.Combine(_root, "extracted");
        var payload = Path.Combine(extracted, "win-x64");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, WorkerExeName), "exe");

        Assert.Equal(payload, WorkerUpdateManager.ResolveWorkerPayload(extracted));
    }

    [Fact]
    public void ResolveWorkerPayload_rejects_packages_without_exactly_one_worker_executable()
    {
        var extracted = Path.Combine(_root, "extracted");
        Directory.CreateDirectory(extracted);
        var missing = Assert.Throws<InvalidDataException>(() => WorkerUpdateManager.ResolveWorkerPayload(extracted));
        Assert.Equal("The package must contain exactly one Worker executable.", missing.Message);

        File.WriteAllText(Path.Combine(extracted, WorkerExeName), "one");
        Directory.CreateDirectory(Path.Combine(extracted, "nested"));
        File.WriteAllText(Path.Combine(extracted, "nested", WorkerExeName), "two");
        var duplicated = Assert.Throws<InvalidDataException>(() => WorkerUpdateManager.ResolveWorkerPayload(extracted));
        Assert.Equal("The package must contain exactly one Worker executable.", duplicated.Message);
    }

    [Fact]
    public void PreserveConfigurationFiles_restores_existing_appsettings_over_the_package_payload()
    {
        var install = Path.Combine(_root, "install");
        var payload = Path.Combine(_root, "payload");
        var preserve = Path.Combine(_root, "preserve");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(install, "appsettings.json"), """{"Agent":{"DeviceCode":"pc-prod"}}""");
        File.WriteAllText(Path.Combine(install, "appsettings.Production.json"), """{"Agent":{"PollInterval":"01:00:00"}}""");
        File.WriteAllText(Path.Combine(install, "other.json"), "leave-behind");
        File.WriteAllText(Path.Combine(payload, "appsettings.json"), """{"Agent":{"DeviceCode":"pc-demo-001"}}""");
        File.WriteAllText(Path.Combine(payload, WorkerExeName), "exe");

        WorkerUpdateManager.PreserveConfigurationFiles(install, preserve);
        WorkerUpdateManager.TryDeleteDirectory(install);
        WorkerUpdateManager.CopyDirectory(payload, install);
        WorkerUpdateManager.RestoreConfigurationFiles(preserve, install);

        Assert.Equal("""{"Agent":{"DeviceCode":"pc-prod"}}""", File.ReadAllText(Path.Combine(install, "appsettings.json")));
        Assert.Equal("""{"Agent":{"PollInterval":"01:00:00"}}""", File.ReadAllText(Path.Combine(install, "appsettings.Production.json")));
        Assert.False(File.Exists(Path.Combine(install, "other.json")));
        Assert.Equal("exe", File.ReadAllText(Path.Combine(install, WorkerExeName)));
        Assert.False(File.Exists(Path.Combine(preserve, "other.json")));
    }

    [Fact]
    public void PreserveConfigurationFiles_leaves_package_appsettings_when_install_has_none()
    {
        var install = Path.Combine(_root, "install");
        var payload = Path.Combine(_root, "payload");
        var preserve = Path.Combine(_root, "preserve");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "appsettings.json"), """{"dev":true}""");
        File.WriteAllText(Path.Combine(payload, WorkerExeName), "exe");

        WorkerUpdateManager.PreserveConfigurationFiles(install, preserve);
        WorkerUpdateManager.CopyDirectory(payload, install);
        WorkerUpdateManager.RestoreConfigurationFiles(preserve, install);

        Assert.Equal("""{"dev":true}""", File.ReadAllText(Path.Combine(install, "appsettings.json")));
        Assert.False(Directory.Exists(preserve));
    }

    [Theory]
    [InlineData("appsettings.json", true)]
    [InlineData("appsettings.Production.json", true)]
    [InlineData("APPSETTINGS.Development.JSON", true)]
    [InlineData("other.json", false)]
    [InlineData("appsettingsfoo.json", false)]
    public void IsProtectedConfigurationFile_matches_appsettings_json_variants(string fileName, bool expected)
    {
        Assert.Equal(expected, WorkerUpdateManager.IsProtectedConfigurationFile(fileName));
    }

    [Fact]
    public void CopyDirectory_copies_nested_files_and_TryDeleteDirectory_removes_the_tree()
    {
        var source = Path.Combine(_root, "install");
        var backup = Path.Combine(_root, "backup", "worker-previous");
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        File.WriteAllText(Path.Combine(source, "worker.txt"), "current");
        File.WriteAllText(Path.Combine(source, "nested", "data.bin"), "blob");

        WorkerUpdateManager.CopyDirectory(source, backup);

        Assert.Equal("current", File.ReadAllText(Path.Combine(backup, "worker.txt")));
        Assert.Equal("blob", File.ReadAllText(Path.Combine(backup, "nested", "data.bin")));

        File.WriteAllText(Path.Combine(source, "worker.txt"), "broken");
        WorkerUpdateManager.TryDeleteDirectory(source);
        Assert.False(Directory.Exists(source));

        WorkerUpdateManager.CopyDirectory(backup, source);
        Assert.Equal("current", File.ReadAllText(Path.Combine(source, "worker.txt")));
        Assert.Equal("blob", File.ReadAllText(Path.Combine(source, "nested", "data.bin")));

        WorkerUpdateManager.TryDeleteDirectory(Path.Combine(_root, "missing"));
    }

    [Fact]
    public void IsWorkerHealthy_requires_a_matching_version_reported_after_the_update_started()
    {
        var startedAt = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
        var manager = CreateManager();
        var healthPath = CreateWatchdogOptions().WorkerHealthFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(healthPath)!);

        Assert.False(manager.IsWorkerHealthy("1.2.3", startedAt));

        File.WriteAllText(healthPath, "{not-json");
        Assert.False(manager.IsWorkerHealthy("1.2.3", startedAt));

        WriteHealth("1.2.3", startedAt.AddMinutes(-1));
        Assert.False(manager.IsWorkerHealthy("1.2.3", startedAt));

        WriteHealth("1.0.0", startedAt.AddMinutes(1));
        Assert.False(manager.IsWorkerHealthy("1.2.3", startedAt));

        WriteHealth("1.2.3", startedAt);
        Assert.True(manager.IsWorkerHealthy("1.2.3", startedAt));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private WorkerUpdateManager CreateManager(HttpMessageHandler? handler = null, Action<WatchdogOptions>? configure = null)
    {
        var options = CreateWatchdogOptions();
        configure?.Invoke(options);
        return new WorkerUpdateManager(
            new HttpClient(handler ?? new StaticHandler([])),
            Options.Create(options),
            NullLogger<WorkerUpdateManager>.Instance);
    }

    private WatchdogOptions CreateWatchdogOptions() =>
        new()
        {
            WorkerServiceName = "SwLicenseWatcher.Agent.Worker",
            StagingDirectory = Path.Combine(_root, "staging"),
            BackupDirectory = Path.Combine(_root, "backup"),
            WorkerInstallDirectory = Path.Combine(_root, "install"),
            WorkerHealthFilePath = Path.Combine(_root, "state", "worker-health.json"),
            MaxPackageBytes = 1024 * 1024,
            MaxExtractedBytes = 2 * 1024 * 1024
        };

    private void WriteHealth(string version, DateTimeOffset reportedAtUtc)
    {
        var healthPath = CreateWatchdogOptions().WorkerHealthFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(healthPath)!);
        var json = JsonSerializer.Serialize(
            new WorkerHealthReport("Worker", version, reportedAtUtc),
            InventoryJsonSerializerContext.Default.WorkerHealthReport);
        File.WriteAllText(healthPath, json);
    }

    private static UpdateManifest Manifest(
        string targetServiceName = "SwLicenseWatcher.Agent.Worker",
        string version = "1.2.3",
        string packageUrl = "https://example.local/worker.zip",
        string? sha256 = null,
        int rollbackAfterMinutes = 10) =>
        new(targetServiceName, version, packageUrl, sha256 ?? ValidSha256, RequireAuthenticode: false, rollbackAfterMinutes);

    private static byte[] CreateZip(params (string Name, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                if (content.Length == 0 && name.EndsWith('/'))
                {
                    continue;
                }

                using var output = entry.Open();
                output.Write(content);
            }
        }

        return stream.ToArray();
    }

    private sealed class StaticHandler(byte[] body, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new ByteArrayContent(body)
            });
        }
    }
}
