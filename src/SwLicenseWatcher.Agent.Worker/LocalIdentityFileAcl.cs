namespace SwLicenseWatcher.Agent.Worker;

internal static class LocalIdentityFileAcl
{
    private const string AdministratorsAndSystem =
        "D:P(A;;FA;;;SY)(A;;FA;;;BA)";

    private const string AdministratorsSystemAndUsersRead =
        "D:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;BU)";

    internal static void RestrictPrivateKey(string path) =>
        Apply(path, AdministratorsAndSystem);

    internal static void AllowUsersRead(string path) =>
        Apply(path, AdministratorsSystemAndUsersRead);

    private static void Apply(string path, string sddl)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path))
        {
            return;
        }

        if (!NativeMethods.ConvertStringSecurityDescriptorToSecurityDescriptor(
                sddl,
                NativeMethods.SddlRevision1,
                out var descriptor,
                0))
        {
            return;
        }

        try
        {
            NativeMethods.SetFileSecurity(
                path,
                NativeMethods.DaclSecurityInformation | NativeMethods.ProtectedDaclSecurityInformation,
                descriptor);
        }
        finally
        {
            NativeMethods.LocalFree(descriptor);
        }
    }
}
