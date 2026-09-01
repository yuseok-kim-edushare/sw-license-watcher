using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Agent.Worker;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Worker.Tests;

public class LocalSnapshotQueueTests : IDisposable
{
    private readonly string _queueDirectory = Path.Combine(Path.GetTempPath(), "slw-queue-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task EnqueueAsync_writes_a_snapshot_file()
    {
        var queue = CreateQueue();

        await queue.EnqueueAsync(CreateSnapshot(), CancellationToken.None);

        var files = Directory.GetFiles(_queueDirectory, "*.snapshot");
        Assert.Single(files);
        Assert.False(File.Exists(files[0] + ".tmp"));
    }

    [Fact]
    public async Task FlushAsync_delivers_queued_snapshots_and_deletes_them()
    {
        var queue = CreateQueue();
        await queue.EnqueueAsync(CreateSnapshot("PC-A"), CancellationToken.None);
        var handler = new RecordingHandler(HttpStatusCode.Accepted);

        var delivered = await queue.FlushAsync(CreateApiClient(handler), CancellationToken.None);

        Assert.True(delivered);
        Assert.Equal(1, handler.RequestCount);
        Assert.Empty(Directory.GetFiles(_queueDirectory, "*.snapshot"));
        Assert.Empty(Directory.GetFiles(_queueDirectory, "*.invalid"));
    }

    [Fact]
    public async Task FlushAsync_keeps_the_file_when_delivery_is_retryable()
    {
        var queue = CreateQueue();
        await queue.EnqueueAsync(CreateSnapshot(), CancellationToken.None);
        var handler = new RecordingHandler(HttpStatusCode.InternalServerError);

        var delivered = await queue.FlushAsync(CreateApiClient(handler), CancellationToken.None);

        Assert.False(delivered);
        Assert.Single(Directory.GetFiles(_queueDirectory, "*.snapshot"));
    }

    [Fact]
    public async Task FlushAsync_quarantines_the_file_when_delivery_is_rejected()
    {
        var queue = CreateQueue();
        await queue.EnqueueAsync(CreateSnapshot(), CancellationToken.None);
        var snapshotPath = Directory.GetFiles(_queueDirectory, "*.snapshot").Single();

        var delivered = await queue.FlushAsync(
            CreateApiClient(new RecordingHandler(HttpStatusCode.Unauthorized)),
            CancellationToken.None);

        Assert.True(delivered);
        Assert.False(File.Exists(snapshotPath));
        Assert.True(File.Exists(snapshotPath + ".invalid"));
    }

    [Fact]
    public async Task FlushAsync_quarantines_corrupt_payloads_as_invalid()
    {
        Directory.CreateDirectory(_queueDirectory);
        var corruptPath = Path.Combine(_queueDirectory, "corrupt.snapshot");
        await File.WriteAllTextAsync(corruptPath, "not-a-snapshot");

        var delivered = await CreateQueue().FlushAsync(CreateApiClient(new RecordingHandler(HttpStatusCode.Accepted)), CancellationToken.None);

        Assert.True(delivered);
        Assert.False(File.Exists(corruptPath));
        Assert.True(File.Exists(corruptPath + ".invalid"));
    }

    [Fact]
    public async Task FlushAsync_quarantines_payloads_that_fail_unprotect()
    {
        Directory.CreateDirectory(_queueDirectory);
        var path = Path.Combine(_queueDirectory, "protected.snapshot");
        await File.WriteAllTextAsync(path, "ciphertext");
        var queue = CreateQueue(protector: new ThrowingProtector());

        var delivered = await queue.FlushAsync(CreateApiClient(new RecordingHandler(HttpStatusCode.Accepted)), CancellationToken.None);

        Assert.True(delivered);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".invalid"));
    }

    [Fact]
    public async Task FlushAsync_returns_true_when_the_queue_directory_is_missing()
    {
        var delivered = await CreateQueue().FlushAsync(CreateApiClient(new RecordingHandler(HttpStatusCode.Accepted)), CancellationToken.None);

        Assert.True(delivered);
        Assert.False(Directory.Exists(_queueDirectory));
    }

    [Fact]
    public async Task Queue_evicts_oldest_files_when_snapshot_count_exceeds_quota()
    {
        Directory.CreateDirectory(_queueDirectory);
        var oldest = Path.Combine(_queueDirectory, "0001.snapshot");
        await File.WriteAllTextAsync(oldest, "stale-1");
        await File.WriteAllTextAsync(Path.Combine(_queueDirectory, "0002.snapshot"), "stale-2");
        await File.WriteAllTextAsync(Path.Combine(_queueDirectory, "0003.snapshot"), "stale-3");

        await CreateQueue(maxQueuedSnapshots: 2).FlushAsync(
            CreateApiClient(new RecordingHandler(HttpStatusCode.Accepted)),
            CancellationToken.None);

        Assert.False(File.Exists(oldest));
        Assert.True(File.Exists(Path.Combine(_queueDirectory, "0002.snapshot.invalid")));
        Assert.True(File.Exists(Path.Combine(_queueDirectory, "0003.snapshot.invalid")));
    }

    [Fact]
    public async Task Queue_evicts_oldest_files_when_total_bytes_exceed_quota()
    {
        Directory.CreateDirectory(_queueDirectory);
        var oldest = Path.Combine(_queueDirectory, "0001.snapshot");
        await File.WriteAllBytesAsync(oldest, new byte[1000]);
        await File.WriteAllBytesAsync(Path.Combine(_queueDirectory, "0002.snapshot"), new byte[1000]);
        await File.WriteAllBytesAsync(Path.Combine(_queueDirectory, "0003.snapshot"), new byte[1000]);

        await CreateQueue(maxQueueBytes: 1500).FlushAsync(
            CreateApiClient(new RecordingHandler(HttpStatusCode.Accepted)),
            CancellationToken.None);

        Assert.False(File.Exists(oldest));
        Assert.False(File.Exists(Path.Combine(_queueDirectory, "0002.snapshot")));
        Assert.True(File.Exists(Path.Combine(_queueDirectory, "0003.snapshot.invalid")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_queueDirectory))
        {
            Directory.Delete(_queueDirectory, true);
        }
    }

    private LocalSnapshotQueue CreateQueue(
        int maxQueuedSnapshots = 48,
        long maxQueueBytes = 64 * 1024 * 1024,
        ILocalStateProtector? protector = null) =>
        new(
            new LocalStateStoreOptions
            {
                QueueDirectory = _queueDirectory,
                MaxQueuedSnapshots = maxQueuedSnapshots,
                MaxQueueBytes = maxQueueBytes
            },
            protector ?? new PassthroughProtector(),
            new SilentLogger());

    private static AgentApiClient CreateApiClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1") };
        var options = Options.Create(new WorkerAgentOptions
        {
            ServerBaseUrl = "http://127.0.0.1",
            SnapshotPath = "/api/inventory/snapshots",
            ApiToken = "test-token"
        });
        return new AgentApiClient(httpClient, new SilentLogger<AgentApiClient>(), options);
    }

    private static InventoryIngestionRequest CreateSnapshot(string deviceCode = "PC-001") =>
        new(
            new PcIdentity(deviceCode, "host-a", "CORP", "Windows 11", "1.0.0"),
            [new InstalledSoftwareEntry("Widget", "1.2.3", "Acme", @"C:\Program Files\Widget", "Machine", "Registry.Uninstall")],
            new DateTimeOffset(2026, 1, 15, 8, 30, 0, TimeSpan.Zero));

    private sealed class PassthroughProtector : ILocalStateProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string protectedPayload) => protectedPayload;
    }

    private sealed class ThrowingProtector : ILocalStateProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string protectedPayload) => throw new CryptographicException("unprotect failed");
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }

    private sealed class SilentLogger : ILogger<LocalSnapshotQueue>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }

    private sealed class SilentLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
