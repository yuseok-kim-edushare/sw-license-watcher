using SwLicenseWatcher.Admin.Services;

namespace SwLicenseWatcher.Admin.Tests;

public class DetailDrawerHostTests
{
    [Fact]
    public async Task Open_and_refresh_without_attach_do_not_throw()
    {
        var host = new DetailDrawerHost();

        await host.OpenDeviceAsync("pc-1");
        await host.RefreshAsync();
        host.Close();

        Assert.False(host.IsOpen);
    }
}
