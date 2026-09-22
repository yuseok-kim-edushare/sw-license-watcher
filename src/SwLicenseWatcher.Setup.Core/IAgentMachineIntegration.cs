namespace SwLicenseWatcher.Setup.Core;

public interface IAgentMachineIntegration
{
    bool ServiceExists(string name);

    void StopService(string name);

    /// <summary>
    /// Stops recovery restarts and the service process so install files can be replaced.
    /// </summary>
    void PrepareForFileReplacement(string name) => StopService(name);

    void InstallOrUpdateService(string name, string displayName, string description, string exePath);

    void StartService(string name);

    void DeleteService(string name);

    void RegisterArp(string version, string setupExe);

    void DeleteArp();
}
