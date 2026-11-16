using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Core.Tests;

public class RegistryUninstallEntryNormalizerTests
{
    [Fact]
    public void TryCreateEntry_returns_null_when_display_name_is_missing()
    {
        var (dedupe, software) = Inventory();

        Assert.Null(RegistrySoftwareInventoryCollector.TryCreateEntry(null, "1.0", "Pub", @"C:\App", "Machine", dedupe, software));
        Assert.Null(RegistrySoftwareInventoryCollector.TryCreateEntry("  ", "1.0", "Pub", @"C:\App", "Machine", dedupe, software));
        Assert.Empty(dedupe);
        Assert.Empty(software);
    }

    [Fact]
    public void TryCreateEntry_normalizes_blank_optional_fields_to_null()
    {
        var (dedupe, software) = Inventory();

        var entry = RegistrySoftwareInventoryCollector.TryCreateEntry("Widget", " ", "", null, "Machine", dedupe, software);

        Assert.NotNull(entry);
        Assert.Equal("Widget", entry.Name);
        Assert.Null(entry.Version);
        Assert.Null(entry.Publisher);
        Assert.Null(entry.InstallLocation);
        Assert.Null(entry.InstalledAtUtc);
        Assert.Equal("Machine", entry.DiscoveryScope);
        Assert.Equal("Registry.Uninstall", entry.DiscoverySource);
    }

    [Fact]
    public void TryCreateEntry_dedupes_by_raw_values_and_scope()
    {
        var (dedupe, software) = Inventory();
        var first = RegistrySoftwareInventoryCollector.TryCreateEntry("Widget", "1.0", "Acme", @"C:\App", "Machine", dedupe, software);
        var duplicate = RegistrySoftwareInventoryCollector.TryCreateEntry("Widget", "1.0", "Acme", @"C:\App", "Machine", dedupe, software);
        var otherScope = RegistrySoftwareInventoryCollector.TryCreateEntry("Widget", "1.0", "Acme", @"C:\App", "User", dedupe, software);

        Assert.NotNull(first);
        Assert.Null(duplicate);
        Assert.NotNull(otherScope);
        Assert.Equal("User", otherScope.DiscoveryScope);
        Assert.Equal(2, software.Count);
    }

    [Fact]
    public void TryCreateEntry_treats_whitespace_version_as_distinct_from_null_for_dedupe()
    {
        var (dedupe, software) = Inventory();

        var blankVersion = RegistrySoftwareInventoryCollector.TryCreateEntry("Widget", "  ", "Acme", @"C:\App", "Machine", dedupe, software);
        var missingVersion = RegistrySoftwareInventoryCollector.TryCreateEntry("Widget", null, "Acme", @"C:\App", "Machine", dedupe, software);

        Assert.NotNull(blankVersion);
        Assert.NotNull(missingVersion);
        Assert.Null(blankVersion.Version);
        Assert.Null(missingVersion.Version);
    }

    [Fact]
    public void TryCreateEntry_fills_a_missing_install_date_from_a_duplicate()
    {
        var (dedupe, software) = Inventory();
        var installedAt = new DateTimeOffset(2024, 3, 15, 0, 0, 0, TimeSpan.Zero);

        var first = RegistrySoftwareInventoryCollector.TryCreateEntry("Widget", "1.0", "Acme", @"C:\App", "Machine", dedupe, software);
        var duplicate = RegistrySoftwareInventoryCollector.TryCreateEntry(
            "Widget", "1.0", "Acme", @"C:\App", "Machine", dedupe, software, installedAt);

        Assert.NotNull(first);
        Assert.Null(first.InstalledAtUtc);
        Assert.Null(duplicate);
        Assert.Equal(installedAt, Assert.Single(software).InstalledAtUtc);
    }

    [Fact]
    public void TryCreateEntry_keeps_the_first_confirmed_install_date()
    {
        var (dedupe, software) = Inventory();
        var installedAt = new DateTimeOffset(2024, 3, 15, 0, 0, 0, TimeSpan.Zero);
        var later = new DateTimeOffset(2024, 4, 1, 0, 0, 0, TimeSpan.Zero);

        RegistrySoftwareInventoryCollector.TryCreateEntry(
            "Widget", "1.0", "Acme", @"C:\App", "Machine", dedupe, software, installedAt);
        RegistrySoftwareInventoryCollector.TryCreateEntry(
            "Widget", "1.0", "Acme", @"C:\App", "Machine", dedupe, software, later);

        Assert.Equal(installedAt, Assert.Single(software).InstalledAtUtc);
    }

    [Theory]
    [InlineData("20240315", "2024-03-15T00:00:00+00:00")]
    [InlineData(" 2024-03-15 ", "2024-03-15T00:00:00+00:00")]
    [InlineData("2024/03/15", "2024-03-15T00:00:00+00:00")]
    [InlineData("2024.03.15", "2024-03-15T00:00:00+00:00")]
    [InlineData("20240315143005", "2024-03-15T14:30:05+00:00")]
    [InlineData("2024-03-15 14:30:05", "2024-03-15T14:30:05+00:00")]
    [InlineData("2024-03-15T14:30:05Z", "2024-03-15T14:30:05+00:00")]
    public void TryParseInstallDate_reads_confirmed_text(string value, string expected)
    {
        var parsed = RegistrySoftwareInventoryCollector.TryParseInstallDate(value);

        Assert.NotNull(parsed);
        Assert.Equal(DateTimeOffset.Parse(expected), parsed);
    }

    [Theory]
    [InlineData(20240315)]
    [InlineData(1710460800)]
    public void TryParseInstallDate_reads_confirmed_numbers(int value)
    {
        var parsed = RegistrySoftwareInventoryCollector.TryParseInstallDate(value);

        Assert.Equal(new DateTimeOffset(2024, 3, 15, 0, 0, 0, TimeSpan.Zero), parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("20240231")]
    [InlineData("not-a-date")]
    [InlineData("2024-3-15")]
    [InlineData("19700101")]
    public void TryParseInstallDate_returns_null_when_the_value_is_not_a_confirmed_date(string? value)
    {
        Assert.Null(RegistrySoftwareInventoryCollector.TryParseInstallDate(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20241340)]
    [InlineData(-1)]
    public void TryParseInstallDate_returns_null_for_unusable_numbers(int value)
    {
        Assert.Null(RegistrySoftwareInventoryCollector.TryParseInstallDate(value));
    }

    private static (Dictionary<string, int> Dedupe, List<InstalledSoftwareEntry> Software) Inventory() =>
        (new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), []);

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
