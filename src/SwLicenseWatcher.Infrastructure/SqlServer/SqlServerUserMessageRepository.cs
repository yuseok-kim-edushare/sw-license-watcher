using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

internal sealed class SqlServerUserMessageRepository(SqlServerDataContext context) : IUserMessageStore
{
    public Task<EnqueuedUserMessage?> EnqueueAsync(
        string deviceCode, string title, string body, CancellationToken cancellationToken) =>
        context.EnqueueUserMessageAsync(deviceCode, title, body, cancellationToken);

    public Task<IReadOnlyList<EnqueuedUserMessage>> BroadcastAsync(
        string title, string body, CancellationToken cancellationToken) =>
        context.BroadcastUserMessagesAsync(title, body, cancellationToken);

    public async Task<IReadOnlyList<AgentUserMessageCommand>> ListPendingAsync(
        string deviceCode, int take, CancellationToken cancellationToken) =>
        await context.ListPendingUserMessagesAsync(deviceCode, take, cancellationToken);

    public Task<AgentUserMessageCommand?> GetOldestPendingAsync(
        string deviceCode, CancellationToken cancellationToken) =>
        context.GetOldestPendingUserMessageAsync(deviceCode, cancellationToken);

    public Task<bool> ConsumeAsync(long id, string deviceCode, CancellationToken cancellationToken) =>
        context.ConsumeUserMessageAsync(id, deviceCode, cancellationToken);
}
