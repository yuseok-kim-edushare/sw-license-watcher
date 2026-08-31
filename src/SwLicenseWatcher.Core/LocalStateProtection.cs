using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;

namespace SwLicenseWatcher.Core;

public interface ILocalStateProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedPayload);
}

public sealed class DpapiLocalStateProtector : ILocalStateProtector
{
    private readonly LocalStateStoreOptions _options;

    public DpapiLocalStateProtector(LocalStateStoreOptions options)
    {
        _options = options;
    }

    public string Protect(string plaintext)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI protection is only available on Windows.");
        }

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var entropyBytes = Encoding.UTF8.GetBytes(_options.InstanceName);
        var protectedBytes = ProtectedData.Protect(plaintextBytes, entropyBytes, ResolveScope());
        return Convert.ToBase64String(protectedBytes);
    }

    public string Unprotect(string protectedPayload)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI protection is only available on Windows.");
        }

        var payloadBytes = Convert.FromBase64String(protectedPayload);
        var entropyBytes = Encoding.UTF8.GetBytes(_options.InstanceName);
        var plaintextBytes = ProtectedData.Unprotect(payloadBytes, entropyBytes, ResolveScope());
        return Encoding.UTF8.GetString(plaintextBytes);
    }

    [SupportedOSPlatform("windows")]
    private DataProtectionScope ResolveScope() =>
        string.Equals(_options.DpapiScope, "CurrentUser", StringComparison.OrdinalIgnoreCase)
            ? DataProtectionScope.CurrentUser
            : DataProtectionScope.LocalMachine;
}
