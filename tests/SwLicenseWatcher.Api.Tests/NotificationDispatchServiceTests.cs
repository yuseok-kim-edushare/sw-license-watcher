using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Api;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api.Tests;

public class NotificationDispatchServiceTests
{
    [Fact]
    public async Task ExecuteAsync_delivers_queued_messages_to_every_sender_even_if_one_fails()
    {
        var publisher = CreatePublisher();
        var first = new RecordingSender();
        var failing = new FailingSender();
        var second = new RecordingSender();
        using var service = new NotificationDispatchService(
            publisher,
            [first, failing, second],
            NullLogger<NotificationDispatchService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            publisher.EnqueueStaleHeartbeatsIfNeeded(
                [new StalePcHeartbeat("PC-001", "HOST-A", DateTimeOffset.UtcNow)],
                TimeSpan.FromHours(24));

            await WaitUntilAsync(() => first.Messages.Count == 1 && second.Messages.Count == 1);

            var delivered = Assert.Single(first.Messages);
            Assert.Equal(delivered, Assert.Single(second.Messages));
            Assert.Contains("HOST-A", delivered.Subject, StringComparison.Ordinal);
            Assert.Equal(1, failing.Attempts);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static NotificationPublisher CreatePublisher()
    {
        var options = new NotificationOptions
        {
            Webhook =
            {
                Enabled = true,
                Url = "https://example.local/webhook"
            },
            Events =
            {
                StaleHeartbeat = true
            }
        };
        return new NotificationPublisher(Options.Create(options), NullLogger<NotificationPublisher>.Instance);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("The notification dispatch service did not deliver the queued message.");
    }

    private sealed class RecordingSender : INotificationSender
    {
        public List<NotificationMessage> Messages { get; } = [];

        public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
        {
            lock (Messages)
            {
                Messages.Add(message);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FailingSender : INotificationSender
    {
        public int Attempts { get; private set; }

        public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
        {
            Attempts++;
            throw new InvalidOperationException("sender failed");
        }
    }
}
