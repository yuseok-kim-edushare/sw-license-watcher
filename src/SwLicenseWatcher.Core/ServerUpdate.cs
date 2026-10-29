namespace SwLicenseWatcher.Core;

public enum ServerUpdateHostKind
{
    WindowsService,
    Iis,
    Linux
}

public static class ServerUpdateHosts
{
    public const string WindowsServiceName = "windows-service";
    public const string IisName = "iis";
    public const string LinuxName = "linux";

    public static bool TryParse(string? value, out ServerUpdateHostKind kind)
    {
        if (string.Equals(value, WindowsServiceName, StringComparison.OrdinalIgnoreCase))
        {
            kind = ServerUpdateHostKind.WindowsService;
            return true;
        }

        if (string.Equals(value, IisName, StringComparison.OrdinalIgnoreCase))
        {
            kind = ServerUpdateHostKind.Iis;
            return true;
        }

        if (string.Equals(value, LinuxName, StringComparison.OrdinalIgnoreCase))
        {
            kind = ServerUpdateHostKind.Linux;
            return true;
        }

        kind = default;
        return false;
    }

    public static string ToName(ServerUpdateHostKind kind) => kind switch
    {
        ServerUpdateHostKind.WindowsService => WindowsServiceName,
        ServerUpdateHostKind.Iis => IisName,
        ServerUpdateHostKind.Linux => LinuxName,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static bool TryDetect(
        bool isWindows,
        bool isLinux,
        string? appPoolId,
        string? iisPhysicalPath,
        out ServerUpdateHostKind kind)
    {
        if (isLinux && !isWindows)
        {
            kind = ServerUpdateHostKind.Linux;
            return true;
        }

        if (!isWindows)
        {
            kind = default;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(appPoolId) || !string.IsNullOrWhiteSpace(iisPhysicalPath))
        {
            kind = ServerUpdateHostKind.Iis;
            return true;
        }

        kind = ServerUpdateHostKind.WindowsService;
        return true;
    }
}

public static class ServerUpdatePackageNames
{
    public const string HelperDirectoryName = "update-helper";

    public static string ZipFileName(ServerUpdateHostKind kind, string version)
    {
        var normalized = WorkerUpdatePackageNames.NormalizeVersion(version);
        return kind switch
        {
            ServerUpdateHostKind.WindowsService => $"SwLicenseWatcher.Api.win-x64-{normalized}.zip",
            ServerUpdateHostKind.Iis => $"SwLicenseWatcher.Api.iis-win-x64-{normalized}.zip",
            ServerUpdateHostKind.Linux => $"SwLicenseWatcher.Api.linux-x64-{normalized}.zip",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}

public static class ServerUpdateFiles
{
    public static bool IsPreserved(string pathOrFileName)
    {
        var name = Path.GetFileName(pathOrFileName);
        if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
            (name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) ||
             name.StartsWith("device-ca", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    public static bool IsHelperPayload(string pathOrFileName) =>
        string.Equals(Path.GetFileName(pathOrFileName), ServerUpdatePackageNames.HelperDirectoryName, StringComparison.OrdinalIgnoreCase);
}

public static class ServerUpdateLayout
{
    public const string WindowsTaskName = "SwLicenseWatcher Api Update";
    public const string WindowsServiceName = "SwLicenseWatcher.Api";
    public const string LinuxApiUnitName = "sw-license-watcher-api";
    public const string LinuxUpdateUnitName = "sw-license-watcher-api-update.service";
    public const string LinuxServiceUser = "swlw";
    public const string ConfigFileName = "helper.conf";
    public const string RequestFileName = "request.conf";
    public const string ResultFileName = "result.conf";
    public const string ReadyFileName = "ready.conf";
    public const string PayloadDirectoryName = "payload";
    public const string BackupDirectoryName = "backup";
    public const string WindowsHelperDirectory = @"C:\Program Files\SwLicenseWatcher\ApiUpdate";
    public const string WindowsStagingDirectory = @"C:\ProgramData\SwLicenseWatcher\api-update";
    public const string LinuxHelperScriptPath = "/usr/local/lib/sw-license-watcher/apply-api-update.sh";
    public const string LinuxConfigPath = "/etc/sw-license-watcher/update-helper.conf";
    public const string LinuxStagingDirectory = "/var/lib/sw-license-watcher/api-update";

    public static string DefaultConfigPath(ServerUpdateHostKind kind) => kind switch
    {
        ServerUpdateHostKind.Linux => LinuxConfigPath,
        _ => Path.Combine(WindowsHelperDirectory, ConfigFileName)
    };

    public static string DefaultStagingPath(ServerUpdateHostKind kind) => kind switch
    {
        ServerUpdateHostKind.Linux => LinuxStagingDirectory,
        _ => WindowsStagingDirectory
    };
}

public sealed class ServerUpdateOptions
{
    public string HelperConfigPath { get; set; } = string.Empty;
}

public sealed class ServerUpdateHelperConfig
{
    public ServerUpdateHostKind HostKind { get; init; }

    public string InstallPath { get; init; } = string.Empty;

    public string ServiceName { get; init; } = string.Empty;

    public string AppPoolName { get; init; } = string.Empty;

    public string StagingPath { get; init; } = string.Empty;

    public string HealthUrl { get; init; } = string.Empty;

    public string ServiceUser { get; init; } = string.Empty;

    public string ServiceGroup { get; init; } = string.Empty;

    public string TaskName { get; init; } = ServerUpdateLayout.WindowsTaskName;

    public string UpdateUnitName { get; init; } = ServerUpdateLayout.LinuxUpdateUnitName;
}

public static class ServerUpdateConfigFile
{
    public const string HostKindKey = "HostKind";
    public const string InstallPathKey = "InstallPath";
    public const string ServiceNameKey = "ServiceName";
    public const string AppPoolNameKey = "AppPoolName";
    public const string StagingPathKey = "StagingPath";
    public const string HealthUrlKey = "HealthUrl";
    public const string ServiceUserKey = "ServiceUser";
    public const string ServiceGroupKey = "ServiceGroup";
    public const string TaskNameKey = "TaskName";
    public const string UpdateUnitNameKey = "UpdateUnitName";
    public const string VersionKey = "Version";
    public const string Sha256Key = "Sha256";
    public const string StatusKey = "Status";
    public const string MessageKey = "Message";

    public static string Format(ServerUpdateHelperConfig config)
    {
        var lines = new[]
        {
            Pair(HostKindKey, ServerUpdateHosts.ToName(config.HostKind)),
            Pair(InstallPathKey, config.InstallPath),
            Pair(ServiceNameKey, config.ServiceName),
            Pair(AppPoolNameKey, config.AppPoolName),
            Pair(StagingPathKey, config.StagingPath),
            Pair(HealthUrlKey, config.HealthUrl),
            Pair(ServiceUserKey, config.ServiceUser),
            Pair(ServiceGroupKey, config.ServiceGroup),
            Pair(TaskNameKey, config.TaskName),
            Pair(UpdateUnitNameKey, config.UpdateUnitName)
        };
        return string.Join('\n', lines) + "\n";
    }

    public static bool TryParse(string text, out ServerUpdateHelperConfig? config, out string error)
    {
        config = null;
        if (!TryReadPairs(text, out var pairs, out error))
        {
            return false;
        }

        if (!ServerUpdateHosts.TryParse(Value(pairs, HostKindKey), out var kind))
        {
            error = "HostKind must be windows-service, iis, or linux.";
            return false;
        }

        var installPath = Value(pairs, InstallPathKey);
        var stagingPath = Value(pairs, StagingPathKey);
        if (!IsSafeAbsolutePath(installPath) || !IsSafeAbsolutePath(stagingPath))
        {
            error = "InstallPath and StagingPath must be absolute paths without traversal.";
            return false;
        }

        if (PathsNest(installPath, stagingPath))
        {
            error = "StagingPath must be outside InstallPath.";
            return false;
        }

        var serviceName = Value(pairs, ServiceNameKey);
        var appPoolName = Value(pairs, AppPoolNameKey);
        var taskName = Value(pairs, TaskNameKey);
        var updateUnit = Value(pairs, UpdateUnitNameKey);
        if (string.IsNullOrWhiteSpace(taskName))
        {
            taskName = ServerUpdateLayout.WindowsTaskName;
        }

        if (string.IsNullOrWhiteSpace(updateUnit))
        {
            updateUnit = ServerUpdateLayout.LinuxUpdateUnitName;
        }

        if (kind == ServerUpdateHostKind.Iis)
        {
            if (!IsSafeTaskName(appPoolName))
            {
                error = "AppPoolName is required for an IIS helper.";
                return false;
            }
        }
        else if (!IsSafeToken(serviceName))
        {
            error = "ServiceName is required.";
            return false;
        }

        if (kind == ServerUpdateHostKind.Linux)
        {
            if (!IsUnitName(updateUnit))
            {
                error = "UpdateUnitName must be a systemd unit name.";
                return false;
            }
        }
        else if (!IsSafeTaskName(taskName))
        {
            error = "TaskName contains characters the helper will not pass to Task Scheduler.";
            return false;
        }

        var serviceUser = Value(pairs, ServiceUserKey);
        var serviceGroup = Value(pairs, ServiceGroupKey);
        if ((!string.IsNullOrEmpty(serviceUser) && !IsSafeToken(serviceUser)) ||
            (!string.IsNullOrEmpty(serviceGroup) && !IsSafeToken(serviceGroup)))
        {
            error = "ServiceUser and ServiceGroup must be simple account names.";
            return false;
        }

        var healthUrl = Value(pairs, HealthUrlKey);
        if (!string.IsNullOrWhiteSpace(healthUrl) &&
            (!Uri.TryCreate(healthUrl, UriKind.Absolute, out var health) ||
             health.Scheme is not "http" and not "https"))
        {
            error = "HealthUrl must be an absolute http or https URL.";
            return false;
        }

        config = new ServerUpdateHelperConfig
        {
            HostKind = kind,
            InstallPath = Path.GetFullPath(installPath),
            ServiceName = serviceName,
            AppPoolName = appPoolName,
            StagingPath = Path.GetFullPath(stagingPath),
            HealthUrl = healthUrl,
            ServiceUser = serviceUser,
            ServiceGroup = serviceGroup,
            TaskName = taskName,
            UpdateUnitName = updateUnit
        };
        error = string.Empty;
        return true;
    }

    public static string FormatState(IReadOnlyDictionary<string, string> pairs)
    {
        var lines = new List<string>(pairs.Count);
        foreach (var pair in pairs)
        {
            if (!IsStateKey(pair.Key) || pair.Value.Contains('\n') || pair.Value.Contains('\r'))
            {
                throw new ArgumentException($"State key '{pair.Key}' is not safe to write.");
            }

            lines.Add(Pair(pair.Key, pair.Value));
        }

        return string.Join('\n', lines) + "\n";
    }

    public static bool TryParseState(string text, out IReadOnlyDictionary<string, string> pairs, out string error)
    {
        if (!TryReadPairs(text, out var parsed, out error))
        {
            pairs = new Dictionary<string, string>();
            return false;
        }

        pairs = parsed;
        return true;
    }

    public static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(NormalizePath(left), NormalizePath(right), comparison);
    }

    public static string InstallHint(ServerUpdateHostKind kind, string contentRoot, string? appPoolName)
    {
        var quotedRoot = Quote(contentRoot);
        return kind switch
        {
            ServerUpdateHostKind.Iis =>
                $"Install-ApiUpdateHelper.ps1 -InstallPath {quotedRoot} -AppPool {Quote(appPoolName ?? "APP_POOL")}",
            ServerUpdateHostKind.Linux =>
                $"sudo bash ./install-api-update-helper.sh --install-path {quotedRoot}",
            _ => $"Install-ApiUpdateHelper.ps1 -InstallPath {quotedRoot}"
        };
    }

    private static bool TryReadPairs(string text, out Dictionary<string, string> pairs, out string error)
    {
        pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        error = string.Empty;
        using var reader = new StringReader(text ?? string.Empty);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            var split = trimmed.IndexOf('=');
            if (split <= 0)
            {
                error = "Each helper setting must be Key=Value.";
                return false;
            }

            var key = trimmed[..split].Trim();
            var value = trimmed[(split + 1)..].Trim();
            if (!IsKnownKey(key))
            {
                error = $"Unknown helper setting '{key}'.";
                return false;
            }

            pairs[key] = value;
        }

        return true;
    }

    private static bool IsKnownKey(string key) => key is
        HostKindKey or InstallPathKey or ServiceNameKey or AppPoolNameKey or StagingPathKey or
        HealthUrlKey or ServiceUserKey or ServiceGroupKey or TaskNameKey or UpdateUnitNameKey or
        VersionKey or Sha256Key or StatusKey or MessageKey;

    private static bool IsStateKey(string key) => key is VersionKey or Sha256Key or StatusKey or MessageKey;

    private static string Value(Dictionary<string, string> pairs, string key) =>
        pairs.TryGetValue(key, out var value) ? value : string.Empty;

    private static string Pair(string key, string value) => $"{key}={value}";

    private static bool IsSafeAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\n') || path.Contains('\r'))
        {
            return false;
        }

        if (!Path.IsPathRooted(path))
        {
            return false;
        }

        var full = Path.GetFullPath(path);
        return !full.Contains(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !full.EndsWith("..", StringComparison.Ordinal);
    }

    private static bool PathsNest(string installPath, string stagingPath)
    {
        var install = NormalizePath(installPath) + Path.DirectorySeparatorChar;
        var staging = NormalizePath(stagingPath) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return staging.StartsWith(install, comparison) || install.StartsWith(staging, comparison);
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsSafeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSafeTaskName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ' ' and not '.' and not '-' and not '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsUnitName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80 || !value.EndsWith(".service", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_')
            {
                return false;
            }
        }

        return true;
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "", StringComparison.Ordinal) + "\"";
}
