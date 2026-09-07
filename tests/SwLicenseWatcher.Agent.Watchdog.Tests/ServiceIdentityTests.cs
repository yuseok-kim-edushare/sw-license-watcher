namespace SwLicenseWatcher.Agent.Watchdog.Tests;

public class ServiceIdentityTests
{
    [Fact]
    public void Installed_service_must_be_LocalSystem()
    {
        Assert.True(ServiceIdentity.IsDisallowedServiceAccount(isWindowsService: true, isLocalSystem: false));
        Assert.False(ServiceIdentity.IsDisallowedServiceAccount(isWindowsService: true, isLocalSystem: true));
    }

    [Fact]
    public void Console_diagnostics_may_run_as_an_elevated_administrator()
    {
        Assert.False(ServiceIdentity.IsDisallowedServiceAccount(isWindowsService: false, isLocalSystem: false));
        Assert.Contains("Local System", ServiceIdentity.WatchdogMustBeLocalSystem, StringComparison.Ordinal);
    }
}
