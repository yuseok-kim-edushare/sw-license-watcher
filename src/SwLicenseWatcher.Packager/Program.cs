using SwLicenseWatcher.Setup.Core;

namespace SwLicenseWatcher.Packager;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new PackagerForm(new PackagingWorkflow(new CompanyPackager())));
    }
}
