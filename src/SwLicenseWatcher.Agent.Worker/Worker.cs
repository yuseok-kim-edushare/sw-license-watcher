using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Worker;

public sealed class Worker(
    ILogger<Worker> logger,
    ISoftwareInventoryCollector inventoryCollector,
    AgentApiClient apiClient,
    LocalSnapshotQueue snapshotQueue,
    IOptions<WorkerAgentOptions> options,
    IOptions<LocalStateStoreOptions> localStateOptions,
    IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var agentOptions = options.Value;
        var localOptions = localStateOptions.Value;

        do
        {
            try
            {
                var queueDrained = await snapshotQueue.FlushAsync(apiClient, stoppingToken);
                var snapshot = await CollectSnapshotAsync(agentOptions, stoppingToken);
                await WriteHealthReportAsync(agentOptions, stoppingToken);
                logger.LogInformation(
                    "Collected inventory for {HostName} running {OperatingSystem}.",
                    snapshot.Pc.HostName,
                    snapshot.Pc.OperatingSystem);
                logger.LogInformation(
                    "Collected {SoftwareCount} software entries via uninstall registry keys. Win32_Product/WMI is intentionally not used.",
                    snapshot.InstalledSoftware.Count);
                logger.LogInformation(
                    "Local store-and-forward queue {QueueDirectory} uses DPAPI scope {DpapiScope}.",
                    localOptions.QueueDirectory,
                    localOptions.DpapiScope);

                var publishResult = AgentPublishResult.RetryableFailure;
                if (queueDrained)
                {
                    publishResult = await apiClient.PublishSnapshotAsync(snapshot, stoppingToken);
                    if (publishResult == AgentPublishResult.RetryableFailure)
                    {
                        await snapshotQueue.EnqueueAsync(snapshot, stoppingToken);
                    }
                }
                else
                {
                    await snapshotQueue.EnqueueAsync(snapshot, stoppingToken);
                }

                await apiClient.PublishHeartbeatAsync(
                    new AgentHeartbeat(
                        snapshot.Pc.DeviceCode,
                        snapshot.Pc.HostName,
                        "Worker",
                        snapshot.Pc.AgentVersion,
                        DateTimeOffset.UtcNow,
                        HeartbeatStatus.Resolve(queueDrained, publishResult)),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred during the inventory collection cycle.");
            }

            if (agentOptions.RunOnceForDiagnostics)
            {
                applicationLifetime.StopApplication();
                return;
            }

            try
            {
                await Task.Delay(JitterDelayCalculator.NextDelay(agentOptions.PollInterval, agentOptions.MaxJitter), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
        while (!stoppingToken.IsCancellationRequested);
    }

    private async Task WriteHealthReportAsync(WorkerAgentOptions agentOptions, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(agentOptions.HealthFilePath))
        {
            return;
        }

        var report = new WorkerHealthReport("Worker", ResolveInstalledVersion(), DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(report, InventoryJsonSerializerContext.Default.WorkerHealthReport);
        var temporaryPath = agentOptions.HealthFilePath + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(agentOptions.HealthFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(temporaryPath, json, Encoding.UTF8, cancellationToken);
            File.Move(temporaryPath, agentOptions.HealthFilePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Unable to publish the Worker health signal to {HealthFilePath}.", agentOptions.HealthFilePath);
        }
    }

    private string ResolveInstalledVersion()
    {
        var versionFile = Path.Combine(AppContext.BaseDirectory, ".version");
        try
        {
            if (File.Exists(versionFile))
            {
                var installedVersion = File.ReadAllText(versionFile).Trim();
                if (!string.IsNullOrEmpty(installedVersion))
                {
                    return installedVersion;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Unable to read the installed version from {VersionFile}.", versionFile);
        }

        return typeof(Worker).Assembly.GetName().Version?.ToString() ?? "1.0.0";
    }

    private async Task<InventoryIngestionRequest> CollectSnapshotAsync(WorkerAgentOptions agentOptions, CancellationToken cancellationToken)
    {
        var software = await inventoryCollector.CollectAsync(cancellationToken);
        var identity = new PcIdentity(
            agentOptions.DeviceCode,
            Environment.MachineName,
            agentOptions.DomainName,
            WindowsOsDescription.Resolve(logger),
            ResolveInstalledVersion());

        return new InventoryIngestionRequest(identity, software, DateTimeOffset.UtcNow);
    }
}
