using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SwLicenseWatcher.Agent.Watchdog;

public interface IUpdatePackageVerifier
{
    Task VerifyHashAsync(string path, string expected, CancellationToken cancellationToken);
    void VerifyAuthenticode(string directory);
}

public sealed class UpdatePackageVerifier : IUpdatePackageVerifier
{
    public async Task VerifyHashAsync(string path, string expected, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual),
                Convert.FromHexString(expected)))
        {
            throw new CryptographicException("The update package SHA-256 digest does not match the manifest.");
        }
    }

    public void VerifyAuthenticode(string directory)
    {
        var binaries = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (binaries.Length == 0)
        {
            throw new CryptographicException("The update package contains no signed binaries.");
        }

        foreach (var binary in binaries)
        {
            if (!AuthenticodeVerifier.IsTrusted(binary))
            {
                throw new CryptographicException($"Authenticode verification failed for {Path.GetFileName(binary)}.");
            }
        }
    }
}

internal static class AuthenticodeVerifier
{
    private static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public static bool IsTrusted(string path)
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
