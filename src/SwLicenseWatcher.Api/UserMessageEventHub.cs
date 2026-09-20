using System.Collections.Concurrent;
using System.Threading.Channels;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal sealed class UserMessageEventHub
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<AgentUserMessageCommand>>> _subscribers =
        new(StringComparer.OrdinalIgnoreCase);

    public async IAsyncEnumerable<AgentUserMessageCommand> Subscribe(
        string deviceCode,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<AgentUserMessageCommand>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        var bucket = _subscribers.GetOrAdd(deviceCode, _ => new ConcurrentDictionary<Guid, Channel<AgentUserMessageCommand>>());
        bucket[id] = channel;
        try
        {
            await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return message;
            }
        }
        finally
        {
            bucket.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }

    public void Publish(IEnumerable<string> deviceAliases, AgentUserMessageCommand message)
    {
        foreach (var alias in deviceAliases)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }

            if (!_subscribers.TryGetValue(alias.Trim(), out var bucket))
            {
                continue;
            }

            foreach (var channel in bucket.Values)
            {
                channel.Writer.TryWrite(message);
            }
        }
    }
}
