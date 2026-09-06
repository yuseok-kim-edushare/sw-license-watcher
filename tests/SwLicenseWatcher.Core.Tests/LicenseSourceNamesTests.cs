using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Core.Tests;

public class LicenseSourceNamesTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" ", null)]
    [InlineData("company", "company")]
    [InlineData("COMPANY", "company")]
    [InlineData("byo", "byo")]
    [InlineData("Byo", "byo")]
    public void TryParse_accepts_company_byo_or_blank(string? value, string? expected)
    {
        Assert.True(LicenseSourceNames.TryParse(value, out var source));
        Assert.Equal(expected, source);
    }

    [Theory]
    [InlineData("personal")]
    [InlineData("home")]
    [InlineData("white")]
    public void TryParse_rejects_unknown_values(string value)
    {
        Assert.False(LicenseSourceNames.TryParse(value, out var source));
        Assert.Null(source);
    }

    [Fact]
    public void ForManagedPolicy_keeps_source_only_for_managed()
    {
        Assert.Equal("company", LicenseSourceNames.ForManagedPolicy(SoftwarePolicyClassification.Managed, "company"));
        Assert.Null(LicenseSourceNames.ForManagedPolicy(SoftwarePolicyClassification.Whitelist, "company"));
        Assert.Null(LicenseSourceNames.ForManagedPolicy(SoftwarePolicyClassification.Blacklist, "byo"));
    }

    [Fact]
    public void ResolveEffective_prefers_assignment_then_managed_policy_default()
    {
        Assert.Equal("byo", LicenseSourceNames.ResolveEffective("byo", "managed", "company"));
        Assert.Equal("company", LicenseSourceNames.ResolveEffective(null, "managed", "company"));
        Assert.Null(LicenseSourceNames.ResolveEffective(null, "managed", null));
        Assert.Null(LicenseSourceNames.ResolveEffective(null, "white", "company"));
        Assert.Equal("company", LicenseSourceNames.ResolveEffective("company", "white", "byo"));
    }
}
