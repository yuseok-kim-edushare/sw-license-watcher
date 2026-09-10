using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Application.Tests;

public class InventoryDecisionsTests
{
    [Fact]
    public void ApplyLicenseSources_uses_device_override_before_managed_policy_default()
    {
        var installed = new[] { Software("Contoso Editor"), Software("Contoso Viewer") };
        var assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CONTOSO EDITOR"] = LicenseSourceNames.Byo
        };

        var result = InventoryDecisions.ApplyLicenseSources(
            installed,
            assignments,
            [Policy("Contoso *", SoftwarePolicyClassification.Managed, LicenseSourceNames.Company)]);

        Assert.Equal(("byo", "byo"), (result[0].LicenseSource, result[0].LicenseSourceOverride));
        Assert.Equal(("company", null), (result[1].LicenseSource, result[1].LicenseSourceOverride));
    }

    [Fact]
    public void ApplySoftwareDeviceLicenseSources_normalizes_override_and_resolves_default()
    {
        var items = new List<SoftwareDevice>
        {
            Device("PC-01", "managed", " BYO "),
            Device("PC-02", "managed", null),
            Device("PC-03", "black", "invalid")
        };

        var result = InventoryDecisions.ApplySoftwareDeviceLicenseSources(
            "Contoso Editor",
            items,
            [Policy("Contoso Editor", SoftwarePolicyClassification.Managed, LicenseSourceNames.Company)]);

        Assert.Equal(("byo", "byo"), (result[0].LicenseSource, result[0].LicenseSourceOverride));
        Assert.Equal(("company", null), (result[1].LicenseSource, result[1].LicenseSourceOverride));
        Assert.Equal((null, null), (result[2].LicenseSource, result[2].LicenseSourceOverride));
    }

    [Fact]
    public void Violation_detection_is_case_insensitive_and_reports_only_new_names()
    {
        var current = InventoryDecisions.CollectCurrentViolations(
        [
            new SoftwarePolicyMatch(Software("uTorrent"), Policy("*Torrent*", SoftwarePolicyClassification.Blacklist, null)),
            new SoftwarePolicyMatch(Software("UTORRENT"), Policy("uTorrent", SoftwarePolicyClassification.Blacklist, null)),
            new SoftwarePolicyMatch(Software("BadApp"), Policy("BadApp", SoftwarePolicyClassification.Blacklist, null))
        ]);

        Assert.Equal(2, current.Count);
        var added = InventoryDecisions.FindNewlyDetectedViolations(current, ["UTORRENT"]);
        Assert.Equal("BadApp", Assert.Single(added).Software.Name);
    }

    private static InstalledSoftwareEntry Software(string name) =>
        new(name, "1.0", "Contoso", null, "machine", "registry");

    private static SoftwareDevice Device(string code, string classification, string? sourceOverride) =>
        new(code, code, "domain", "Windows", "1.0", null, null, "1.0", "Contoso", classification, null, sourceOverride);

    private static SoftwarePolicyEntry Policy(
        string productName,
        SoftwarePolicyClassification classification,
        string? defaultLicenseSource) =>
        new(1, productName, null, null, classification, null, true, DateTimeOffset.UnixEpoch, defaultLicenseSource);
}
