namespace SwLicenseWatcher.Core;

public static class SoftwarePolicyClassificationNames
{
    public const string White = "white";
    public const string Managed = "managed";
    public const string Black = "black";
    public const string Unclassified = "unclassified";

    public static string ToStorage(SoftwarePolicyClassification classification) => classification switch
    {
        SoftwarePolicyClassification.Whitelist => White,
        SoftwarePolicyClassification.Managed => Managed,
        SoftwarePolicyClassification.Blacklist => Black,
        _ => throw new ArgumentOutOfRangeException(nameof(classification), classification, "Unsupported software policy classification.")
    };

    public static string ToInstalledSoftwareStorage(SoftwarePolicyClassification? classification) =>
        classification is { } value ? ToStorage(value) : Unclassified;

    public static string ToInstalledSoftwareStorage(SoftwarePolicyMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return ToInstalledSoftwareStorage(match.Classification);
    }

    public static bool TryParse(string? value, out SoftwarePolicyClassification classification)
    {
        if (value is not null)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case White:
                case "whitelist":
                    classification = SoftwarePolicyClassification.Whitelist;
                    return true;
                case Managed:
                    classification = SoftwarePolicyClassification.Managed;
                    return true;
                case Black:
                case "blacklist":
                    classification = SoftwarePolicyClassification.Blacklist;
                    return true;
            }
        }

        classification = default;
        return false;
    }

    public static bool TryParseInstalledSoftware(string? value, out string storage)
    {
        if (value is not null)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case White:
                case "whitelist":
                    storage = White;
                    return true;
                case Managed:
                    storage = Managed;
                    return true;
                case Black:
                case "blacklist":
                    storage = Black;
                    return true;
                case Unclassified:
                    storage = Unclassified;
                    return true;
            }
        }

        storage = string.Empty;
        return false;
    }
}
