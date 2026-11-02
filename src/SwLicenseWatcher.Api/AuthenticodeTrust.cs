using System.Runtime.InteropServices;

namespace SwLicenseWatcher.Api;

internal static class AuthenticodeTrust
{
    private static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public static void RequireTrusted(IReadOnlyList<string> files)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new UpdatePackageSignException("Authenticode verification requires a Windows API host.");
        }

        if (files.Count == 0)
        {
            throw new UpdatePackageSignException("The package contains no EXE or DLL to verify.");
        }

        foreach (var file in files)
        {
            if (!IsTrusted(file))
            {
                throw new UpdatePackageSignException($"Authenticode verification failed for {Path.GetFileName(file)}.");
            }
        }
    }

    private static bool IsTrusted(string path)
    {
        using var fileInfo = new WinTrustFileInfo(path);
        using var trustData = new WinTrustData(fileInfo);
        return WinVerifyTrust(IntPtr.Zero, ActionGenericVerifyV2, trustData) == 0;
    }

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern int WinVerifyTrust(
        IntPtr hwnd,
        [In, MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
        [In] WinTrustData trustData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustFileInfo : IDisposable
    {
        private readonly uint StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
        private IntPtr FilePath;
        private readonly IntPtr FileHandle = IntPtr.Zero;
        private readonly IntPtr KnownSubject = IntPtr.Zero;

        public WinTrustFileInfo(string path) => FilePath = Marshal.StringToCoTaskMemUni(path);

        public void Dispose() => Marshal.FreeCoTaskMem(FilePath);
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class WinTrustData : IDisposable
    {
        private readonly uint StructSize = (uint)Marshal.SizeOf<WinTrustData>();
        private readonly IntPtr PolicyCallbackData = IntPtr.Zero;
        private readonly IntPtr SipClientData = IntPtr.Zero;
        private readonly uint UiChoice = 2;
        private readonly uint RevocationChecks = 1;
        private readonly uint UnionChoice = 1;
        private IntPtr FileInfo;
        private readonly uint StateAction = 0;
        private readonly IntPtr StateData = IntPtr.Zero;
        private readonly IntPtr UrlReference = IntPtr.Zero;
        private readonly uint ProviderFlags = 0x00000080;
        private readonly uint UiContext = 0;

        public WinTrustData(WinTrustFileInfo fileInfo)
        {
            FileInfo = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, FileInfo, false);
        }

        public void Dispose() => Marshal.FreeCoTaskMem(FileInfo);
    }
}
