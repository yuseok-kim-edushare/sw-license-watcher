using System.Net;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Agent.Watchdog;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Watchdog.Tests;

public class PackageDownloaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "slw-dl-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DownloadAsync_sends_the_agent_token_only_to_the_configured_API_origin()
    {
        Directory.CreateDirectory(_root);
        var handler = new RecordingHandler("pkg"u8.ToArray());
        var options = Options.Create(new WatchdogOptions
        {
            ServerBaseUrl = "https://license.contoso.local",
            ApiToken = "agent-token-abcdefghijklmnopqrst",
            MaxPackageBytes = 1024
        });
        var downloader = new PackageDownloader(new HttpClient(handler), options);

        await downloader.DownloadAsync(
            new Uri("https://license.contoso.local/api/updates/worker/package/1.2.3"),
            Path.Combine(_root, "same-origin.zip"),
            CancellationToken.None);
        await downloader.DownloadAsync(
            new Uri("https://github.com/org/repo/releases/download/1.2.3/worker.zip"),
            Path.Combine(_root, "github.zip"),
            CancellationToken.None);

        Assert.Equal(2, handler.Authorizations.Count);
        Assert.Equal("Bearer agent-token-abcdefghijklmnopqrst", handler.Authorizations[0]);
        Assert.Null(handler.Authorizations[1]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private sealed class RecordingHandler(byte[] body) : HttpMessageHandler
    {
        public List<string?> Authorizations { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorizations.Add(request.Headers.Authorization?.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body)
            });
        }
    }
}
