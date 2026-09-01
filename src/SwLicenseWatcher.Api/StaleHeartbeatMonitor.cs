using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

public sealed class StaleHeartbeatMonitor(
    SqlServerInventoryRepository repository,
    NotificationPublisher publisher,
    IOptions<NotificationOptions> options,
    ILogger<StaleHeartbeatMonitor> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, byte> _notifiedDeviceCodes =
        new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.StaleHeartbeatCheckInterval;
        using var timer = new PeriodicTimer(interval);

        do
        {
            await CheckOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        var current = options.Value;
        if (!current.Events.StaleHeartbeat || !current.HasEnabledChannel)
        {
            return;
        }

        try
        {
            var cutoff = DateTimeOffset.UtcNow - current.StaleHeartbeatThreshold;
            var stalePcs = await repository.GetStaleHeartbeatsAsync(cutoff, cancellationToken);
            var staleCodes = new HashSet<string>(stalePcs.Select(pc => pc.DeviceCode), StringComparer.OrdinalIgnoreCase);
            foreach (var deviceCode in _notifiedDeviceCodes.Keys)
            {
                if (!staleCodes.Contains(deviceCode))
                {
                    _notifiedDeviceCodes.TryRemove(deviceCode, out _);
                }
            }

            var newlyStale = stalePcs.Where(pc => _notifiedDeviceCodes.TryAdd(pc.DeviceCode, 0)).ToList();
            if (newlyStale.Count == 0)
            {
                return;
            }

            publisher.EnqueueStaleHeartbeatsIfNeeded(newlyStale, current.StaleHeartbeatThreshold);
            logger.LogInformation(
                "Queued stale-heartbeat notification for {Count} PC(s).",
                newlyStale.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to inspect stale heartbeats.");
        }
    }
}
