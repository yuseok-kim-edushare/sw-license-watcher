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

    [Fact]
    public void Failure_none_clears_restart_actions_before_file_replacement()
    {
        Assert.Equal(
            ["failureflag", "SwLicenseWatcher.Agent.Watchdog", "0"],
            WindowsServiceRegistration.FailureFlagOff("SwLicenseWatcher.Agent.Watchdog"));
    }

    [Theory]
    [InlineData("        PID                : 0", false, 0)]
    [InlineData("        PID                : 4821\r\n        FLAGS              :", true, 4821)]
    [InlineData("SERVICE_NAME: demo", false, 0)]
    public void Query_output_exposes_a_live_service_process_id(string output, bool found, int processId)
    {
        Assert.Equal(found, WindowsServiceProcessQuery.TryReadProcessId(output, out var parsed));
        Assert.Equal(processId, parsed);
    }
}
