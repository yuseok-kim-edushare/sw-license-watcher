using System.Text;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Worker;

public readonly record struct SseEvent(string Name, string Data);

public static class SseEventReader
{
    public static async IAsyncEnumerable<SseEvent> ReadAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        var name = "message";
        var data = new StringBuilder();
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                yield break;
            }

            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    yield return new SseEvent(name, data.ToString());
                }

                name = "message";
                data.Clear();
                continue;
            }

            if (line.StartsWith(':'))
            {
                continue;
            }

            var colon = line.IndexOf(':');
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? string.Empty : line[(colon + 1)..].TrimStart(' ');
            if (field == "event")
            {
                name = value;
            }
            else if (field == "data")
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(value);
            }
        }
    }

    public static bool TryParseUserMessage(SseEvent sse, out AgentUserMessageCommand? command)
    {
        command = null;
        if (!string.Equals(sse.Name, "user-message", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(sse.Data))
        {
            return false;
        }

        return AgentAssignmentStore.TryReadUserMessageCommandObject(sse.Data, out command);
    }
}
