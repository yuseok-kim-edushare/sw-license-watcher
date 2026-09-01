using System.Text.Json;
using SwLicenseWatcher.Api;

namespace SwLicenseWatcher.Api.Tests;

public class InventoryQueryApiTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void TryNormalizeClassification_allows_missing_filter(string? classification)
    {
        Assert.True(InventoryQueryApi.TryNormalizeClassification(classification, out var normalized, out var error));
        Assert.Null(normalized);
        Assert.Equal(string.Empty, error);
    }

    [Theory]
    [InlineData("unclassified", "unclassified")]
    [InlineData("WHITE", "white")]
    [InlineData("whitelist", "white")]
    [InlineData("managed", "managed")]
    [InlineData("black", "black")]
    [InlineData("Blacklist", "black")]
    public void TryNormalizeClassification_accepts_known_values(string classification, string expected)
    {
        Assert.True(InventoryQueryApi.TryNormalizeClassification(classification, out var normalized, out var error));
        Assert.Equal(expected, normalized);
        Assert.Equal(string.Empty, error);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("grey")]
    public void TryNormalizeClassification_rejects_invalid_values(string classification)
    {
        Assert.False(InventoryQueryApi.TryNormalizeClassification(classification, out var normalized, out var error));
        Assert.Null(normalized);
        Assert.Equal("classification must be white, managed, black, or unclassified.", error);
    }

    [Fact]
    public void Software_query_dtos_serialize_classification_with_source_generator()
    {
        var aggregate = new SoftwareAggregate("Chrome", "120.0", "unclassified", 3);
        var aggregateJson = JsonSerializer.Serialize(aggregate, ApiJsonSerializerContext.Default.SoftwareAggregate);
        Assert.Contains("\"Classification\":\"unclassified\"", aggregateJson, StringComparison.Ordinal);

        var device = new SoftwareDevice("PC-01", "host", "WORKGROUP", "Windows", "1.0.0", null, null, "120.0", "Google", "black");
        var deviceJson = JsonSerializer.Serialize(device, ApiJsonSerializerContext.Default.SoftwareDevice);
        Assert.Contains("\"Classification\":\"black\"", deviceJson, StringComparison.Ordinal);
    }
}
