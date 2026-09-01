namespace SwLicenseWatcher.Api;

public sealed class NotificationDispatchService(
    NotificationPublisher publisher,
    IEnumerable<INotificationSender> senders,
    ILogger<NotificationDispatchService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in publisher.Reader.ReadAllAsync(stoppingToken))
        {
            foreach (var sender in senders)
            {
                try
                {
                    await sender.SendAsync(message, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Notification sender {Sender} failed for '{Subject}'.", sender.GetType().Name, message.Subject);
                }
            }
        }
    }
}
