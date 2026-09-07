namespace SwLicenseWatcher.Setup.Core;

public static class PackagerPaths
{
    public static bool TryDiscover(string searchRoot, out PackagerBundle bundle, out string error)
    {
        bundle = new PackagerBundle();
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(searchRoot) || !Directory.Exists(searchRoot))
        {
            error = "The packager search directory was not found.";
            return false;
        }

        var launcher = ReleaseLayout.FindFile(searchRoot, PayloadLayout.LauncherFileName);
        var setupUi = ReleaseLayout.FindFile(Path.Combine(searchRoot, PayloadLayout.SetupUiDirectory), PayloadLayout.SetupUiFileName)
            ?? ReleaseLayout.FindFile(searchRoot, PayloadLayout.SetupUiFileName);
        ReleaseLayout.TryFindAgents(searchRoot, out var worker, out var watchdog);

        bundle = new PackagerBundle
        {
            LauncherStubPath = launcher,
            SetupUiPath = setupUi,
            WorkerDirectory = string.IsNullOrEmpty(worker) ? null : worker,
            WatchdogDirectory = string.IsNullOrEmpty(watchdog) ? null : watchdog
        };
        return true;
    }
}

public sealed class PackagerBundle
{
    public string? LauncherStubPath { get; init; }

    public string? SetupUiPath { get; init; }

    public string? WorkerDirectory { get; init; }

    public string? WatchdogDirectory { get; init; }

    public bool HasAgents =>
        !string.IsNullOrWhiteSpace(WorkerDirectory) && !string.IsNullOrWhiteSpace(WatchdogDirectory);

    public bool CanPack =>
        !string.IsNullOrWhiteSpace(LauncherStubPath)
        && !string.IsNullOrWhiteSpace(SetupUiPath)
        && HasAgents;
}
