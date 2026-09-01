using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Worker;

public sealed class LocalSnapshotQueue(
    LocalStateStoreOptions options,
    ILocalStateProtector protector,
    ILogger<LocalSnapshotQueue> logger)
{
    private readonly string _queueDirectory = options.QueueDirectory;

    public async Task EnqueueAsync(InventoryIngestionRequest snapshot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_queueDirectory);
        var json = JsonSerializer.Serialize(snapshot, InventoryJsonSerializerContext.Default.InventoryIngestionRequest);
        var protectedPayload = protector.Protect(json);
        var finalPath = Path.Combine(_queueDirectory, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfffffff}-{Guid.NewGuid():N}.snapshot");
        var temporaryPath = finalPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, protectedPayload, Encoding.UTF8, cancellationToken);
        File.Move(temporaryPath, finalPath);
        logger.LogInformation("Queued inventory snapshot for later delivery.");
        EvictSnapshotsBeyondQuota(finalPath);
    }

    public async Task<bool> FlushAsync(AgentApiClient apiClient, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_queueDirectory))
        {
            return true;
        }

        string[] queuedPaths;
        try
        {
            queuedPaths = Directory.EnumerateFiles(_queueDirectory, "*.snapshot").Order(StringComparer.Ordinal).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Unable to enumerate the snapshot queue {QueueDirectory}.", _queueDirectory);
            return false;
        }

        foreach (var path in queuedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var protectedPayload = await File.ReadAllTextAsync(path, cancellationToken);
                var json = protector.Unprotect(protectedPayload);
                var snapshot = ReadValidatedSnapshot(json);
                if (!await apiClient.PublishSnapshotAsync(snapshot, cancellationToken))
                {
                    return false;
                }

                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or JsonException or CryptographicException)
            {
                logger.LogError(ex, "Unable to process queued snapshot {SnapshotFile}; moving it aside.", Path.GetFileName(path));
                try
                {
                    File.Move(path, path + ".invalid", true);
                }
                catch (Exception moveException) when (moveException is IOException or UnauthorizedAccessException)
                {
                    logger.LogError(moveException, "Unable to quarantine queued snapshot {SnapshotFile}.", Path.GetFileName(path));
                }
            }
        }

        return true;
    }

    private static InventoryIngestionRequest ReadValidatedSnapshot(string json)
    {
        var snapshot = JsonSerializer.Deserialize(json, InventoryJsonSerializerContext.Default.InventoryIngestionRequest)
            ?? throw new JsonException("Queued snapshot was empty.");
        if (snapshot.Pc is null ||
            string.IsNullOrWhiteSpace(snapshot.Pc.DeviceCode) ||
            string.IsNullOrWhiteSpace(snapshot.Pc.HostName) ||
            snapshot.Pc.DomainName is null ||
            snapshot.Pc.OperatingSystem is null ||
            string.IsNullOrWhiteSpace(snapshot.Pc.AgentVersion) ||
            snapshot.CollectedAtUtc == default)
        {
            throw new JsonException("Queued snapshot is missing required identity fields.");
        }

        if (snapshot.InstalledSoftware is null)
        {
            throw new JsonException("Queued snapshot is missing the installed software collection.");
        }

        foreach (var entry in snapshot.InstalledSoftware)
        {
            if (entry is null ||
                string.IsNullOrWhiteSpace(entry.Name) ||
                string.IsNullOrWhiteSpace(entry.DiscoveryScope) ||
                string.IsNullOrWhiteSpace(entry.DiscoverySource))
            {
                throw new JsonException("Queued snapshot contains an invalid installed software entry.");
            }
        }

        return snapshot;
    }

    private void EvictSnapshotsBeyondQuota(string newestPath)
    {
        List<FileInfo> queued;
        try
        {
            queued = new DirectoryInfo(_queueDirectory)
                .EnumerateFiles("*.snapshot")
                .OrderBy(file => file.FullName, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Unable to enforce the snapshot queue quota in {QueueDirectory}.", _queueDirectory);
            return;
        }

        var totalBytes = queued.Sum(file => file.Length);
        var index = 0;
        while (index < queued.Count &&
               queued.Count > 1 &&
               (queued.Count > options.MaxQueuedSnapshots || totalBytes > options.MaxQueueBytes))
        {
            var file = queued[index];
            if (string.Equals(file.FullName, newestPath, StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            var length = file.Length;
            try
            {
                file.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Unable to evict queued snapshot {SnapshotFile}.", file.Name);
                return;
            }

            totalBytes -= length;
            queued.RemoveAt(index);
            logger.LogWarning(
                "Evicted queued snapshot {SnapshotFile} because the local queue quota was exceeded.",
                file.Name);
        }
    }
}
