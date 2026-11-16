using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.Relational;

internal readonly record struct SemanticVersionSort(string FromSuffix, string OrderBy);

internal static class SemanticVersionOrder
{
    internal static SemanticVersionSort For(
        StorageProvider provider,
        string versionColumn,
        bool descending,
        string fallback)
    {
        var direction = descending ? "DESC" : "ASC";
        var prereleaseDirection = descending ? "ASC" : "DESC";
        var trimmed = Apply(provider, $"SELECT {Trim(provider, versionColumn)} AS trimmed", "sv_trim");
        var withoutBuild = Apply(provider, Cut(provider, "sv_trim.trimmed", "+", "without_build"), "sv_build");
        var numeric = Apply(provider, Cut(provider, "sv_build.without_build", "-", "numeric_text"), "sv_numeric");
        var core = Apply(provider, StripLeadingV(provider, "sv_numeric.numeric_text"), "sv_core");
        var major = Apply(provider, Split(provider, "sv_core.core", "sv_major", "sv_rest1"), "sv_major");
        var minor = Apply(provider, Split(provider, "sv_major.sv_rest1", "sv_minor", "sv_rest2"), "sv_minor");
        var patch = Apply(provider, Split(provider, "sv_minor.sv_rest2", "sv_patch", "sv_rest3"), "sv_patch");
        var revision = Apply(provider, $"SELECT {Head(provider, "sv_patch.sv_rest3")} AS sv_revision", "sv_revision");
        var from = string.Join(
            Environment.NewLine,
            trimmed,
            withoutBuild,
            numeric,
            core,
            major,
            minor,
            patch,
            revision);
        var order = string.Join(
            ", ",
            $"{Number(provider, "sv_major.sv_major")} {direction}",
            $"{Number(provider, "sv_minor.sv_minor")} {direction}",
            $"{Number(provider, "sv_patch.sv_patch")} {direction}",
            $"{Number(provider, "sv_revision.sv_revision")} {direction}",
            $"{Prerelease(provider, "sv_build.without_build")} {prereleaseDirection}",
            $"{versionColumn} {direction}",
            fallback);
        return new SemanticVersionSort(from, order);
    }

    private static string Apply(StorageProvider provider, string selectBody, string alias) =>
        provider switch
        {
            StorageProvider.MySql or StorageProvider.PostgreSql =>
                $"CROSS JOIN LATERAL ({selectBody}) {alias}",
            _ => $"CROSS APPLY ({selectBody}) {alias}"
        };

    private static string Cut(StorageProvider provider, string source, string marker, string resultName)
    {
        var literal = Text(provider, marker);
        var haystack = Concat(provider, source, literal);
        var position = Position(provider, haystack, literal);
        var cut = $"CASE WHEN {position} > 0 THEN {Left(provider, source, $"{position} - 1")} ELSE {source} END";
        return $"SELECT {cut} AS {resultName}";
    }

    private static string StripLeadingV(StorageProvider provider, string source)
    {
        var first = Left(provider, source, "1");
        var core = $"CASE WHEN {first} = {Text(provider, "v")} OR {first} = {Text(provider, "V")} THEN {Substring(provider, source, "2")} ELSE {source} END";
        return $"SELECT {core} AS core";
    }

    private static string Split(StorageProvider provider, string source, string headName, string restName)
    {
        var dot = Text(provider, ".");
        var position = Position(provider, source, dot);
        var head = $"CASE WHEN {position} = 0 THEN {source} ELSE {Left(provider, source, $"{position} - 1")} END";
        var rest = $"CASE WHEN {position} = 0 THEN {Empty(provider)} ELSE {Substring(provider, source, $"{position} + 1")} END";
        return $"SELECT {head} AS {headName}, {rest} AS {restName}";
    }

    private static string Head(StorageProvider provider, string source)
    {
        var position = Position(provider, source, Text(provider, "."));
        return $"CASE WHEN {position} = 0 THEN {source} ELSE {Left(provider, source, $"{position} - 1")} END";
    }

    private static string Prerelease(StorageProvider provider, string withoutBuild)
    {
        var position = Position(provider, withoutBuild, Text(provider, "-"));
        return $"CASE WHEN {position} > 0 THEN 1 ELSE 0 END";
    }

    private static string Number(StorageProvider provider, string segment) => provider switch
    {
        StorageProvider.MySql =>
            $"CAST(CASE WHEN {segment} REGEXP '^[0-9]+$' THEN {segment} ELSE '0' END AS UNSIGNED)",
        StorageProvider.PostgreSql =>
            $"CASE WHEN {segment} ~ '^[0-9]+$' THEN {segment}::bigint ELSE 0 END",
        _ => $"ISNULL(TRY_CONVERT(bigint, {segment}), 0)"
    };

    private static string Trim(StorageProvider provider, string value) => provider switch
    {
        StorageProvider.SqlServer => $"LTRIM(RTRIM({value}))",
        _ => $"TRIM({value})"
    };

    private static string Position(StorageProvider provider, string haystack, string needle) => provider switch
    {
        StorageProvider.MySql => $"LOCATE({needle}, {haystack})",
        StorageProvider.PostgreSql => $"strpos({haystack}, {needle})",
        _ => $"CHARINDEX({needle}, {haystack})"
    };

    private static string Left(StorageProvider provider, string value, string length) =>
        $"LEFT({value}, {length})";

    private static string Substring(StorageProvider provider, string value, string start) => provider switch
    {
        StorageProvider.PostgreSql => $"substring({value} from {start} for 32)",
        _ => $"SUBSTRING({value}, {start}, 32)"
    };

    private static string Concat(StorageProvider provider, string left, string right) => provider switch
    {
        StorageProvider.MySql => $"CONCAT({left}, {right})",
        StorageProvider.PostgreSql => $"({left} || {right})",
        _ => $"({left} + {right})"
    };

    private static string Text(StorageProvider provider, string value) =>
        provider == StorageProvider.SqlServer ? $"N'{value}'" : $"'{value}'";

    private static string Empty(StorageProvider provider) => Text(provider, "");
}
