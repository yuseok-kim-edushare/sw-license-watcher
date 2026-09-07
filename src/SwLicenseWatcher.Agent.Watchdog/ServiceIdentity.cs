namespace SwLicenseWatcher.Agent.Watchdog;

internal static class ServiceIdentity
{
    internal const string WatchdogMustBeLocalSystem =
        "Watchdog must run as Local System (NT AUTHORITY\\SYSTEM) so it can stop, replace, and start the Worker service. Administrator is only required to register the service, not as the service logon account.";

    internal static bool IsDisallowedServiceAccount(bool isWindowsService, bool isLocalSystem) =>
        isWindowsService && !isLocalSystem;
}
