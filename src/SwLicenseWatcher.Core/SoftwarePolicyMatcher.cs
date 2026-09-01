namespace SwLicenseWatcher.Core;

public static class SoftwarePolicyMatcher
{
    public static SoftwarePolicyMatch Match(InstalledSoftwareEntry software, IReadOnlyList<SoftwarePolicyEntry> policies)
    {
        ArgumentNullException.ThrowIfNull(software);
        ArgumentNullException.ThrowIfNull(policies);

        SoftwarePolicyEntry? chosen = null;
        var chosenRank = int.MaxValue;
        var chosenSpecificity = int.MinValue;

        foreach (var policy in policies)
        {
            if (!policy.Enabled || !Matches(software, policy))
            {
                continue;
            }

            var rank = Rank(policy.Classification);
            var specificity = Specificity(policy);
            if (chosen is not null && (rank > chosenRank || (rank == chosenRank && specificity <= chosenSpecificity)))
            {
                continue;
            }

            chosen = policy;
            chosenRank = rank;
            chosenSpecificity = specificity;
        }

        return new SoftwarePolicyMatch(software, chosen);
    }

    public static IReadOnlyList<SoftwarePolicyMatch> MatchAll(
        IEnumerable<InstalledSoftwareEntry> software,
        IReadOnlyList<SoftwarePolicyEntry> policies)
    {
        ArgumentNullException.ThrowIfNull(software);
        ArgumentNullException.ThrowIfNull(policies);

        var matches = new List<SoftwarePolicyMatch>();
        foreach (var entry in software)
        {
            matches.Add(Match(entry, policies));
        }

        return matches;
    }

    internal static bool Matches(InstalledSoftwareEntry software, SoftwarePolicyEntry policy)
    {
        if (!MatchesPattern(software.Name, policy.ProductName))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(policy.Publisher) &&
            !MatchesPattern(software.Publisher ?? string.Empty, policy.Publisher))
        {
            return false;
        }

        return MatchesVersion(software.Version, policy.VersionPattern);
    }

    internal static bool MatchesPattern(string value, string pattern)
    {
        if (pattern.IndexOfAny(['*', '?']) < 0)
        {
            return value.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        }

        return MatchGlob(value, pattern);
    }

    internal static bool MatchesVersion(string? installedVersion, string? versionPattern)
    {
        if (string.IsNullOrWhiteSpace(versionPattern))
        {
            return true;
        }

        var conditions = versionPattern.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (conditions.Length == 0)
        {
            return true;
        }

        foreach (var condition in conditions)
        {
            if (!MatchesVersionCondition(installedVersion, condition))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesVersionCondition(string? installedVersion, string condition)
    {
        if (condition == "*")
        {
            return true;
        }

        if (TryParseComparison(condition, out var comparison, out var expected))
        {
            if (string.IsNullOrWhiteSpace(installedVersion))
            {
                return false;
            }

            var result = CompareVersions(installedVersion, expected);
            return comparison switch
            {
                ">=" => result >= 0,
                "<=" => result <= 0,
                ">" => result > 0,
                "<" => result < 0,
                "=" => result == 0,
                _ => false
            };
        }

        return !string.IsNullOrWhiteSpace(installedVersion) && MatchesPattern(installedVersion, condition);
    }

    private static bool TryParseComparison(string condition, out string comparison, out string version)
    {
        if (condition.StartsWith(">=", StringComparison.Ordinal))
        {
            comparison = ">=";
            version = condition[2..].Trim();
            return version.Length > 0;
        }

        if (condition.StartsWith("<=", StringComparison.Ordinal))
        {
            comparison = "<=";
            version = condition[2..].Trim();
            return version.Length > 0;
        }

        if (condition.StartsWith('>') || condition.StartsWith('<') || condition.StartsWith('='))
        {
            comparison = condition[..1];
            version = condition[1..].Trim();
            return version.Length > 0;
        }

        comparison = string.Empty;
        version = string.Empty;
        return false;
    }

    internal static int CompareVersions(string left, string right)
    {
        var leftParts = ParseVersionParts(left);
        var rightParts = ParseVersionParts(right);
        var length = Math.Max(leftParts.Count, rightParts.Count);
        for (var i = 0; i < length; i++)
        {
            var leftPart = i < leftParts.Count ? leftParts[i] : 0;
            var rightPart = i < rightParts.Count ? rightParts[i] : 0;
            var comparison = leftPart.CompareTo(rightPart);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static List<long> ParseVersionParts(string version)
    {
        var parts = new List<long>();
        foreach (var token in version.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var start = 0;
            while (start < token.Length && !char.IsAsciiDigit(token[start]))
            {
                start++;
            }

            var end = start;
            while (end < token.Length && char.IsAsciiDigit(token[end]))
            {
                end++;
            }

            parts.Add(start < end && long.TryParse(token[start..end], out var number) ? number : 0);
        }

        return parts;
    }

    private static int Rank(SoftwarePolicyClassification classification) => classification switch
    {
        SoftwarePolicyClassification.Blacklist => 0,
        SoftwarePolicyClassification.Managed => 1,
        SoftwarePolicyClassification.Whitelist => 2,
        _ => 3
    };

    private static int Specificity(SoftwarePolicyEntry policy)
    {
        var score = 0;
        if (policy.ProductName.IndexOfAny(['*', '?']) < 0)
        {
            score += 4;
        }
        else
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(policy.Publisher))
        {
            score += 2;
        }

        if (!string.IsNullOrWhiteSpace(policy.VersionPattern))
        {
            score += 1;
        }

        score += Math.Min(policy.ProductName.Length, 64);
        return score;
    }

    private static bool MatchGlob(string value, string pattern)
    {
        var valueIndex = 0;
        var patternIndex = 0;
        var starIndex = -1;
        var matchIndex = 0;

        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length &&
                (pattern[patternIndex] == '?' ||
                 (pattern[patternIndex] != '*' && CharsEqual(pattern[patternIndex], value[valueIndex]))))
            {
                valueIndex++;
                patternIndex++;
                continue;
            }

            if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex;
                matchIndex = valueIndex;
                patternIndex++;
                continue;
            }

            if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                matchIndex++;
                valueIndex = matchIndex;
                continue;
            }

            return false;
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    private static bool CharsEqual(char left, char right) =>
        char.ToUpperInvariant(left) == char.ToUpperInvariant(right);
}
