using System.Text;

namespace SwLicenseWatcher.Api;

internal static class InventoryCsv
{
    public static IResult File(string downloadName, string[] headers, IEnumerable<string?[]> rows)
    {
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true))
        {
            writer.NewLine = "\r\n";
            writer.WriteLine(string.Join(',', headers.Select(Escape)));
            foreach (var row in rows)
            {
                writer.WriteLine(string.Join(',', row.Select(Escape)));
            }
        }

        return Results.File(stream.ToArray(), "text/csv", downloadName);
    }

    public static string? Format(DateTimeOffset? value) => value?.ToString("o");

    public static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
        }

        var text = builder.ToString().Trim();
        return string.IsNullOrEmpty(text) ? "export" : text;
    }

    internal static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t')
        {
            text = "'" + text;
        }

        if (text.AsSpan().IndexOfAny(['"', ',', '\r', '\n']) >= 0)
        {
            return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return text;
    }
}
