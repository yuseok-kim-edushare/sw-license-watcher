namespace SwLicenseWatcher.Core;

public static class ReleaseChecksumParser
{
    public static bool TryGetSha256(string sumsText, string fileName, out string sha256)
    {
        sha256 = string.Empty;
        if (string.IsNullOrWhiteSpace(sumsText) || string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        using var reader = new StringReader(sumsText);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length < 67)
            {
                continue;
            }

            var digest = trimmed[..64];
            if (!digest.All(Uri.IsHexDigit))
            {
                continue;
            }

            var remainder = trimmed[64..].TrimStart();
            if (remainder.StartsWith('*'))
            {
                remainder = remainder[1..].TrimStart();
            }

            if (string.Equals(remainder, fileName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(remainder.Replace('\\', '/')), fileName, StringComparison.OrdinalIgnoreCase))
            {
                sha256 = digest.ToLowerInvariant();
                return true;
            }
        }

        return false;
    }
}
