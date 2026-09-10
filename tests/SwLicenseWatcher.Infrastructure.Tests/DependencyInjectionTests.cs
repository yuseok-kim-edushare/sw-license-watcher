using Microsoft.Extensions.DependencyInjection;
using SwLicenseWatcher.Application;
using SwLicenseWatcher.Infrastructure;

namespace SwLicenseWatcher.Infrastructure.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddSwLicenseWatcherInfrastructure_registers_each_port_as_singleton()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.AddSwLicenseWatcherInfrastructure();
        using var provider = services.BuildServiceProvider();

        AssertSingleton<IHealthProbe>(provider);
        AssertSingleton<ISnapshotRepository>(provider);
        AssertSingleton<IHeartbeatRepository>(provider);
        AssertSingleton<IDeviceQuery>(provider);
        AssertSingleton<ISoftwareQuery>(provider);
        AssertSingleton<IViolationQuery>(provider);
        AssertSingleton<IPolicyStore>(provider);
        AssertSingleton<IUninstallRequestStore>(provider);
        AssertSingleton<IWorkerUpdatePinStore>(provider);
    }

    private static void AssertSingleton<T>(IServiceProvider provider) where T : class =>
        Assert.Same(provider.GetRequiredService<T>(), provider.GetRequiredService<T>());
}
