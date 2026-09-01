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
    }

    public async Task FlushAsync(AgentApiClient apiClient, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_queueDirectory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(_queueDirectory, "*.snapshot").Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var protectedPayload = await File.ReadAllTextAsync(path, cancellationToken);
                var json = protector.Unprotect(protectedPayload);
                var snapshot = JsonSerializer.Deserialize(json, InventoryJsonSerializerContext.Default.InventoryIngestionRequest)
                    ?? throw new JsonException("Queued snapshot was empty.");
                if (!await apiClient.PublishSnapshotAsync(snapshot, cancellationToken))
                {
                    return;
                }

                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or JsonException or CryptographicException)
            {
                logger.LogError(ex, "Unable to process queued snapshot {SnapshotFile}; moving it aside.", Path.GetFileName(path));
                File.Move(path, path + ".invalid", true);
            }
        }
    }
}
