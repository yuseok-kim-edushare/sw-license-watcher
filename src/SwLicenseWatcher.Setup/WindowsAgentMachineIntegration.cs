using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;
using SwLicenseWatcher.Setup.Core;

namespace SwLicenseWatcher.Setup;

internal sealed class WindowsAgentMachineIntegration : IAgentMachineIntegration
{
    public void InstallOrUpdateService(string name, string displayName, string description, string exePath)
    {
        var binPath = $"\"{exePath}\"";
        RunSc(ServiceExists(name)
            ? WindowsServiceRegistration.Config(name, binPath, displayName)
            : WindowsServiceRegistration.Create(name, binPath, displayName));
        RunSc(WindowsServiceRegistration.Description(name, description));
        RunSc(WindowsServiceRegistration.FailureRestart(name));
        RunSc(WindowsServiceRegistration.FailureFlag(name));
    }

    public void StartService(string name)
    {
        using var service = new ServiceController(name);
        if (service.Status != ServiceControllerStatus.Running)
        {
            service.Start();
        }

        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
    }

    public void StopService(string name)
    {
        if (!ServiceExists(name))
        {
            return;
        }

        using var service = new ServiceController(name);
        if (service.Status == ServiceControllerStatus.Stopped)
        {
            return;
        }

        service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromMinutes(1));
    }

    public void DeleteService(string name)
    {
        if (!ServiceExists(name))
        {
            return;
        }

        StopService(name);
        RunSc(WindowsServiceRegistration.Delete(name));
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (!ServiceExists(name))
            {
                return;
            }

            Thread.Sleep(300);
        }

        throw new InvalidOperationException($"Service {name} was not deleted in time.");
    }

    public void RegisterArp(string version, string setupExe)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(SetupPaths.ArpRegistryPath, writable: true)
                ?? throw new InvalidOperationException("Could not write the uninstall registry key.");
            key.SetValue("DisplayName", SetupPaths.ProductName);
            key.SetValue("Publisher", SetupPaths.Publisher);
            key.SetValue("DisplayVersion", version);
            key.SetValue("UninstallString", $"\"{setupExe}\" /uninstall");
            key.SetValue("DisplayIcon", setupExe);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", 20 * 1024, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new UnauthorizedAccessException(WindowsAdministratorPrivilege.RequiredMessage, ex);
        }
    }

    public void DeleteArp()
    {
        try
        {
            Registry.LocalMachine.DeleteSubKeyTree(SetupPaths.ArpRegistryPath, throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new UnauthorizedAccessException(WindowsAdministratorPrivilege.RequiredMessage, ex);
        }
    }

    private static bool ServiceExists(string name) =>
        ServiceController.GetServices().Any(service =>
            string.Equals(service.ServiceName, name, StringComparison.OrdinalIgnoreCase));

    private static void RunSc(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            throw new ArgumentException("sc.exe requires at least one argument.", nameof(arguments));
        }

        var start = new ProcessStartInfo
        {
            FileName = "sc.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("sc.exe could not be started.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            if (process.ExitCode == 5)
            {
                throw new UnauthorizedAccessException(
                    WindowsAdministratorPrivilege.RequiredMessage);
            }

            throw new InvalidOperationException(
                $"sc.exe {arguments[0]} failed with exit code {process.ExitCode}. {output}".Trim());
        }
    }
}
