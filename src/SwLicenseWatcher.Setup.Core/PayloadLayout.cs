namespace SwLicenseWatcher.Setup.Core;

public static class PayloadLayout
{
    public const string CompanyJson = "company.json";
    public const string SetupUiDirectory = "setup-ui";
    public const string SetupUiFileName = "SwLicenseWatcher.Setup.exe";
    public const string WorkerRelative = "agent-worker/win-x64";
    public const string WatchdogRelative = "agent-watchdog/win-x64";
    public const string WorkerExe = "SwLicenseWatcher.Agent.Worker.exe";
    public const string WatchdogExe = "SwLicenseWatcher.Agent.Watchdog.exe";
    public const string LauncherFileName = "SwLicenseWatcher-Setup.exe";
    public const string VersionFileName = ".version";

    public static string SetupUiZipPath => SetupUiDirectory + "/" + SetupUiFileName;

    public static string WorkerExeZipPath => WorkerRelative + "/" + WorkerExe;

    public static string WatchdogExeZipPath => WatchdogRelative + "/" + WatchdogExe;

    public static string GetSetupUiPath(string payloadDirectory) =>
        Path.Combine(payloadDirectory, SetupUiDirectory, SetupUiFileName);

    public static string GetCompanyJsonPath(string payloadDirectory) =>
        Path.Combine(payloadDirectory, CompanyJson);

    public static string GetWorkerDirectory(string payloadDirectory) =>
        Path.Combine(payloadDirectory, "agent-worker", "win-x64");

    public static string GetWatchdogDirectory(string payloadDirectory) =>
        Path.Combine(payloadDirectory, "agent-watchdog", "win-x64");

    public static string GetWorkerExePath(string payloadDirectory) =>
        Path.Combine(GetWorkerDirectory(payloadDirectory), WorkerExe);

    public static string GetWatchdogExePath(string payloadDirectory) =>
        Path.Combine(GetWatchdogDirectory(payloadDirectory), WatchdogExe);
}
