using SwLicenseWatcher.Api;

namespace SwLicenseWatcher.Api.Tests;

public class RequestBodySizeLimitsTests
{
    [Fact]
    public void Resolve_limits_snapshots_to_8_mib()
    {
        Assert.Equal(8 * 1024 * 1024, RequestBodySizeLimits.Resolve("/api/inventory/snapshots"));
    }

    [Fact]
    public void Resolve_limits_heartbeats_to_64_kib()
    {
        Assert.Equal(64 * 1024, RequestBodySizeLimits.Resolve("/api/agents/heartbeats"));
    }

    [Theory]
    [InlineData("/api/inventory/snapshots")]
    [InlineData("/API/inventory/snapshots")]
    [InlineData("/api/agents/heartbeats")]
    [InlineData("/API/AGENTS/HEARTBEATS")]
    public void Resolve_is_case_insensitive(string path)
    {
        Assert.NotNull(RequestBodySizeLimits.Resolve(path));
    }

    [Theory]
    [InlineData("/api/inventory/snapshots/pc-01")]
    [InlineData("/api/inventory/devices")]
    [InlineData("/api/policies")]
    [InlineData("/api/updates/worker/manifest")]
    [InlineData("/health")]
    [InlineData("/")]
    [InlineData("")]
    public void Resolve_does_not_limit_other_paths(string path)
    {
        Assert.Null(RequestBodySizeLimits.Resolve(path));
    }
}
