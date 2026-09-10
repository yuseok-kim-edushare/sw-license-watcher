using System.ComponentModel;
using System.Runtime.Versioning;
using System.ServiceProcess;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Watchdog;

public interface IWorkerServiceControl
{
    Task StopAsync(CancellationToken cancellationToken);
    Task StartAsync(CancellationToken cancellationToken);
    bool IsRunning();
}

public sealed class WorkerServiceControl(IOptions<WatchdogOptions> options) : IWorkerServiceControl
{
    private readonly string _serviceName = options.Value.WorkerServiceName;

    [SupportedOSPlatform("windows")]
    public Task StopAsync(CancellationToken cancellationToken) =>
        SetStateAsync(start: false, cancellationToken);

    [SupportedOSPlatform("windows")]
    public Task StartAsync(CancellationToken cancellationToken) =>
        SetStateAsync(start: true, cancellationToken);

    [SupportedOSPlatform("windows")]
    public bool IsRunning()
    {
        using var service = new ServiceController(_serviceName);
        service.Refresh();
        return service.Status == ServiceControllerStatus.Running;
    }

    [SupportedOSPlatform("windows")]
    private Task SetStateAsync(bool start, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            using var service = new ServiceController(_serviceName);
            try
            {
                service.Refresh();
                if (start && service.Status != ServiceControllerStatus.Running)
                {
                    service.Start();
                    service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromMinutes(2));
                }
                else if (!start && service.Status != ServiceControllerStatus.Stopped)
                {
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromMinutes(2));
                }
            }
            catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: 5 })
            {
                throw new InvalidOperationException(ServiceIdentity.WatchdogMustBeLocalSystem, ex);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                throw new InvalidOperationException(ServiceIdentity.WatchdogMustBeLocalSystem, ex);
            }
        }, cancellationToken);
}
