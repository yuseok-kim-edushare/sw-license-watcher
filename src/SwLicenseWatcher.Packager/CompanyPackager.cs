using SwLicenseWatcher.Setup.Core;

namespace SwLicenseWatcher.Packager;

internal static class CompanyPackager
{
    public static void Build(
        string serverBaseUrl,
        string agentToken,
        string launcherStubPath,
        string setupUiPath,
        string workerDirectory,
        string watchdogDirectory,
        string outputExePath)
    {
        var version = ReleaseLayout.ReadVersion(workerDirectory);
        var settings = new CompanySettings
        {
            ServerBaseUrl = serverBaseUrl,
            AgentToken = agentToken,
            Version = version
        };
        var zip = PayloadZipBuilder.Build(settings, setupUiPath, workerDirectory, watchdogDirectory);
        AttachedPayload.Attach(launcherStubPath, zip, outputExePath);
        if (!AttachedPayload.HasPayload(outputExePath))
        {
            throw new InvalidOperationException("The company setup executable was written but the payload could not be read back.");
        }
    }
}
