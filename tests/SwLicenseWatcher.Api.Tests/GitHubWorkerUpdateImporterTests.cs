using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Api;
using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api.Tests;

public class WorkerUpdatePackageStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "slw-pkg-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Save_and_resolve_a_safe_version_and_reject_traversal()
    {
        var store = CreateStore();
        await using var payload = new MemoryStream("worker-zip"u8.ToArray());
        await store.SavePackageAsync("v1.2.3", payload, CancellationToken.None);

        Assert.True(store.TryGetPackagePath("1.2.3", out var path));
        Assert.Equal("worker-zip", await File.ReadAllTextAsync(path));
        Assert.False(store.TryGetPackagePath("../evil", out _));
        Assert.False(store.TryGetPackagePath("missing", out _));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private WorkerUpdatePackageStore CreateStore() =>
        new(
            Options.Create(new GitHubUpdateOptions { PackageDirectory = _root }),
            new StubHostEnvironment(_root));
}

public class WorkerUpdatePackageUrlsTests
{
    [Fact]
    public void Builds_an_absolute_package_url_from_the_incoming_request()
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("license.contoso.local", 443);
        http.Request.PathBase = "/slw";

        Assert.Equal(
            "https://license.contoso.local/slw/api/updates/worker/package/1.2.3",
            WorkerUpdatePackageUrls.ForRequest(http.Request, "1.2.3"));
    }

    [Fact]
    public void Rewrites_the_pin_only_when_the_package_is_cached()
    {
        var root = Path.Combine(Path.GetTempPath(), "slw-url-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new WorkerUpdatePackageStore(
                Options.Create(new GitHubUpdateOptions { PackageDirectory = root }),
                new StubHostEnvironment(root));
            var pin = new UpdateManifest(
                "SwLicenseWatcher.Agent.Worker",
                "1.2.3",
                "https://github.com/org/repo/releases/download/1.2.3/SwLicenseWatcher.Agent.Worker-1.2.3.zip",
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                false,
                10);
            var http = new DefaultHttpContext();
            http.Request.Scheme = "https";
            http.Request.Host = new HostString("api.example.local");

            Assert.Equal(pin.PackageUrl, WorkerUpdatePackageUrls.WithLocalPackageUrlIfCached(pin, http.Request, store).PackageUrl);

            File.WriteAllBytes(Path.Combine(root, "SwLicenseWatcher.Agent.Worker-1.2.3.zip"), [1]);
            Assert.Equal(
                "https://api.example.local/api/updates/worker/package/1.2.3",
                WorkerUpdatePackageUrls.WithLocalPackageUrlIfCached(pin, http.Request, store).PackageUrl);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

public class GitHubWorkerUpdateImporterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "slw-gh-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ImportAsync_downloads_verifies_caches_and_pins()
    {
        var zip = "worker-package"u8.ToArray();
        var sha = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
        var sums = $"{sha}  SwLicenseWatcher.Agent.Worker-1.2.3.zip{Environment.NewLine}";
        var handler = new GitHubHandler(zip, sums);
        var store = new MemoryPinStore();
        var importer = CreateImporter(handler, store);

        var imported = await importer.ImportAsync(
            new GitHubWorkerUpdateImportRequest("1.2.3"),
            version => $"https://license.contoso.local/api/updates/worker/package/{version}",
            CancellationToken.None);

        Assert.Equal("1.2.3", imported.Version);
        Assert.Equal(sha, imported.Sha256);
        Assert.True(imported.Pinned);
        Assert.Equal("https://license.contoso.local/api/updates/worker/package/1.2.3", imported.PackageUrl);
        Assert.Equal("1.2.3", store.Pin?.Version);
        Assert.Equal(sha, store.Pin?.Sha256);
        Assert.Equal("https://license.contoso.local/api/updates/worker/package/1.2.3", store.Pin?.PackageUrl);
        Assert.False(store.Pin?.RequireAuthenticode);
        Assert.True(File.Exists(Path.Combine(_root, "SwLicenseWatcher.Agent.Worker-1.2.3.zip")));
        Assert.Contains("/releases/tags/1.2.3", handler.Paths, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSourceAsync_marks_cached_releases()
    {
        var zip = "worker-package"u8.ToArray();
        var sha = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
        var handler = new GitHubHandler(zip, $"{sha}  SwLicenseWatcher.Agent.Worker-1.2.3.zip\n");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(Path.Combine(_root, "SwLicenseWatcher.Agent.Worker-1.2.3.zip"), zip);
        var importer = CreateImporter(handler, new MemoryPinStore());

        var source = await importer.GetSourceAsync(CancellationToken.None);

        Assert.True(source.Configured);
        var release = Assert.Single(source.Releases);
        Assert.Equal("1.2.3", release.Version);
        Assert.True(release.HasWorkerPackage);
        Assert.True(release.HasChecksums);
        Assert.True(release.Cached);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private GitHubWorkerUpdateImporter CreateImporter(HttpMessageHandler handler, IWorkerUpdatePinStore pins)
    {
        var githubOptions = Options.Create(new GitHubUpdateOptions
        {
            Owner = "org",
            Repository = "repo",
            PackageDirectory = _root,
            MaxPackageBytes = 1024 * 1024
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SwLicenseWatcher.Api.Tests");
        var pinService = new WorkerUpdatePinService(
            pins,
            Options.Create(new UpdateManifestOptions
            {
                TargetServiceName = "SwLicenseWatcher.Agent.Worker",
                Version = "0.0.1",
                PackageUrl = "https://example.local/worker.zip",
                Sha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                RequireAuthenticode = false,
                RollbackAfterMinutes = 10
            }),
            Options.Create(new SqlServerStorageOptions { ConnectionString = "Server=.;" }),
            NullLogger<WorkerUpdatePinService>.Instance);
        return new GitHubWorkerUpdateImporter(
            new GitHubReleaseClient(http, githubOptions),
            new WorkerUpdatePackageStore(githubOptions, new StubHostEnvironment(_root)),
            pinService,
            githubOptions,
            NullLogger<GitHubWorkerUpdateImporter>.Instance);
    }

    private sealed class GitHubHandler(byte[] zip, string sums) : HttpMessageHandler
    {
        public string Paths { get; private set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            Paths += path + "\n";
            if (path.Contains("/releases/tags/", StringComparison.Ordinal) ||
                path.EndsWith("/releases/latest", StringComparison.Ordinal))
            {
                return Json(ReleaseJson, array: false);
            }

            if (path.EndsWith("/releases", StringComparison.Ordinal))
            {
                return Json(ReleaseJson, array: true);
            }

            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return Bytes(zip);
            }

            if (path.EndsWith("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
            {
                return Bytes(Encoding.UTF8.GetBytes(sums));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private const string ReleaseJson = """
            {
              "tag_name": "1.2.3",
              "published_at": "2026-01-01T00:00:00Z",
              "assets": [
                {
                  "name": "SwLicenseWatcher.Agent.Worker-1.2.3.zip",
                  "url": "https://api.github.com/repos/org/repo/releases/assets/1",
                  "browser_download_url": "https://github.com/org/repo/releases/download/1.2.3/SwLicenseWatcher.Agent.Worker-1.2.3.zip",
                  "size": 14
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

        private static Task<HttpResponseMessage> Json(string body, bool array)
        {
            var payload = array ? "[" + body + "]" : body;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }

        private static Task<HttpResponseMessage> Bytes(byte[] body) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body)
            });
    }

    private sealed class MemoryPinStore : IWorkerUpdatePinStore
    {
        public UpdateManifest? Pin { get; private set; } = new(
            "SwLicenseWatcher.Agent.Worker",
            "0.0.1",
            "https://example.local/worker.zip",
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            true,
            10);

        public Task<UpdateManifest?> GetWorkerUpdatePinAsync(string targetServiceName, CancellationToken cancellationToken) =>
            Task.FromResult(Pin);

        public Task<UpdateManifest> UpsertWorkerUpdatePinAsync(UpdateManifest pin, CancellationToken cancellationToken)
        {
            Pin = pin;
            return Task.FromResult(pin);
        }

        public Task<bool> SeedWorkerUpdatePinIfEmptyAsync(UpdateManifest pin, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}

internal sealed class StubHostEnvironment(string contentRoot) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "tests";
    public string ContentRootPath { get; set; } = contentRoot;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
