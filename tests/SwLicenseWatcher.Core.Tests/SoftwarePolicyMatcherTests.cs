using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Core.Tests;

public class SoftwarePolicyMatcherTests
{
    [Fact]
    public void Match_returns_unclassified_when_no_policy_applies()
    {
        var software = Software("Google Chrome", "120.0");
        var match = SoftwarePolicyMatcher.Match(software, [Policy("Firefox", SoftwarePolicyClassification.Blacklist)]);

        Assert.True(match.IsUnclassified);
        Assert.False(match.IsBlacklisted);
        Assert.Null(match.Policy);
        Assert.Equal(SoftwarePolicyClassificationNames.Unclassified, match.StoredClassification);
    }

    [Fact]
    public void Match_uses_exact_name_match_without_wildcards()
    {
        var software = Software("Google Chrome", "120.0");
        var match = SoftwarePolicyMatcher.Match(software, [Policy("Google Chrome", SoftwarePolicyClassification.Managed)]);

        Assert.Equal(SoftwarePolicyClassification.Managed, match.Classification);
    }

    [Fact]
    public void Match_supports_prefix_and_wildcard_name_patterns()
    {
        var chrome = Software("Google Chrome", "120.0");
        var torrent = Software("uTorrent Web", "1.0");

        Assert.Equal(
            SoftwarePolicyClassification.Whitelist,
            SoftwarePolicyMatcher.Match(chrome, [Policy("Google Chrome*", SoftwarePolicyClassification.Whitelist)]).Classification);
        Assert.Equal(
            SoftwarePolicyClassification.Blacklist,
            SoftwarePolicyMatcher.Match(torrent, [Policy("*Torrent*", SoftwarePolicyClassification.Blacklist)]).Classification);
    }

    [Fact]
    public void Match_prefers_blacklist_over_whitelist()
    {
        var software = Software("uTorrent", "3.5");
        var match = SoftwarePolicyMatcher.Match(software,
        [
            Policy("uTorrent*", SoftwarePolicyClassification.Whitelist),
            Policy("uTorrent", SoftwarePolicyClassification.Blacklist)
        ]);

        Assert.True(match.IsBlacklisted);
        Assert.Equal(SoftwarePolicyClassification.Blacklist, match.Classification);
        Assert.Equal(SoftwarePolicyClassificationNames.Black, match.StoredClassification);
    }

    [Fact]
    public void Match_ignores_disabled_policies()
    {
        var software = Software("uTorrent", "3.5");
        var match = SoftwarePolicyMatcher.Match(software, [Policy("uTorrent", SoftwarePolicyClassification.Blacklist, enabled: false)]);

        Assert.True(match.IsUnclassified);
    }

    [Fact]
    public void Match_applies_optional_publisher_and_version_conditions()
    {
        var software = Software("Visual Studio", "17.8.0", "Microsoft Corporation");
        var policies = new[]
        {
            Policy("Visual Studio*", SoftwarePolicyClassification.Managed, publisher: "Microsoft*", versionPattern: ">=17.0,<18.0")
        };

        Assert.Equal(SoftwarePolicyClassification.Managed, SoftwarePolicyMatcher.Match(software, policies).Classification);
        Assert.True(SoftwarePolicyMatcher.Match(Software("Visual Studio", "16.11.0", "Microsoft Corporation"), policies).IsUnclassified);
        Assert.True(SoftwarePolicyMatcher.Match(Software("Visual Studio", "17.8.0", "Other"), policies).IsUnclassified);
    }

    [Fact]
    public void MatchAll_exposes_storage_classification_for_each_entry()
    {
        var matches = SoftwarePolicyMatcher.MatchAll(
            [Software("Google Chrome", "120.0"), Software("uTorrent", "3.5")],
            [Policy("uTorrent", SoftwarePolicyClassification.Blacklist)]);

        Assert.Equal(
            [SoftwarePolicyClassificationNames.Unclassified, SoftwarePolicyClassificationNames.Black],
            matches.Select(match => match.StoredClassification));
        Assert.Equal(
            [SoftwarePolicyClassificationNames.Unclassified, SoftwarePolicyClassificationNames.Black],
            matches.Select(SoftwarePolicyClassificationNames.ToInstalledSoftwareStorage));
    }

    [Theory]
    [InlineData("16.0", "16.0", true)]
    [InlineData("16.0.1", "16.*", true)]
    [InlineData("17.0", ">=16.0,<17.0", false)]
    [InlineData("16.11", ">=16.0,<17.0", true)]
    [InlineData(null, ">=16.0", false)]
    [InlineData("1.2.3", null, true)]
    public void MatchesVersion_supports_exact_wildcard_and_comparisons(string? installed, string? pattern, bool expected)
    {
        Assert.Equal(expected, SoftwarePolicyMatcher.MatchesVersion(installed, pattern));
    }

    [Fact]
    public void CompareVersions_orders_dotted_numeric_segments()
    {
        Assert.True(SoftwarePolicyMatcher.CompareVersions("17.8.0", "17.0") > 0);
        Assert.Equal(0, SoftwarePolicyMatcher.CompareVersions("16.0", "16.0.0"));
        Assert.True(SoftwarePolicyMatcher.CompareVersions("v1.2", "1.1") > 0);
    }

    private static InstalledSoftwareEntry Software(string name, string? version, string? publisher = null) =>
        new(name, version, publisher, null, "HKLM", "Uninstall");

    private static SoftwarePolicyEntry Policy(
        string productName,
        SoftwarePolicyClassification classification,
        string? publisher = null,
        string? versionPattern = null,
        bool enabled = true) =>
        new(1, productName, publisher, versionPattern, classification, null, enabled, DateTimeOffset.UtcNow);
}

public class SoftwarePolicyValidatorTests
{
    [Fact]
    public void TryValidate_requires_product_name_and_classification()
    {
        Assert.False(SoftwarePolicyValidator.TryValidate(null, out _));
        Assert.False(SoftwarePolicyValidator.TryValidate(new SoftwarePolicyWriteRequest("", null, null, SoftwarePolicyClassification.Blacklist, null), out _));
        Assert.False(SoftwarePolicyValidator.TryValidate(new SoftwarePolicyWriteRequest("uTorrent", null, null, null, null), out _));
        Assert.True(SoftwarePolicyValidator.TryValidate(new SoftwarePolicyWriteRequest("uTorrent", null, null, SoftwarePolicyClassification.Blacklist, null), out var error));
        Assert.Equal(string.Empty, error);
    }
}
