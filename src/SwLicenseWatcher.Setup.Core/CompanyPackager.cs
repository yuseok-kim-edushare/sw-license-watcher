namespace SwLicenseWatcher.Setup.Core;

public interface ICompanyPackager
{
    void Build(
        string serverBaseUrl,
        string agentToken,
        string launcherStubPath,
        string setupUiPath,
        string workerDirectory,
        string watchdogDirectory,
        string outputExePath);
}

public sealed class CompanyPackager : ICompanyPackager
{
    public void Build(
        string serverBaseUrl,
        string agentToken,
        string launcherStubPath,
        string setupUiPath,
        string workerDirectory,
        string watchdogDirectory,
        string outputExePath)
    {
        var settings = new CompanySettings
        {
            ServerBaseUrl = serverBaseUrl,
            AgentToken = agentToken,
            Version = ReleaseLayout.ReadVersion(workerDirectory)
        };
        var zip = PayloadZipBuilder.Build(settings, setupUiPath, workerDirectory, watchdogDirectory);
        AttachedPayload.Attach(launcherStubPath, zip, outputExePath);
        if (!AttachedPayload.HasPayload(outputExePath))
        {
            throw new InvalidOperationException(
                "The company setup executable was written but the payload could not be read back.");
        }
    }
}
