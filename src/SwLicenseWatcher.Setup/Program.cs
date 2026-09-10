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

var setup = new AgentSetupOrchestrator(new WindowsAgentMachineIntegration());
if (arguments.Uninstall)
{
    using var http = UninstallApiClient.Create(settings.ServerBaseUrl, settings.AgentToken);
    var uninstall = new UninstallOrchestrator(
        new UninstallApiClient(http),
        new InstalledDeviceCodeReader(),
        setup);
    Application.Run(new UninstallForm(uninstall));
}
else
{
    Application.Run(new InstallForm(settings, payloadDirectory, arguments.SourceExePath, setup));
}
