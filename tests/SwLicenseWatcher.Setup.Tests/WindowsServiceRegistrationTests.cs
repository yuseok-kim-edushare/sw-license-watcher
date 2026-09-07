using SwLicenseWatcher.Setup.Core;

namespace SwLicenseWatcher.Setup.Tests;

public class WindowsServiceRegistrationTests
{
    [Fact]
    public void Create_and_config_force_LocalSystem_not_the_installing_user()
    {
        var create = WindowsServiceRegistration.Create(
            "SwLicenseWatcher.Agent.Watchdog",
            "\"C:\\Program Files\\SwLicenseWatcher\\Agent.Watchdog\\SwLicenseWatcher.Agent.Watchdog.exe\"",
            "SW License Watcher Watchdog");
        var config = WindowsServiceRegistration.Config(
            "SwLicenseWatcher.Agent.Worker",
            "\"C:\\Program Files\\SwLicenseWatcher\\Agent.Worker\\SwLicenseWatcher.Agent.Worker.exe\"",
            "SW License Watcher Worker");

        Assert.Equal("LocalSystem", WindowsServiceRegistration.LocalSystemAccount);
        Assert.Contains("obj=", create);
        Assert.Equal("LocalSystem", create[Array.IndexOf(create, "obj=") + 1]);
        Assert.Contains("obj=", config);
        Assert.Equal("LocalSystem", config[Array.IndexOf(config, "obj=") + 1]);
        Assert.DoesNotContain("Administrator", create, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Failure_recovery_restarts_the_service()
    {
        var failure = WindowsServiceRegistration.FailureRestart("SwLicenseWatcher.Agent.Watchdog");
        Assert.Equal(["failure", "SwLicenseWatcher.Agent.Watchdog", "reset=", "86400", "actions=", "restart/5000/restart/30000/restart/60000"], failure);
        Assert.Equal(["failureflag", "SwLicenseWatcher.Agent.Watchdog", "1"], WindowsServiceRegistration.FailureFlag("SwLicenseWatcher.Agent.Watchdog"));
    }
}
