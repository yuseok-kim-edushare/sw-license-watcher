using System.Text.Json;
using SwLicenseWatcher.Admin.Services;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Admin.Tests;

public class AdminMappingTests
{
    [Fact]
    public void Filter_values_preserve_api_storage_names_and_order()
    {
        Assert.Equal(["", "white", "managed", "black", "unclassified"], DashboardPresentation.SoftwareClasses);
        Assert.Equal(["", "white", "managed", "black"], DashboardPresentation.PolicyClasses);
    }

    [Theory]
    [InlineData(null, "-")]
    [InlineData("", "-")]
    [InlineData("value", "value")]
    [InlineData(42, "42")]
    public void Dash_maps_missing_and_present_values(object? value, string expected)
    {
        Assert.Equal(expected, DashboardPresentation.Dash(value));
    }

    [Theory]
    [InlineData("company", "회사")]
    [InlineData("byo", "BYO")]
    [InlineData("Company", "-")]
    [InlineData(null, "-")]
    public void LicenseLabel_maps_known_storage_values(string? value, string expected)
    {
        Assert.Equal(expected, DashboardPresentation.LicenseLabel(value));
    }

    [Theory]
    [InlineData(SoftwarePolicyClassification.Whitelist, "white")]
    [InlineData(SoftwarePolicyClassification.Managed, "managed")]
    [InlineData(SoftwarePolicyClassification.Blacklist, "black")]
    [InlineData((SoftwarePolicyClassification)999, "managed")]
    public void ClassifyStorage_maps_enum_values(SoftwarePolicyClassification value, string expected)
    {
        Assert.Equal(expected, DashboardPresentation.ClassificationStorage(value));
    }

    [Fact]
    public void SoftwareKey_includes_name_version_and_classification()
    {
        var first = new SoftwareAggregate("Editor", "1.0", "managed", 2);
        var secondVersion = first with { Version = "2.0" };
        var secondClass = first with { Classification = "black" };

        Assert.Equal("Editor\u001f1.0\u001fmanaged", DashboardPresentation.SoftwareKey(first));
        Assert.NotEqual(DashboardPresentation.SoftwareKey(first), DashboardPresentation.SoftwareKey(secondVersion));
        Assert.NotEqual(DashboardPresentation.SoftwareKey(first), DashboardPresentation.SoftwareKey(secondClass));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(" company ", "company")]
    public void EmptyToNull_trims_values_and_maps_whitespace_to_null(string? value, string? expected)
    {
        Assert.Equal(expected, DashboardPresentation.EmptyToNull(value));
    }

    [Theory]
    [InlineData("WHITE", SoftwarePolicyClassification.Whitelist)]
    [InlineData("blacklist", SoftwarePolicyClassification.Blacklist)]
    [InlineData("managed", SoftwarePolicyClassification.Managed)]
    public void TryParseClassification_accepts_ui_and_legacy_names(
        string value,
        SoftwarePolicyClassification expected)
    {
        Assert.True(SoftwarePolicyClassificationNames.TryParse(value, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Admin_json_options_use_camel_case_enum_contract_and_case_insensitive_reads()
    {
        var request = new SoftwareClassificationWriteRequest(
            SoftwarePolicyClassification.Managed,
            "Contoso",
            "company");

        var json = JsonSerializer.Serialize(request, AdminApiClient.JsonOptions);
        var health = JsonSerializer.Deserialize<HealthResponse>(
            """{"STATUS":"Healthy","UTC":"1970-01-01T00:00:00Z"}""",
            AdminApiClient.JsonOptions);

        Assert.Equal(
            """{"classification":"managed","publisher":"Contoso","defaultLicenseSource":"company"}""",
            json);
        Assert.NotNull(health);
        Assert.Equal("Healthy", health.Status);
        Assert.Equal(DateTimeOffset.UnixEpoch, health.Utc);
    }
}
