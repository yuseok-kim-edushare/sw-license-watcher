using System.Text.Json;
using SwLicenseWatcher.Api;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api.Tests;

public class PolicyQueryApiTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void TryNormalizePolicyClassification_allows_missing_filter(string? classification)
    {
        Assert.True(PolicyQueryApi.TryNormalizePolicyClassification(classification, out var normalized, out var error));
        Assert.Null(normalized);
        Assert.Equal(string.Empty, error);
    }

    [Theory]
    [InlineData("white", "white")]
    [InlineData("WHITELIST", "white")]
    [InlineData("managed", "managed")]
    [InlineData("black", "black")]
    [InlineData("Blacklist", "black")]
    public void TryNormalizePolicyClassification_accepts_known_values(string classification, string expected)
    {
        Assert.True(PolicyQueryApi.TryNormalizePolicyClassification(classification, out var normalized, out var error));
        Assert.Equal(expected, normalized);
        Assert.Equal(string.Empty, error);
    }

    [Theory]
    [InlineData("unclassified")]
    [InlineData("unknown")]
    [InlineData("grey")]
    public void TryNormalizePolicyClassification_rejects_invalid_values(string classification)
    {
        Assert.False(PolicyQueryApi.TryNormalizePolicyClassification(classification, out var normalized, out var error));
        Assert.Null(normalized);
        Assert.Equal("classification must be white, managed, or black.", error);
    }

    [Fact]
    public void TryParseSince_allows_missing_value()
    {
        Assert.True(QueryList.TryParseSince(null, out var value, out var error));
        Assert.Null(value);
        Assert.Equal(string.Empty, error);
        Assert.True(QueryList.TryParseSince("  ", out value, out error));
        Assert.Null(value);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryParseSince_accepts_iso8601()
    {
        Assert.True(QueryList.TryParseSince("2026-01-15T00:00:00Z", out var value, out var error));
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), value);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryParseSince_rejects_invalid_values()
    {
        Assert.False(QueryList.TryParseSince("not-a-date", out var value, out var error));
        Assert.Null(value);
        Assert.Equal("since must be an ISO 8601 date/time.", error);
    }

    [Fact]
    public void TryValidateSearch_rejects_overlong_values()
    {
        Assert.True(QueryList.TryValidateSearch(null, out var error));
        Assert.Equal(string.Empty, error);
        Assert.False(QueryList.TryValidateSearch(new string('a', QueryList.MaxSearchLength + 1), out error));
        Assert.Equal($"search must be at most {QueryList.MaxSearchLength} characters.", error);
    }

    [Fact]
    public void NormalizePaging_uses_inventory_defaults_and_clamps()
    {
        Assert.Equal((0, QueryList.DefaultTake), QueryList.NormalizePaging(null, null, csv: false));
        Assert.Equal((0, QueryList.CsvDefaultTake), QueryList.NormalizePaging(null, null, csv: true));
        Assert.Equal((0, QueryList.MaxTake), QueryList.NormalizePaging(-5, 50_000, csv: false));
        Assert.Equal((12, 1), QueryList.NormalizePaging(12, 0, csv: false));
    }

    [Fact]
    public void Policy_and_violation_list_dtos_serialize_with_source_generator()
    {
        var policy = new SoftwarePolicyEntry(
            7, "*Torrent*", "BitTorrent", "3.*", SoftwarePolicyClassification.Blacklist, "P2P 금지", true,
            new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));
        var policies = new PolicyListResponse(0, 100, 1, [policy]);
        var policyJson = JsonSerializer.Serialize(policies, ApiJsonSerializerContext.Default.PolicyListResponse);
        Assert.Contains("\"TotalCount\":1", policyJson, StringComparison.Ordinal);
        Assert.Contains("\"Classification\":\"black\"", policyJson, StringComparison.Ordinal);
        Assert.Contains("*Torrent*", policyJson, StringComparison.Ordinal);

        var violation = new SoftwareViolationEntry(
            9, "PC-01", "host", "uTorrent", "3.5", "BitTorrent", 7, "*Torrent*",
            SoftwarePolicyClassification.Blacklist,
            new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 16, 0, 0, 0, TimeSpan.Zero));
        var violations = new ViolationListResponse(0, 100, 1, [violation]);
        var violationJson = JsonSerializer.Serialize(violations, ApiJsonSerializerContext.Default.ViolationListResponse);
        Assert.Contains("\"TotalCount\":1", violationJson, StringComparison.Ordinal);
        Assert.Contains("\"DeviceCode\":\"PC-01\"", violationJson, StringComparison.Ordinal);
        Assert.Contains("\"Classification\":\"black\"", violationJson, StringComparison.Ordinal);
    }

    [Fact]
    public void PolicyCsvRow_formats_fields_and_escapes_formula_injection()
    {
        var policy = new SoftwarePolicyEntry(
            1, "=cmd", "+pub", "@1.0", SoftwarePolicyClassification.Managed, "a,b", false,
            new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));
        var escaped = PolicyQueryApi.PolicyCsvRow(policy).Select(InventoryCsv.Escape).ToArray();
        Assert.Equal("1", escaped[0]);
        Assert.Equal("'=cmd", escaped[1]);
        Assert.Equal("'+pub", escaped[2]);
        Assert.Equal("'@1.0", escaped[3]);
        Assert.Equal("managed", escaped[4]);
        Assert.Equal("\"a,b\"", escaped[5]);
        Assert.Equal("false", escaped[6]);
        Assert.Equal("2026-01-15T12:00:00.0000000+00:00", escaped[7]);
    }

    [Fact]
    public void ViolationCsvRow_formats_ids_and_classification()
    {
        var violation = new SoftwareViolationEntry(
            2, "PC-01", "host", "uTorrent", "3.5", null, 7, "*Torrent*",
            SoftwarePolicyClassification.Blacklist,
            new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 16, 0, 0, 0, TimeSpan.Zero));
        var row = PolicyQueryApi.ViolationCsvRow(violation);
        Assert.Equal("2", row[0]);
        Assert.Equal("PC-01", row[1]);
        Assert.Equal("uTorrent", row[3]);
        Assert.Equal("7", row[6]);
        Assert.Equal("black", row[8]);
        Assert.Equal("2026-01-15T00:00:00.0000000+00:00", row[9]);
    }

    [Fact]
    public void WantsCsv_is_case_insensitive()
    {
        Assert.True(QueryList.WantsCsv("csv"));
        Assert.True(QueryList.WantsCsv("CSV"));
        Assert.False(QueryList.WantsCsv(null));
        Assert.False(QueryList.WantsCsv("json"));
    }
}
