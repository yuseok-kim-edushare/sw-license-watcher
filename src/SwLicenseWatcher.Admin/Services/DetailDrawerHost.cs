using SwLicenseWatcher.Admin.Components;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Admin.Services;

public sealed class DetailDrawerHost
{
    private DetailDrawer? _drawer;

    public bool IsOpen => _drawer?.IsOpen == true;

    public void Attach(DetailDrawer drawer) => _drawer = drawer;

    public void Detach(DetailDrawer drawer)
    {
        if (ReferenceEquals(_drawer, drawer))
        {
            _drawer = null;
        }
    }

    public Task OpenDeviceAsync(string deviceCode) =>
        _drawer?.OpenDeviceAsync(deviceCode) ?? Task.CompletedTask;

    public Task OpenSoftwareAsync(SoftwareAggregate row) =>
        _drawer?.OpenSoftwareAsync(row) ?? Task.CompletedTask;

    public Task OpenSoftwareAsync(string name, string? classification = null, string? publisher = null) =>
        _drawer?.OpenSoftwareAsync(name, classification, publisher) ?? Task.CompletedTask;

    public Task RefreshAsync() => _drawer?.RefreshAsync() ?? Task.CompletedTask;

    public void Close() => _drawer?.Close();
}
