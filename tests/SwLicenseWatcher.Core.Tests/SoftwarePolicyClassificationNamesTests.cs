using System.Text.Json;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Core.Tests;

public class SoftwarePolicyClassificationNamesTests
{
    [Theory]
    [InlineData(SoftwarePolicyClassification.Whitelist, "white")]
    [InlineData(SoftwarePolicyClassification.Managed, "managed")]
    [InlineData(SoftwarePolicyClassification.Blacklist, "black")]
    public void ToStorage_maps_policy_classifications(SoftwarePolicyClassification classification, string expected)
    {
        Assert.Equal(expected, SoftwarePolicyClassificationNames.ToStorage(classification));
    }

    [Fact]
    public void ToInstalledSoftwareStorage_maps_null_to_unclassified()
    {
        Assert.Equal(
            SoftwarePolicyClassificationNames.Unclassified,
            SoftwarePolicyClassificationNames.ToInstalledSoftwareStorage((SoftwarePolicyClassification?)null));
        Assert.Equal(
            SoftwarePolicyClassificationNames.White,
            SoftwarePolicyClassificationNames.ToInstalledSoftwareStorage(SoftwarePolicyClassification.Whitelist));
    }

    [Theory]
    [InlineData("white", SoftwarePolicyClassification.Whitelist)]
    [InlineData("WHITELIST", SoftwarePolicyClassification.Whitelist)]
    [InlineData("managed", SoftwarePolicyClassification.Managed)]
    [InlineData("black", SoftwarePolicyClassification.Blacklist)]
    [InlineData("Blacklist", SoftwarePolicyClassification.Blacklist)]
    public void TryParse_accepts_policy_aliases(string value, SoftwarePolicyClassification expected)
    {
        Assert.True(SoftwarePolicyClassificationNames.TryParse(value, out var classification));
        Assert.Equal(expected, classification);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unclassified")]
    [InlineData("unknown")]
    public void TryParse_rejects_unclassified_and_invalid_values(string? value)
    {
        Assert.False(SoftwarePolicyClassificationNames.TryParse(value, out _));
    }

    [Theory]
    [InlineData("white", "white")]
    [InlineData("WHITELIST", "white")]
    [InlineData("managed", "managed")]
    [InlineData("black", "black")]
    [InlineData("Blacklist", "black")]
    [InlineData("unclassified", "unclassified")]
    [InlineData(" UNCLASSIFIED ", "unclassified")]
    public void TryParseInstalledSoftware_normalizes_storage_values(string value, string expected)
    {
        Assert.True(SoftwarePolicyClassificationNames.TryParseInstalledSoftware(value, out var storage));
        Assert.Equal(expected, storage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("grey")]
    public void TryParseInstalledSoftware_rejects_invalid_values(string? value)
    {
        Assert.False(SoftwarePolicyClassificationNames.TryParseInstalledSoftware(value, out var storage));
        Assert.Equal(string.Empty, storage);
    }

    [Fact]
    public void InstalledSoftwareEntry_json_omits_null_classification()
    {
        var entry = new InstalledSoftwareEntry("Widget", "1.0", "Acme", null, "Machine", "Registry.Uninstall");
        var json = JsonSerializer.Serialize(entry, InventoryJsonSerializerContext.Default.InstalledSoftwareEntry);
        Assert.DoesNotContain("classification", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstalledSoftwareEntry_json_writes_stored_classification()
    {
        var entry = new InstalledSoftwareEntry("Widget", "1.0", "Acme", null, "Machine", "Registry.Uninstall", "unclassified");
        var json = JsonSerializer.Serialize(entry, InventoryJsonSerializerContext.Default.InstalledSoftwareEntry);
        Assert.Contains("\"classification\":\"unclassified\"", json, StringComparison.Ordinal);
    }
}
