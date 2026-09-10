using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Core.Tests;

public class PcDisplayNamesTests
{
    [Theory]
    [InlineData("DESKTOP-1", null, "DESKTOP-1")]
    [InlineData("DESKTOP-1", "", "DESKTOP-1")]
    [InlineData("DESKTOP-1", "  ", "DESKTOP-1")]
    [InlineData("DESKTOP-1", " 마케팅-01 ", "마케팅-01")]
    public void Resolve_prefers_assigned_name(string hostName, string? assigned, string expected)
    {
        Assert.Equal(expected, PcDisplayNames.Resolve(hostName, assigned));
    }

    [Fact]
    public void TryNormalizeAssignedHostName_accepts_blank_and_trims()
    {
        Assert.True(PcDisplayNames.TryNormalizeAssignedHostName("  pc-01  ", out var normalized, out var error));
        Assert.Equal("pc-01", normalized);
        Assert.Equal(string.Empty, error);
        Assert.True(PcDisplayNames.TryNormalizeAssignedHostName(" ", out normalized, out error));
        Assert.Null(normalized);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryNormalizeAssignedHostName_rejects_overlong_values()
    {
        Assert.False(PcDisplayNames.TryNormalizeAssignedHostName(new string('a', 129), out var normalized, out var error));
        Assert.Null(normalized);
        Assert.Equal("assignedHostName must be at most 128 characters.", error);
    }

    [Fact]
    public void TryNormalizeAdminNotes_rejects_overlong_values()
    {
        Assert.False(PcDisplayNames.TryNormalizeAdminNotes(new string('n', 1025), out var normalized, out var error));
        Assert.Null(normalized);
        Assert.Equal("adminNotes must be at most 1024 characters.", error);
    }
}
