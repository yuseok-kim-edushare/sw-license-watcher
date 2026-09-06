using System.Text.Json;
using SwLicenseWatcher.Api;

namespace SwLicenseWatcher.Api.Tests;

public class UninstallQueryApiTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void TryValidateDeviceCode_requires_a_value(string? deviceCode)
    {
        Assert.False(UninstallQueryApi.TryValidateDeviceCode(deviceCode, out var normalized, out var error));
        Assert.Equal(string.Empty, normalized);
        Assert.Equal("deviceCode is required.", error);
    }

    [Fact]
    public void TryValidateDeviceCode_rejects_values_longer_than_128_characters()
    {
        Assert.False(UninstallQueryApi.TryValidateDeviceCode(new string('d', 129), out var normalized, out var error));
        Assert.Equal(string.Empty, normalized);
        Assert.Equal("deviceCode must be at most 128 characters.", error);
    }

    [Fact]
    public void TryValidateDeviceCode_trims_valid_codes()
    {
        Assert.True(UninstallQueryApi.TryValidateDeviceCode("  PC-01  ", out var normalized, out var error));
        Assert.Equal("PC-01", normalized);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void Admin_list_dto_does_not_include_the_plaintext_code()
    {
        var item = new AdminUninstallRequest(
            3,
            "PC-01",
            "host",
            "pending",
            new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.Zero),
            null,
            null,
            null);
        var json = JsonSerializer.Serialize(
            new UninstallRequestListResponse(0, 100, 1, [item]),
            ApiJsonSerializerContext.Default.UninstallRequestListResponse);

        Assert.Contains("\"DeviceCode\":\"PC-01\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Status\":\"pending\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Code\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("code_hash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Agent_poll_dto_includes_code_only_on_the_response_type()
    {
        var approved = new AgentUninstallRequestResponse(
            3,
            "PC-01",
            "approved",
            new DateTimeOffset(2026, 9, 6, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 6, 10, 1, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 6, 10, 16, 0, TimeSpan.Zero),
            "ABCD2345");
        var json = JsonSerializer.Serialize(approved, ApiJsonSerializerContext.Default.AgentUninstallRequestResponse);
        Assert.Contains("\"Code\":\"ABCD2345\"", json, StringComparison.Ordinal);

        var pending = approved with { Status = "pending", Code = null };
        var pendingJson = JsonSerializer.Serialize(pending, ApiJsonSerializerContext.Default.AgentUninstallRequestResponse);
        Assert.Contains("\"Status\":\"pending\"", pendingJson, StringComparison.Ordinal);
        Assert.DoesNotContain("ABCD2345", pendingJson, StringComparison.Ordinal);
    }
}
