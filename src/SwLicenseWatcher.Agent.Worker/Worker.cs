using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;
using SwLicenseWatcher.Crypto;

namespace SwLicenseWatcher.Agent.Worker;

public sealed class Worker(
    ILogger<Worker> logger,
    ISoftwareInventoryCollector inventoryCollector,
    AgentApiClient apiClient,
    LocalSnapshotQueue snapshotQueue,
    IOptions<WorkerAgentOptions> options,
    IOptions<LocalStateStoreOptions> localStateOptions,
    AgentAssignmentStore assignmentStore,
    AgentDeviceIdentityStore identityStore,
    RemoteAgentUninstaller remoteUninstaller,
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
                    var snapshotOutcome = await apiClient.PublishSnapshotAsync(snapshot, stoppingToken);
                    publishResult = snapshotOutcome.Result;
                    await ApplyAssignmentAsync(snapshotOutcome, stoppingToken);
                    if (await TryRemoteUninstallAsync(snapshotOutcome, stoppingToken))
                    {
                        return;
                    }
                    if (publishResult == AgentPublishResult.RetryableFailure)
                    {
                        await snapshotQueue.EnqueueAsync(snapshot, stoppingToken);
                    }
                }
                else
                {
                    await snapshotQueue.EnqueueAsync(snapshot, stoppingToken);
                }

                var heartbeatOutcome = await apiClient.PublishHeartbeatAsync(
                    new AgentHeartbeat(
                        snapshot.Pc.DeviceCode,
                        snapshot.Pc.HostName,
                        "Worker",
                        snapshot.Pc.AgentVersion,
                        DateTimeOffset.UtcNow,
                        HeartbeatStatus.Resolve(queueDrained, publishResult),
                        snapshot.Pc.DeviceId,
                        snapshot.Pc.DevicePublicKey,
                        snapshot.Pc.DeviceCertificate,
                        snapshot.Pc.DeviceProof),
                    stoppingToken);
                await ApplyAssignmentAsync(heartbeatOutcome, stoppingToken);
                if (await TryRemoteUninstallAsync(heartbeatOutcome, stoppingToken))
                {
                    return;
                }
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
        var stored = identityStore.Ensure();
        var deviceCode = assignmentStore.ResolveDeviceCode(agentOptions.DeviceCode);
        var proof = "";
        if (MldsaDeviceCrypto.TryFromBase64(stored.PrivateKey, out var privateKey))
        {
            proof = MldsaDeviceCrypto.ToBase64(
                MldsaDeviceCrypto.Sign(privateKey, DeviceProofs.Payload(stored.DeviceId ?? "", deviceCode)));
        }

        var identity = new PcIdentity(
            deviceCode,
            assignmentStore.ResolveHostName(Environment.MachineName),
            agentOptions.DomainName,
            WindowsOsDescription.Resolve(logger),
            ResolveInstalledVersion(),
            stored.DeviceId,
            stored.PublicKey,
            stored.Certificate,
            proof);

        return new InventoryIngestionRequest(identity, software, DateTimeOffset.UtcNow);
    }

    private async Task ApplyAssignmentAsync(AgentPublishOutcome outcome, CancellationToken cancellationToken)
    {
        await assignmentStore.ApplyLabelsAsync(
            outcome.AssignmentSpecified,
            outcome.AssignedHostName,
            outcome.DeviceCodeSpecified,
            outcome.AssignedDeviceCode,
            cancellationToken);
        identityStore.ApplyCertificate(outcome.DeviceId, outcome.DeviceCertificate);
    }

    private async Task<bool> TryRemoteUninstallAsync(AgentPublishOutcome outcome, CancellationToken cancellationToken)
    {
        if (outcome.Result != AgentPublishResult.Succeeded || outcome.UninstallCommand is null)
        {
            return false;
        }

        var deviceCode = assignmentStore.ResolveDeviceCode(options.Value.DeviceCode);
        try
        {
            if (!await remoteUninstaller.ExecuteAsync(deviceCode, outcome.UninstallCommand, cancellationToken))
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Control-plane uninstall for {DeviceCode} failed after the grant was issued.", deviceCode);
            return false;
        }

        logger.LogInformation("Control-plane uninstall started for {DeviceCode}. Worker is stopping.", deviceCode);
        applicationLifetime.StopApplication();
        return true;
    }
}
