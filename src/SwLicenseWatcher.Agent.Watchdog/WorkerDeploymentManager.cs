using System.Runtime.Versioning;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Watchdog;

public interface IWorkerDeploymentManager
{
    Task DeployAndVerifyAsync(
        string source,
        string version,
        TimeSpan healthTimeout,
        CancellationToken cancellationToken);
}

public sealed class WorkerDeploymentManager(
    IWorkerServiceControl serviceControl,
    WorkerHealthMonitor healthMonitor,
    WorkerUpdateFileSystem fileSystem,
    IOptions<WatchdogOptions> options,
    ILogger<WorkerDeploymentManager> logger) : IWorkerDeploymentManager
{
    private readonly WatchdogOptions _options = options.Value;

    [SupportedOSPlatform("windows")]
    public async Task DeployAndVerifyAsync(
        string source,
        string version,
        TimeSpan healthTimeout,
        CancellationToken cancellationToken)
    {
        var backup = Path.Combine(_options.BackupDirectory, "worker-previous");
        if (!File.Exists(Path.Combine(_options.WorkerInstallDirectory, "SwLicenseWatcher.Agent.Worker.exe")))
        {
            throw new InvalidDataException("The Worker installation does not contain the expected executable.");
        }

        await serviceControl.StopAsync(cancellationToken);
        var installReplaced = false;
        var startedAtUtc = DateTimeOffset.UtcNow;
        try
        {
            var preservedConfigDirectory = Path.Combine(
                _options.StagingDirectory,
                "preserved-config-" + Guid.NewGuid().ToString("N"));
            try
            {
                fileSystem.TryDeleteDirectory(backup);
                if (Directory.Exists(_options.WorkerInstallDirectory))
                {
                    fileSystem.CopyDirectory(_options.WorkerInstallDirectory, backup);
                }

                fileSystem.PreserveConfigurationFiles(_options.WorkerInstallDirectory, preservedConfigDirectory);
                installReplaced = true;
                fileSystem.TryDeleteDirectory(_options.WorkerInstallDirectory);
                fileSystem.CopyDirectory(source, _options.WorkerInstallDirectory);
                fileSystem.RestoreConfigurationFiles(preservedConfigDirectory, _options.WorkerInstallDirectory);
                await File.WriteAllTextAsync(
                    Path.Combine(_options.WorkerInstallDirectory, ".version"),
                    version,
                    cancellationToken);
                await serviceControl.StartAsync(cancellationToken);
                await healthMonitor.WaitForHealthAsync(version, startedAtUtc, healthTimeout, cancellationToken);
                logger.LogInformation("Worker service updated successfully to {Version}.", version);
            }
            finally
            {
                fileSystem.TryDeleteDirectory(preservedConfigDirectory);
            }
        }
        catch
        {
            logger.LogError("Worker update failed; restoring backup.");
            await RollbackAsync(backup, installReplaced);
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    private async Task RollbackAsync(string backup, bool installReplaced)
    {
        try
        {
            await serviceControl.StopAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to stop the Worker service before restoring the backup; attempting restoration anyway.");
        }

        if (installReplaced)
        {
            try
            {
                fileSystem.TryDeleteDirectory(_options.WorkerInstallDirectory);
                if (Directory.Exists(backup))
                {
                    fileSystem.CopyDirectory(backup, _options.WorkerInstallDirectory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Unable to restore the previous Worker installation from {BackupDirectory}.", backup);
            }
        }

        try
        {
            await serviceControl.StartAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unable to restart the Worker service after rollback.");
        }
    }
}
