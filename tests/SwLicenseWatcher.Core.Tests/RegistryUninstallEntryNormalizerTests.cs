using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Core.Tests;

public class RegistryUninstallEntryNormalizerTests
{
    [Fact]
    public void TryCreateEntry_returns_null_when_display_name_is_missing()
    {
        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Assert.Null(RegistrySoftwareInventoryCollector.TryCreateEntry(null, "1.0", "Pub", @"C:\App", "Machine", dedupe));
        Assert.Null(RegistrySoftwareInventoryCollector.TryCreateEntry("  ", "1.0", "Pub", @"C:\App", "Machine", dedupe));
        Assert.Empty(dedupe);
    }

    [Fact]
    public void TryCreateEntry_normalizes_blank_optional_fields_to_null()
    {
        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var entry = RegistrySoftwareInventoryCollector.TryCreateEntry("Widget", " ", "", null, "Machine", dedupe);

        Assert.NotNull(entry);
        Assert.Equal("Widget", entry.Name);
        Assert.Null(entry.Version);
        Assert.Null(entry.Publisher);
        Assert.Null(entry.InstallLocation);
        Assert.Equal("Machine", entry.DiscoveryScope);
        Assert.Equal("Registry.Uninstall", entry.DiscoverySource);
    }

    [Fact]
    public void TryCreateEntry_dedupes_by_raw_values_and_scope()
    {
        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var first = RegistrySoftwareInventoryCollector.TryCreateEntry("Widget", "1.0", "Acme", @"C:\App", "Machine", dedupe);
        var duplicate = RegistrySoftwareInventoryCollector.TryCreateEntry("Widget", "1.0", "Acme", @"C:\App", "Machine", dedupe);
        var otherScope = RegistrySoftwareInventoryCollector.TryCreateEntry("Widget", "1.0", "Acme", @"C:\App", "User", dedupe);

        Assert.NotNull(first);
        Assert.Null(duplicate);
        Assert.NotNull(otherScope);
        Assert.Equal("User", otherScope.DiscoveryScope);
    }

    [Fact]
    public void TryCreateEntry_treats_whitespace_version_as_distinct_from_null_for_dedupe()
    {
        var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var blankVersion = RegistrySoftwareInventoryCollector.TryCreateEntry("Widget", "  ", "Acme", @"C:\App", "Machine", dedupe);
        var missingVersion = RegistrySoftwareInventoryCollector.TryCreateEntry("Widget", null, "Acme", @"C:\App", "Machine", dedupe);

        Assert.NotNull(blankVersion);
        Assert.NotNull(missingVersion);
        Assert.Null(blankVersion.Version);
        Assert.Null(missingVersion.Version);
    }

    [Theory]
    [InlineData("S-1-5-21-1-2-3-1000_Classes", "S-1-5-21-1-2-3-1001", true)]
    [InlineData("S-1-5-21-1-2-3-1000_classes", "S-1-5-21-1-2-3-1001", true)]
    [InlineData("S-1-5-21-1-2-3-1000", "S-1-5-21-1-2-3-1000", true)]
    [InlineData("S-1-5-21-1-2-3-1000", "s-1-5-21-1-2-3-1000", true)]
    [InlineData("S-1-5-21-1-2-3-1000", "S-1-5-21-1-2-3-1001", false)]
    [InlineData("S-1-5-21-1-2-3-1000", null, false)]
    public void ShouldIgnoreLoadedUserSid_skips_classes_suffix_and_current_user(string sid, string? currentSid, bool expected)
    {
        Assert.Equal(expected, RegistrySoftwareInventoryCollector.ShouldIgnoreLoadedUserSid(sid, currentSid));
    }
}
