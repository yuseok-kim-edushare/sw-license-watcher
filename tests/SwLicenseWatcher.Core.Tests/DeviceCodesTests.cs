using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Core.Tests;

public class DeviceCodesTests
{
    [Fact]
    public void Official_prefers_assigned_code()
    {
        Assert.Equal("ASSET-1", DeviceCodes.Official("DESKTOP-1", " ASSET-1 "));
        Assert.Equal("DESKTOP-1", DeviceCodes.Official("DESKTOP-1", null));
    }

    [Fact]
    public void TryNormalizeAssigned_allows_blank_and_rejects_overlong()
    {
        Assert.True(DeviceCodes.TryNormalizeAssigned("  ", out var empty, out _));
        Assert.Null(empty);
        Assert.False(DeviceCodes.TryNormalizeAssigned(new string('a', 129), out _, out var error));
        Assert.Contains("128", error);
    }
}
