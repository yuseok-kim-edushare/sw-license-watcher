namespace SwLicenseWatcher.Core;

public static class LicenseSourceNames
{
    public const string Company = "company";
    public const string Byo = "byo";

    public static bool TryParse(string? value, out string? source)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            source = null;
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case Company:
                source = Company;
                return true;
            case Byo:
                source = Byo;
                return true;
            default:
                source = null;
                return false;
        }
    }

    public static string? ForManagedPolicy(SoftwarePolicyClassification classification, string? source) =>
        classification == SoftwarePolicyClassification.Managed ? source : null;

    public static string? ResolveEffective(
        string? assignment,
        string storedClassification,
        string? matchedPolicyDefault)
    {
        if (!string.IsNullOrWhiteSpace(assignment))
        {
            return assignment;
        }

        return string.Equals(storedClassification, SoftwarePolicyClassificationNames.Managed, StringComparison.Ordinal)
            ? matchedPolicyDefault
            : null;
    }
}
