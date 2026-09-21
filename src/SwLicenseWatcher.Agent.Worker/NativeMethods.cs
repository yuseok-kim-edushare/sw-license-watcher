using System.Runtime.InteropServices;

namespace SwLicenseWatcher.Agent.Worker;

internal static class NativeMethods
{
    internal const uint CreateUnicodeEnvironment = 0x00000400;
    internal const uint CreateNoWindow = 0x08000000;
    internal const uint GenericAll = 0x10000000;
    internal const int TokenPrimary = 1;
    internal const int SecurityImpersonation = 2;
    internal const int WtsActive = 0;
    internal const uint WaitObject0 = 0;
    internal const uint WaitTimeout = 0x00000102;

    [DllImport("wtsapi32.dll", SetLastError = true)]
    internal static extern bool WTSEnumerateSessions(
        nint hServer,
        int reserved,
        int version,
        out nint ppSessionInfo,
        out int pCount);

    [DllImport("wtsapi32.dll")]
    internal static extern void WTSFreeMemory(nint memory);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    internal static extern bool WTSQueryUserToken(uint sessionId, out nint phToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool DuplicateTokenEx(
        nint hExistingToken,
        uint dwDesiredAccess,
        nint lpTokenAttributes,
        int impersonationLevel,
        int tokenType,
        out nint phNewToken);

    [DllImport("userenv.dll", SetLastError = true)]
    internal static extern bool CreateEnvironmentBlock(out nint lpEnvironment, nint hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    internal static extern bool DestroyEnvironmentBlock(nint lpEnvironment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool CreateProcessAsUser(
        nint hToken,
        string lpApplicationName,
        string lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        nint lpEnvironment,
        string lpCurrentDirectory,
        ref StartupInfo lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetExitCodeProcess(nint hProcess, out uint lpExitCode);

    internal const uint DaclSecurityInformation = 0x00000004;
    internal const uint ProtectedDaclSecurityInformation = 0x80000000;
    internal const uint SddlRevision1 = 1;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
        string stringSecurityDescriptor,
        uint stringSdRevision,
        out nint securityDescriptor,
        nint securityDescriptorSize);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SetFileSecurity(
        string fileName,
        uint securityInformation,
        nint securityDescriptor);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint LocalFree(nint handle);

    [StructLayout(LayoutKind.Sequential)]
    internal struct WtsSessionInfo
    {
        internal uint SessionId;
        internal nint pWinStationName;
        internal int State;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct StartupInfo
    {
        internal int cb;
        internal string? lpReserved;
        internal string? lpDesktop;
        internal string? lpTitle;
        internal int dwX;
        internal int dwY;
        internal int dwXSize;
        internal int dwYSize;
        internal int dwXCountChars;
        internal int dwYCountChars;
        internal int dwFillAttribute;
        internal int dwFlags;
        internal short wShowWindow;
        internal short cbReserved2;
        internal nint lpReserved2;
        internal nint hStdInput;
        internal nint hStdOutput;
        internal nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInformation
    {
        internal nint hProcess;
        internal nint hThread;
        internal int dwProcessId;
        internal int dwThreadId;
    }
}
