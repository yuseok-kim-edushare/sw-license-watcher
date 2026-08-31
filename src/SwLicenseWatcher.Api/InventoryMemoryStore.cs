using System.Threading;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

public sealed class InventoryMemoryStore
{
    private int _snapshotCount;
    private int _heartbeatCount;

    public int SnapshotCount => Volatile.Read(ref _snapshotCount);
    public int HeartbeatCount => Volatile.Read(ref _heartbeatCount);

    public void RecordSnapshot(InventoryIngestionRequest snapshot) => Interlocked.Increment(ref _snapshotCount);

    public void RecordHeartbeat(AgentHeartbeat heartbeat) => Interlocked.Increment(ref _heartbeatCount);
}
