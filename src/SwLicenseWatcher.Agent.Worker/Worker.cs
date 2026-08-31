using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Worker;

public sealed class Worker(
    ILogger<Worker> logger,
    ISoftwareInventoryCollector inventoryCollector,
    AgentApiClient apiClient,
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
            var snapshot = await CollectSnapshotAsync(agentOptions, stoppingToken);
            logger.LogInformation(
                "Collected {SoftwareCount} software entries via uninstall registry keys. Win32_Product/WMI is intentionally not used.",
                snapshot.InstalledSoftware.Count);
            logger.LogInformation(
                "Local agent state is designed for ESENT file {EsentDatabaseFilePath} with DPAPI scope {DpapiScope}.",
                localOptions.EsentDatabaseFilePath,
                localOptions.DpapiScope);

            await apiClient.PublishSnapshotAsync(snapshot, stoppingToken);
            await apiClient.PublishHeartbeatAsync(
                new AgentHeartbeat(
                    snapshot.Pc.DeviceCode,
                    snapshot.Pc.HostName,
                    "Worker",
                    snapshot.Pc.AgentVersion,
                    DateTimeOffset.UtcNow,
                    "Healthy"),
                stoppingToken);

            if (agentOptions.RunOnceForDiagnostics)
            {
                applicationLifetime.StopApplication();
                return;
            }

            await Task.Delay(JitterDelayCalculator.NextDelay(agentOptions.PollInterval, agentOptions.MaxJitter), stoppingToken);
        }
        while (!stoppingToken.IsCancellationRequested);
    }

    private async Task<InventoryIngestionRequest> CollectSnapshotAsync(WorkerAgentOptions agentOptions, CancellationToken cancellationToken)
    {
        var software = await inventoryCollector.CollectAsync(cancellationToken);
        var identity = new PcIdentity(
            agentOptions.DeviceCode,
            Environment.MachineName,
            agentOptions.DomainName,
            Environment.OSVersion.VersionString,
            typeof(Worker).Assembly.GetName().Version?.ToString() ?? "1.0.0");

        return new InventoryIngestionRequest(identity, software, DateTimeOffset.UtcNow);
    }
}
