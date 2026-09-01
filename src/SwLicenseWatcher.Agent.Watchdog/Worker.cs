using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;
using System.Security.Cryptography;

namespace SwLicenseWatcher.Agent.Watchdog;

public sealed class Worker(
    ILogger<Worker> logger,
    UpdateManifestClient manifestClient,
    WorkerUpdateManager updateManager,
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
                try
                {
                    await updateManager.ApplyAsync(manifest, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Worker update to {Version} failed.", manifest.Version);
                }
            }

            logger.LogInformation(
                "Worker service restarts rollback from {BackupDirectory} if health is not restored within the manifest timeout. Download checks are jittered by up to {MaxJitter}.",
                watchdogOptions.BackupDirectory,
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
