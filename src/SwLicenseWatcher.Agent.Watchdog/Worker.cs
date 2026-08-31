using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Watchdog;

public sealed class Worker(
    ILogger<Worker> logger,
    UpdateManifestClient manifestClient,
    IOptions<WatchdogOptions> options,
    IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var watchdogOptions = options.Value;

        do
        {
            var manifest = await manifestClient.TryGetManifestAsync(stoppingToken);
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (manifest is null)
            {
                logger.LogWarning("No update manifest could be retrieved for {WorkerServiceName}.", watchdogOptions.WorkerServiceName);
            }
            else
            {
                logger.LogInformation(
                    "Watchdog prepared update plan for {WorkerServiceName} -> version {Version}. SHA-256 verification required: {RequireSha256}; Authenticode required: {RequireAuthenticode}.",
                    watchdogOptions.WorkerServiceName,
                    manifest.Version,
                    !string.IsNullOrWhiteSpace(manifest.Sha256),
                    manifest.RequireAuthenticode);
            }

            logger.LogInformation(
                "Worker service restarts must rollback from {BackupDirectory} if healthy heartbeat is not restored within {WorkerHealthyTimeout}. Download checks are jittered by up to {MaxJitter}.",
                watchdogOptions.BackupDirectory,
                watchdogOptions.WorkerHealthyTimeout,
                watchdogOptions.MaxJitter);

            if (watchdogOptions.RunOnceForDiagnostics)
            {
                applicationLifetime.StopApplication();
                return;
            }

            try
            {
                await Task.Delay(JitterDelayCalculator.NextDelay(watchdogOptions.CheckInterval, watchdogOptions.MaxJitter), stoppingToken);
            }
            catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
        while (!stoppingToken.IsCancellationRequested);
    }
}
