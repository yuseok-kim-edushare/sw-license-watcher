using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

public interface IStaleHeartbeatNotificationStore
{
    Task<List<StalePcHeartbeat>> ClaimNewlyStaleHeartbeatsAsync(
        DateTimeOffset cutoff,
        DateTimeOffset notifiedAtUtc,
        CancellationToken cancellationToken);
}
