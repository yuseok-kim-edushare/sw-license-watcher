using System.Collections.Concurrent;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

public sealed class InventoryMemoryStore
{
    private readonly ConcurrentQueue<InventoryIngestionRequest> _snapshots = new();
    private readonly ConcurrentQueue<AgentHeartbeat> _heartbeats = new();

    public int SnapshotCount => _snapshots.Count;
    public int HeartbeatCount => _heartbeats.Count;

    public void RecordSnapshot(InventoryIngestionRequest snapshot) => _snapshots.Enqueue(snapshot);

    public void RecordHeartbeat(AgentHeartbeat heartbeat) => _heartbeats.Enqueue(heartbeat);
}
