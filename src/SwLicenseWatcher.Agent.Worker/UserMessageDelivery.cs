using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Worker;

public sealed class UserMessageDelivery(
    AgentApiClient api,
    AgentAssignmentStore assignmentStore,
    IOptions<WorkerAgentOptions> options,
    UserToastLauncher launcher,
    ILogger<UserMessageDelivery> logger)
{
    private readonly ConcurrentDictionary<long, byte> _seen = new();

    public Task DeliverAsync(AgentPublishOutcome outcome, CancellationToken cancellationToken)
    {
        if (outcome.Result != AgentPublishResult.Succeeded || outcome.UserMessageCommand is null)
        {
            return Task.CompletedTask;
        }

        return DeliverAsync(outcome.UserMessageCommand, cancellationToken);
    }

    public async Task DeliverAsync(AgentUserMessageCommand command, CancellationToken cancellationToken)
    {
        if (!_seen.TryAdd(command.Id, 0))
        {
            return;
        }

        var shown = await launcher.ShowAsync(command, cancellationToken);
        if (!shown)
        {
            _seen.TryRemove(command.Id, out _);
            return;
        }

        var code = assignmentStore.ResolveDeviceCode(options.Value.DeviceCode);
        if (!await api.ConsumeUserMessageAsync(command.Id, code, cancellationToken))
        {
            logger.LogWarning("Shown user message {Id} but consume failed; it may be retried.", command.Id);
        }
    }
}
