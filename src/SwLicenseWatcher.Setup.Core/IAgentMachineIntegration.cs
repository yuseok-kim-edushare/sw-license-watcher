namespace SwLicenseWatcher.Setup.Core;

public interface IAgentMachineIntegration
{
    void StopService(string name);

    void InstallOrUpdateService(string name, string displayName, string description, string exePath);

    void StartService(string name);

    void DeleteService(string name);

    void RegisterArp(string version, string setupExe);

    void DeleteArp();
}
