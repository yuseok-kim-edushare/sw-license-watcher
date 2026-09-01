using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

public sealed class StaleHeartbeatMonitor(
    IStaleHeartbeatNotificationStore store,
    NotificationPublisher publisher,
    IOptions<NotificationOptions> options,
    ILogger<StaleHeartbeatMonitor> logger) : BackgroundService
{
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

    internal async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        var current = options.Value;
        if (!current.Events.StaleHeartbeat || !current.HasEnabledChannel)
        {
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var cutoff = now - current.StaleHeartbeatThreshold;
            var newlyStale = await store.ClaimNewlyStaleHeartbeatsAsync(cutoff, now, cancellationToken);
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

    internal static List<StalePcHeartbeat> TakeNewlyStale(
        IReadOnlyList<StalePcHeartbeat> stalePcs,
        ConcurrentDictionary<string, byte> notifiedDeviceCodes)
    {
        var staleCodes = new HashSet<string>(stalePcs.Select(pc => pc.DeviceCode), StringComparer.OrdinalIgnoreCase);
        foreach (var deviceCode in notifiedDeviceCodes.Keys)
        {
            if (!staleCodes.Contains(deviceCode))
            {
                notifiedDeviceCodes.TryRemove(deviceCode, out _);
            }
        }

        return stalePcs.Where(pc => notifiedDeviceCodes.TryAdd(pc.DeviceCode, 0)).ToList();
    }
}
