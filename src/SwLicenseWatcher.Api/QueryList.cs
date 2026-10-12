using System.Globalization;

namespace SwLicenseWatcher.Api;

internal static class QueryList
{
    internal const int DefaultTake = 100;
    internal const int CsvDefaultTake = 10_000;
    internal const int MaxTake = 10_000;
    internal const int MaxSearchLength = 256;

    internal static bool WantsCsv(string? format) =>
        string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);

    internal static (int Skip, int Take) NormalizePaging(int? skip, int? take, bool csv)
    {
        var normalizedSkip = Math.Max(skip.GetValueOrDefault(), 0);
        var requestedTake = take ?? (csv ? CsvDefaultTake : DefaultTake);
        var normalizedTake = Math.Clamp(requestedTake, 1, MaxTake);
        return (normalizedSkip, normalizedTake);
    }

    internal static bool TryValidateSearch(string? search, out string error)
    {
        if (search is { Length: > MaxSearchLength })
        {
            error = $"search must be at most {MaxSearchLength} characters.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    internal static bool TryParseSince(string? since, out DateTimeOffset? value, out string error)
    {
        if (string.IsNullOrWhiteSpace(since))
        {
            value = null;
            error = string.Empty;
            return true;
        }

        if (!DateTimeOffset.TryParse(
                since.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            value = null;
            error = "since must be an ISO 8601 date/time.";
            return false;
        }

        value = parsed;
        error = string.Empty;
        return true;
    }

    internal static bool TryNormalizeSort(
        string? sort,
        string? direction,
        IReadOnlySet<string> allowedColumns,
        out string? column,
        out bool descending,
        out string error)
    {
        column = null;
        descending = false;
        if (string.IsNullOrWhiteSpace(sort))
        {
            if (!string.IsNullOrWhiteSpace(direction))
            {
                error = "dir requires sort.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        var normalized = sort.Trim();
        if (!allowedColumns.Contains(normalized))
        {
            error = "sort is not a supported column.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(direction) || direction.Trim().Equals("asc", StringComparison.OrdinalIgnoreCase))
        {
            column = normalized;
            error = string.Empty;
            return true;
        }

        if (direction.Trim().Equals("desc", StringComparison.OrdinalIgnoreCase))
        {
            column = normalized;
            descending = true;
            error = string.Empty;
            return true;
        }

        error = "dir must be asc or desc.";
        return false;
    }
}
