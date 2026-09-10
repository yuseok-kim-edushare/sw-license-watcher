using System.Text.Json;
using System.Runtime.Versioning;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Watchdog;

public sealed class WorkerHealthMonitor(
    IWorkerServiceControl serviceControl,
    IOptions<WatchdogOptions> options,
    ILogger<WorkerHealthMonitor> logger)
{
    private readonly WatchdogOptions _options = options.Value;

    [SupportedOSPlatform("windows")]
    public async Task WaitForHealthAsync(
        string expectedVersion,
        DateTimeOffset startedAtUtc,
        TimeSpan healthTimeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + healthTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (serviceControl.IsRunning() && IsWorkerHealthy(expectedVersion, startedAtUtc))
            {
                return;
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(remaining < TimeSpan.FromSeconds(5) ? remaining : TimeSpan.FromSeconds(5), cancellationToken);
        }

        throw new TimeoutException("Worker health was not restored before the rollback deadline.");
    }

    internal bool IsWorkerHealthy(string expectedVersion, DateTimeOffset startedAtUtc)
    {
        try
        {
            if (!File.Exists(_options.WorkerHealthFilePath))
            {
                return false;
            }

            var report = JsonSerializer.Deserialize(
                File.ReadAllText(_options.WorkerHealthFilePath),
                InventoryJsonSerializerContext.Default.WorkerHealthReport);
            return report is not null &&
                report.ReportedAtUtc >= startedAtUtc &&
                string.Equals(report.Version, expectedVersion, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogDebug(ex, "Worker health signal {HealthFilePath} is not readable yet.", _options.WorkerHealthFilePath);
            return false;
        }
    }
}
