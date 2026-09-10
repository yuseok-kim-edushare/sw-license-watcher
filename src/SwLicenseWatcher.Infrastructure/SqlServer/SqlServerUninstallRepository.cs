using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

internal sealed class SqlServerUninstallRepository(SqlServerDataContext context) : IUninstallRequestStore
{
    public Task<UninstallRequestCreatedResponse?> CreateUninstallRequestAsync(
        string deviceCode, CancellationToken cancellationToken) =>
        context.CreateUninstallRequestAsync(deviceCode, cancellationToken);

    public Task<AgentUninstallRequestResponse?> GetAgentUninstallRequestAsync(
        long id, string deviceCode, CancellationToken cancellationToken) =>
        context.GetAgentUninstallRequestAsync(id, deviceCode, cancellationToken);

    public Task<bool> ConsumeUninstallRequestAsync(
        long id, string deviceCode, string code, CancellationToken cancellationToken) =>
        context.ConsumeUninstallRequestAsync(id, deviceCode, code, cancellationToken);

    public Task<(int TotalCount, List<AdminUninstallRequest> Items)> ListUninstallRequestsAsync(
        int skip, int take, string? search, CancellationToken cancellationToken) =>
        context.ListUninstallRequestsAsync(skip, take, search, cancellationToken);

    public Task<bool> ApproveUninstallRequestAsync(long id, CancellationToken cancellationToken) =>
        context.ApproveUninstallRequestAsync(id, cancellationToken);

    public Task<bool> DenyUninstallRequestAsync(long id, CancellationToken cancellationToken) =>
        context.DenyUninstallRequestAsync(id, cancellationToken);
}
