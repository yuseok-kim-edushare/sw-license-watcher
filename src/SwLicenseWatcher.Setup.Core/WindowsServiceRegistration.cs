namespace SwLicenseWatcher.Setup.Core;

/// <summary>
/// SCM registration for agent and API services.
/// The installer process may be an elevated administrator, but the service
/// logon must be Local System. Watchdog in particular has to stop, replace,
/// and start Worker; a user account (even a local admin) is the wrong identity.
/// </summary>
public static class WindowsServiceRegistration
{
    public const string LocalSystemAccount = "LocalSystem";

    public static string[] Create(string name, string quotedBinPath, string displayName) =>
    [
        "create", name,
        "binPath=", quotedBinPath,
        "start=", "auto",
        "obj=", LocalSystemAccount,
        "DisplayName=", displayName
    ];

    public static string[] Config(string name, string quotedBinPath, string displayName) =>
    [
        "config", name,
        "binPath=", quotedBinPath,
        "start=", "auto",
        "obj=", LocalSystemAccount,
        "DisplayName=", displayName
    ];

    public static string[] Description(string name, string description) =>
        ["description", name, description];

    public static string[] FailureRestart(string name) =>
        ["failure", name, "reset=", "86400", "actions=", "restart/5000/restart/30000/restart/60000"];

    public static string[] FailureFlagOff(string name) =>
        ["failureflag", name, "0"];

    public static string[] QueryProcess(string name) =>
        ["queryex", name];

    public static string[] FailureFlag(string name) =>
        ["failureflag", name, "1"];

    public static string[] Delete(string name) =>
        ["delete", name];
}

public static class WindowsServiceProcessQuery
{
    public static bool TryReadProcessId(string queryOutput, out int processId)
    {
        processId = 0;
        if (string.IsNullOrWhiteSpace(queryOutput))
        {
            return false;
        }

        foreach (var line in queryOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }

            var label = line[..separator].Trim();
            if (!label.Equals("PID", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return int.TryParse(line[(separator + 1)..].Trim(), out processId) && processId > 0;
        }

        return false;
    }
}
