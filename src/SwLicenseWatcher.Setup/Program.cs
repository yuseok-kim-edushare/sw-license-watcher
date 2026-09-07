using SwLicenseWatcher.Setup;
using SwLicenseWatcher.Setup.Core;

ApplicationConfiguration.Initialize();

var arguments = SetupArguments.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray());
var payloadDirectory = arguments.PayloadDirectory;
if (string.IsNullOrWhiteSpace(payloadDirectory))
{
    payloadDirectory = AppContext.BaseDirectory;
}

CompanySettings settings;
try
{
    settings = CompanySettingsStore.Load(PayloadLayout.GetCompanyJsonPath(payloadDirectory));
}
catch (Exception ex)
{
    MessageBox.Show(
        "company.json을 읽지 못했습니다.\n\n" + ex.Message,
        SetupPaths.ProductName,
        MessageBoxButtons.OK,
        MessageBoxIcon.Error);
    return;
}

Application.Run(arguments.Uninstall
    ? new UninstallForm(settings, arguments)
    : new InstallForm(settings, arguments, payloadDirectory));
