using System.IO.Compression;
using System.Net;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Api;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api.Tests;

public class ServerUpdateZipTests
{
    [Fact]
    public void Rejects_entries_that_escape_the_destination()
    {
        var root = Path.Combine(Path.GetTempPath(), "slw-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var zip = ZipOf(("../outside.txt", "nope"));
            var ex = Assert.Throws<InvalidDataException>(() => ServerUpdateZip.Extract(zip, root, 1024 * 1024));
            Assert.Contains("escapes", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(Directory.GetParent(root)!.FullName, "outside.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Repacks_a_directory_with_the_same_relative_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), "slw-zip-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(Path.Combine(source, "watchdog-update"));
        File.WriteAllText(Path.Combine(source, "worker.dll"), "worker");
        File.WriteAllText(Path.Combine(source, "watchdog-update", "watchdog.exe"), "watchdog");
        var packed = Path.Combine(root, "packed.zip");
        var extracted = Path.Combine(root, "extracted");
        try
        {
            ServerUpdateZip.CreateFromDirectory(source, packed);
            using var file = File.OpenRead(packed);
            ServerUpdateZip.Extract(file, extracted, 1024 * 1024);
            Assert.Equal("worker", File.ReadAllText(Path.Combine(extracted, "worker.dll")));
            Assert.Equal("watchdog", File.ReadAllText(Path.Combine(extracted, "watchdog-update", "watchdog.exe")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stops_when_the_expanded_package_is_too_large()
    {
        var root = Path.Combine(Path.GetTempPath(), "slw-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var zip = ZipOf(("SwLicenseWatcher.Api.exe", "0123456789"));
            Assert.Throws<InvalidDataException>(() => ServerUpdateZip.Extract(zip, root, 4));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static MemoryStream ZipOf(params (string Name, string Contents)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, contents) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(contents);
            }
        }

        stream.Position = 0;
        return stream;
    }
}

public class ServerUpdateCoordinatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "slw-server-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Import_and_apply_use_the_package_for_this_host()
    {
        var install = Path.Combine(_root, "api");
        var staging = Path.Combine(_root, "staging");
        Directory.CreateDirectory(install);
        await File.WriteAllTextAsync(Path.Combine(install, ".version"), "1.0.0", TestContext.Current.CancellationToken);
        var zip = ZipBytes(("SwLicenseWatcher.Api.exe", "new-binary"), ("appsettings.json", "from-package"));
        var sha = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
        var sums = $"{sha}  SwLicenseWatcher.Api.win-x64-1.2.3.zip\n";
        var trigger = new RecordingTrigger();
        var coordinator = Create(install, staging, zip, sums, trigger, ServerUpdateHostKind.WindowsService);

        var imported = await coordinator.ImportAsync(new ServerUpdateImportRequest("1.2.3"), CancellationToken.None);
        Assert.Equal("SwLicenseWatcher.Api.win-x64-1.2.3.zip", imported.PackageFileName);
        Assert.Equal("new-binary", await File.ReadAllTextAsync(Path.Combine(staging, "payload", "SwLicenseWatcher.Api.exe"), TestContext.Current.CancellationToken));

        var applied = await coordinator.ApplyAsync(new ServerUpdateApplyRequest("1.2.3"), CancellationToken.None);
        Assert.Equal("1.2.3", applied.Version);
        Assert.NotNull(trigger.Config);
        Assert.Equal(ServerUpdateHostKind.WindowsService, trigger.Config.HostKind);
        Assert.Contains("Version=1.2.3", await File.ReadAllTextAsync(Path.Combine(staging, "request.conf"), TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_without_a_helper_is_rejected()
    {
        var install = Path.Combine(_root, "api");
        Directory.CreateDirectory(install);
        var coordinator = Create(install, Path.Combine(_root, "staging"), [], "", new RecordingTrigger(), ServerUpdateHostKind.WindowsService, writeHelper: false);

        var ex = await Assert.ThrowsAsync<ServerUpdateException>(() =>
            coordinator.ImportAsync(new ServerUpdateImportRequest("1.2.3"), CancellationToken.None));
        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);
        Assert.Contains("Install-ApiUpdateHelper.ps1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_signs_the_payload_and_apply_stops_when_the_signature_is_not_trusted()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "signtool signs Windows PE files.");
        var signTool = TestCodeSigning.FindSignTool();
        Assert.SkipUnless(signTool is not null, "signtool.exe was not found under the Windows SDK.");
        var install = Path.Combine(_root, "api");
        var staging = Path.Combine(_root, "staging");
        Directory.CreateDirectory(install);
        Directory.CreateDirectory(_root);
        var pfx = TestCodeSigning.WritePfx(_root, "test-password");
        var zip = TestCodeSigning.ZipAssembly("SwLicenseWatcher.Api.dll");
        var sha = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
        var trigger = new RecordingTrigger();
        var coordinator = Create(
            install,
            staging,
            zip,
            $"{sha}  SwLicenseWatcher.Api.win-x64-1.2.3.zip\n",
            trigger,
            ServerUpdateHostKind.WindowsService,
            signer: new LocalCertificatePackageSigner(signTool!, pfx, "test-password", signServer: true),
            maxPackageBytes: 64 * 1024 * 1024);

        var imported = await coordinator.ImportAsync(new ServerUpdateImportRequest("1.2.3"), CancellationToken.None);
        Assert.Equal(sha, imported.Sha256);
        var signedDll = Path.Combine(staging, "payload", "SwLicenseWatcher.Api.dll");
        Assert.True(SignedFileCertificate.HasCertificate(signedDll));

        Assert.Contains("Signed=true", await File.ReadAllTextAsync(Path.Combine(staging, "ready.conf"), TestContext.Current.CancellationToken), StringComparison.Ordinal);
        var ex = await Assert.ThrowsAsync<ServerUpdateException>(() =>
            coordinator.ApplyAsync(new ServerUpdateApplyRequest("1.2.3"), CancellationToken.None));
        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);
        Assert.Contains("Authenticode", ex.Message, StringComparison.Ordinal);
        Assert.Null(trigger.Config);
    }

    [Fact]
    public async Task Import_reports_an_unsigned_server_package_when_server_signing_was_requested()
    {
        var install = Path.Combine(_root, "api");
        var staging = Path.Combine(_root, "staging");
        Directory.CreateDirectory(install);
        var zip = ZipBytes(("SwLicenseWatcher.Api.exe", "new-binary"));
        var sha = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
        var unsigned = new GitHubWorkerUpdateImporterTests.RecordingUnsignedPackageNotifier();
        var coordinator = Create(
            install,
            staging,
            zip,
            $"{sha}  SwLicenseWatcher.Api.win-x64-1.2.3.zip\n",
            new RecordingTrigger(),
            ServerUpdateHostKind.WindowsService,
            signer: new ServerSigningRequestedSigner(),
            unsignedPackages: unsigned);

        await coordinator.ImportAsync(new ServerUpdateImportRequest("1.2.3"), CancellationToken.None);

        Assert.Equal([("server", "1.2.3")], unsigned.Calls);
        Assert.DoesNotContain("Signed=true", await File.ReadAllTextAsync(Path.Combine(staging, "ready.conf"), TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private ServerUpdateCoordinator Create(
        string install,
        string staging,
        byte[] zip,
        string sums,
        RecordingTrigger trigger,
        ServerUpdateHostKind kind,
        bool writeHelper = true,
        IUpdatePackageSigner? signer = null,
        long maxPackageBytes = 1024 * 1024,
        GitHubWorkerUpdateImporterTests.RecordingUnsignedPackageNotifier? unsignedPackages = null)
    {
        if (writeHelper)
        {
            Directory.CreateDirectory(_root);
            var config = new ServerUpdateHelperConfig
            {
                HostKind = kind,
                InstallPath = install,
                ServiceName = "SwLicenseWatcher.Api",
                AppPoolName = kind == ServerUpdateHostKind.Iis ? "SwLicenseWatcher" : "",
                StagingPath = staging,
                TaskName = ServerUpdateLayout.WindowsTaskName,
                UpdateUnitName = ServerUpdateLayout.LinuxUpdateUnitName
            };
            File.WriteAllText(Path.Combine(_root, "helper.conf"), ServerUpdateConfigFile.Format(config));
        }

        var githubOptions = Options.Create(new GitHubUpdateOptions
        {
            Owner = "org",
            Repository = "repo",
            PackageDirectory = _root,
            MaxPackageBytes = maxPackageBytes
        });
        var http = new HttpClient(new ServerPackageHandler(zip, sums)) { BaseAddress = new Uri("https://api.github.com/") };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SwLicenseWatcher.Api.Tests");
        return new ServerUpdateCoordinator(
            new GitHubReleaseClient(http, githubOptions),
            githubOptions,
            Options.Create(new ServerUpdateOptions { HelperConfigPath = Path.Combine(_root, "helper.conf") }),
            new FixedServerEnvironment(install, kind),
            trigger,
            signer ?? DisabledUpdatePackageSigner.Instance,
            unsignedPackages ?? new GitHubWorkerUpdateImporterTests.RecordingUnsignedPackageNotifier(),
            NullLogger<ServerUpdateCoordinator>.Instance);
    }

    private static byte[] ZipBytes(params (string Name, string Contents)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, contents) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(contents);
            }
        }

        return stream.ToArray();
    }

    private sealed class FixedServerEnvironment(string contentRoot, ServerUpdateHostKind kind) : IServerUpdateEnvironment
    {
        public ServerUpdateHostKind? HostKind => kind;
        public string ContentRootPath => contentRoot;
        public string CurrentVersion => "1.0.0";
        public string? AppPoolName => kind == ServerUpdateHostKind.Iis ? "SwLicenseWatcher" : null;
    }

    private sealed class ServerSigningRequestedSigner : IUpdatePackageSigner
    {
        public bool SignClientPackages => false;

        public bool SignServerPackages => true;

        public bool ShouldSignClient => false;

        public bool ShouldSignServer => false;

        public Task SignBinariesAsync(string directory, CancellationToken cancellationToken) =>
            Task.FromException(new UpdatePackageSignException("Signing was not supposed to run."));
    }

    private sealed class RecordingTrigger : IServerUpdateTrigger
    {
        public ServerUpdateHelperConfig? Config { get; private set; }

        public Task TriggerAsync(ServerUpdateHelperConfig config, CancellationToken cancellationToken)
        {
            Config = config;
            return Task.CompletedTask;
        }
    }

    private sealed class ServerPackageHandler(byte[] zip, string sums) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("/releases/tags/", StringComparison.Ordinal) || path.EndsWith("/releases/latest", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(ReleaseJson(false)));
            }

            if (path.EndsWith("/releases", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(ReleaseJson(true)));
            }

            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });
            }

            if (path.EndsWith("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes(sums))
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static string ReleaseJson(bool array)
        {
            const string body = """
                {
                  "tag_name": "1.2.3",
                  "published_at": "2026-01-01T00:00:00Z",
                  "assets": [
                    {
                      "name": "SwLicenseWatcher.Api.win-x64-1.2.3.zip",
                      "url": "https://api.github.com/repos/org/repo/releases/assets/1",
                      "browser_download_url": "https://github.com/org/repo/releases/download/1.2.3/SwLicenseWatcher.Api.win-x64-1.2.3.zip",
                      "size": 20
                    },
                    {
                      "name": "SHA256SUMS.txt",
                      "url": "https://api.github.com/repos/org/repo/releases/assets/2",
                      "browser_download_url": "https://github.com/org/repo/releases/download/1.2.3/SHA256SUMS.txt",
                      "size": 80
                    }
                  ]
                }
                """;
            return array ? "[" + body + "]" : body;
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
    }
}
