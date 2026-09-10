using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

internal sealed class SqlServerHeartbeatRepository(SqlServerDataContext context) : IHeartbeatRepository
{
    public Task SaveHeartbeatAsync(AgentHeartbeat heartbeat, CancellationToken cancellationToken) =>
        context.SaveHeartbeatAsync(heartbeat, cancellationToken);

    public Task<List<StalePcHeartbeat>> GetStaleHeartbeatsAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken) =>
        context.GetStaleHeartbeatsAsync(cutoff, cancellationToken);

    public Task<List<StalePcHeartbeat>> ClaimNewlyStaleHeartbeatsAsync(
        DateTimeOffset cutoff,
        DateTimeOffset notifiedAtUtc,
        CancellationToken cancellationToken) =>
        context.ClaimNewlyStaleHeartbeatsAsync(cutoff, notifiedAtUtc, cancellationToken);
}
