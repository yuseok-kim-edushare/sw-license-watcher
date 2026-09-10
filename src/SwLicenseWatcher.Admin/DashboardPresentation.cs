using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Admin;

internal static class DashboardPresentation
{
    internal static readonly string[] SoftwareClasses =
    [
        "",
        SoftwarePolicyClassificationNames.White,
        SoftwarePolicyClassificationNames.Managed,
        SoftwarePolicyClassificationNames.Black,
        SoftwarePolicyClassificationNames.Unclassified
    ];

    internal static readonly string[] PolicyClasses =
    [
        "",
        SoftwarePolicyClassificationNames.White,
        SoftwarePolicyClassificationNames.Managed,
        SoftwarePolicyClassificationNames.Black
    ];

    internal static string Dash(object? value) =>
        value is null || value is "" ? "-" : Convert.ToString(value) ?? "-";

    internal static string FormatTime(DateTimeOffset? value) =>
        value is { } time ? time.ToLocalTime().ToString("g") : "-";

    internal static string LicenseLabel(string? value)
    {
        return value switch
        {
            LicenseSourceNames.Company => "회사",
            LicenseSourceNames.Byo => "BYO",
            _ => "-"
        };
    }

    internal static string ClassificationStorage(SoftwarePolicyClassification classification)
    {
        try
        {
            return SoftwarePolicyClassificationNames.ToStorage(classification);
        }
        catch (ArgumentOutOfRangeException)
        {
            return SoftwarePolicyClassificationNames.Managed;
        }
    }

    internal static string SoftwareKey(SoftwareAggregate row) =>
        $"{row.Name}\u001f{row.Version}\u001f{row.Classification}";

    internal static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
