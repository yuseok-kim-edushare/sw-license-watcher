namespace SwLicenseWatcher.Setup.Core;

public sealed class AgentSetupOrchestrator
{
    private readonly IAgentMachineIntegration _machine;
    private readonly AgentConfigurationWriter _configurationWriter;
    private readonly IAdministratorPrivilege _privilege;
    private readonly string _installRoot;
    private readonly string _stateRoot;

    public AgentSetupOrchestrator(
        IAgentMachineIntegration machine,
        AgentConfigurationWriter? configurationWriter = null,
        string? installRoot = null,
        string? stateRoot = null,
        IAdministratorPrivilege? privilege = null)
    {
        _machine = machine;
        _configurationWriter = configurationWriter ?? new AgentConfigurationWriter();
        _privilege = privilege ?? WindowsAdministratorPrivilege.Current;
        _installRoot = installRoot ?? SetupPaths.DefaultInstallRoot;
        _stateRoot = stateRoot ?? SetupPaths.DefaultStateRoot;
    }

    public void EnsureCanChangeMachine()
    {
        if (!_privilege.IsElevated)
        {
            throw new UnauthorizedAccessException(WindowsAdministratorPrivilege.RequiredMessage);
        }
    }

    public ExistingAgentInstallation DetectExistingInstallation() =>
        new ExistingAgentInstallationReader(_machine, _installRoot, _stateRoot).Read();

    public void Install(
        CompanySettings settings,
        string payloadDirectory,
        string deviceCode,
        string? sourceExePath)
    {
        EnsureCanChangeMachine();
        var existing = DetectExistingInstallation();
        var workerDirectory = SetupPaths.WorkerDirectory(_installRoot);
        var watchdogDirectory = SetupPaths.WatchdogDirectory(_installRoot);
        var queueDirectory = SetupPaths.QueueDirectory(_stateRoot);
        var healthPath = SetupPaths.HealthFilePath(_stateRoot);
        var stagingDirectory = SetupPaths.StagingDirectory(_stateRoot);
        var backupDirectory = SetupPaths.BackupDirectory(_stateRoot);
        // In-place upgrade stops and replaces services locally. It does not create a
        // server uninstall request and does not delete ProgramData identity/state.
        var domainName = string.IsNullOrWhiteSpace(existing.DomainName)
            ? SetupPaths.ResolveDomainName()
            : existing.DomainName;

        _machine.StopService(SetupPaths.WatchdogServiceName);
        _machine.StopService(SetupPaths.WorkerServiceName);

        CopyDirectory(PayloadLayout.GetWorkerDirectory(payloadDirectory), workerDirectory);
        CopyDirectory(PayloadLayout.GetWatchdogDirectory(payloadDirectory), watchdogDirectory);

        if (!File.Exists(SetupPaths.WorkerExe(_installRoot)))
        {
            throw new InvalidOperationException("Worker executable was not copied.");
        }

        if (!File.Exists(SetupPaths.WatchdogExe(_installRoot)))
        {
            throw new InvalidOperationException("Watchdog executable was not copied.");
        }

        Directory.CreateDirectory(queueDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(healthPath)!);
        Directory.CreateDirectory(stagingDirectory);
        Directory.CreateDirectory(backupDirectory);

        _configurationWriter.WriteWorker(
            workerDirectory,
            settings,
            deviceCode,
            domainName,
            healthPath,
            queueDirectory);
        _configurationWriter.WriteWatchdog(
            watchdogDirectory,
            settings,
            deviceCode,
            workerDirectory,
            healthPath,
            stagingDirectory,
            backupDirectory);

        _machine.InstallOrUpdateService(
            SetupPaths.WorkerServiceName,
            "SW License Watcher Worker",
            "Collects installed software and sends inventory snapshots.",
            SetupPaths.WorkerExe(_installRoot));
        _machine.InstallOrUpdateService(
            SetupPaths.WatchdogServiceName,
            "SW License Watcher Watchdog",
            "Downloads and applies signed Worker updates.",
            SetupPaths.WatchdogExe(_installRoot));

        _machine.StartService(SetupPaths.WorkerServiceName);
        _machine.StartService(SetupPaths.WatchdogServiceName);

        var installedSetup = SetupPaths.InstalledSetupExe(_installRoot);
        if (!string.IsNullOrWhiteSpace(sourceExePath) && File.Exists(sourceExePath))
        {
            CopySetupExecutable(sourceExePath, installedSetup);
        }

        _machine.RegisterArp(settings.Version, installedSetup);
    }

    public void Uninstall(bool removeState)
    {
        EnsureCanChangeMachine();
        _machine.StopService(SetupPaths.WatchdogServiceName);
        _machine.StopService(SetupPaths.WorkerServiceName);
        _machine.DeleteService(SetupPaths.WatchdogServiceName);
        _machine.DeleteService(SetupPaths.WorkerServiceName);

        foreach (var directory in new[]
                 {
                     SetupPaths.WatchdogDirectory(_installRoot),
                     SetupPaths.WorkerDirectory(_installRoot)
                 })
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        var setupExe = SetupPaths.InstalledSetupExe(_installRoot);
        if (File.Exists(setupExe))
        {
            try
            {
                File.Delete(setupExe);
            }
            catch (IOException)
            {
                // The running installer copy may lock itself; ARP still gets removed.
            }
        }

        if (Directory.Exists(_installRoot) && !Directory.EnumerateFileSystemEntries(_installRoot).Any())
        {
            Directory.Delete(_installRoot, recursive: false);
        }

        _machine.DeleteArp();

        if (removeState && Directory.Exists(_stateRoot))
        {
            Directory.Delete(_stateRoot, recursive: true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void CopySetupExecutable(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (PathsEqual(source, destination))
        {
            return;
        }

        File.Copy(source, destination, overwrite: true);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
