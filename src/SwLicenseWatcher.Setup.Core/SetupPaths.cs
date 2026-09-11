namespace SwLicenseWatcher.Setup.Core;

public static class SetupPaths
{
    public const string WorkerServiceName = "SwLicenseWatcher.Agent.Worker";
    public const string WatchdogServiceName = "SwLicenseWatcher.Agent.Watchdog";
    public const string ProductName = "SW License Watcher";
    public const string Publisher = "SW License Watcher";
    public const string ArpKeyName = "SwLicenseWatcher";
    public const string InstalledSetupFileName = "SwLicenseWatcher-Setup.exe";

    public static string DefaultInstallRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SwLicenseWatcher");

    public static string DefaultStateRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SwLicenseWatcher");

    public static string WorkerDirectory(string installRoot) => Path.Combine(installRoot, "Agent.Worker");

    public static string WatchdogDirectory(string installRoot) => Path.Combine(installRoot, "Agent.Watchdog");

    public static string WorkerExe(string installRoot) =>
        Path.Combine(WorkerDirectory(installRoot), PayloadLayout.WorkerExe);

    public static string WatchdogExe(string installRoot) =>
        Path.Combine(WatchdogDirectory(installRoot), PayloadLayout.WatchdogExe);

    public static string InstalledSetupExe(string installRoot) =>
        Path.Combine(installRoot, InstalledSetupFileName);

    public static string QueueDirectory(string stateRoot) => Path.Combine(stateRoot, "state", "queue");

    public static string HealthFilePath(string stateRoot) => Path.Combine(stateRoot, "state", "worker-health.json");

    public static string DeviceIdentityPath(string stateRoot) =>
        Path.Combine(stateRoot, "state", "device-identity.bin");

    public static string AssignmentFilePath(string stateRoot) =>
        Path.Combine(stateRoot, "state", "assigned-host-name.json");

    public static string StagingDirectory(string stateRoot) => Path.Combine(stateRoot, "staging");

    public static string BackupDirectory(string stateRoot) => Path.Combine(stateRoot, "backup");

    public static string ArpRegistryPath =>
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + ArpKeyName;

    public static string ResolveDomainName()
    {
        var domain = Environment.GetEnvironmentVariable("USERDOMAIN");
        return string.IsNullOrWhiteSpace(domain) ? "WORKGROUP" : domain;
    }
}
