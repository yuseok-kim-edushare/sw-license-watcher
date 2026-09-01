using System.Globalization;
using System.Text;

namespace SwLicenseWatcher.Core;

public static class SqlScriptBatchSplitter
{
    public static IReadOnlyList<string> Split(string? script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return [];
        }

        var batches = new List<string>();
        var current = new StringBuilder();
        using var reader = new StringReader(script);
        while (reader.ReadLine() is { } line)
        {
            if (TryParseGo(line, out var repeatCount))
            {
                Flush(current, batches, repeatCount);
                continue;
            }

            current.AppendLine(line);
        }

        Flush(current, batches, repeatCount: 1);
        return batches;
    }

    private static void Flush(StringBuilder current, List<string> batches, int repeatCount)
    {
        var batch = current.ToString().Trim();
        current.Clear();
        if (batch.Length == 0)
        {
            return;
        }

        for (var i = 0; i < repeatCount; i++)
        {
            batches.Add(batch);
        }
    }

    private static bool TryParseGo(string line, out int repeatCount)
    {
        repeatCount = 1;
        var trimmed = line.Trim();
        if (trimmed.Length < 2 ||
            !trimmed.StartsWith("GO", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (trimmed.Length == 2)
        {
            return true;
        }

        if (!char.IsWhiteSpace(trimmed[2]))
        {
            return false;
        }

        var rest = trimmed[2..].Trim();
        if (rest.Length == 0)
        {
            return true;
        }

        if (int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count > 0)
        {
            repeatCount = count;
            return true;
        }

        return false;
    }
}
