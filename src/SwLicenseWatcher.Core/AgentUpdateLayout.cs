namespace SwLicenseWatcher.Core;

public static class AgentUpdateLayout
{
    public const string WatchdogPayloadDirectoryName = "watchdog-update";

    public const string UpdateCommittedFileName = ".update-committed";

    public const string WatchdogExeName = "SwLicenseWatcher.Agent.Watchdog.exe";

    public const string WatchdogServiceName = "SwLicenseWatcher.Agent.Watchdog";

    public const string WatchdogDirectoryName = "Agent.Watchdog";

    public static string WatchdogInstallDirectory(string workerDirectory)
    {
        var root = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(workerDirectory));
        return Path.Combine(root ?? workerDirectory, WatchdogDirectoryName);
    }

    public static string WatchdogBackupDirectory(string healthFilePath)
    {
        var stateDirectory = Path.GetDirectoryName(healthFilePath);
        var dataRoot = string.IsNullOrEmpty(stateDirectory)
            ? null
            : Path.GetDirectoryName(stateDirectory);
        return Path.Combine(dataRoot ?? Path.GetTempPath(), "backup", "watchdog-previous");
    }
}
