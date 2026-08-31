using System.Security.Cryptography;
using System.Reflection;
using System.Text;

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
        EnsureWindows();
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var entropyBytes = Encoding.UTF8.GetBytes(_options.InstanceName);
        var protectedBytes = InvokeProtectedData("Protect", plaintextBytes, entropyBytes);
        return Convert.ToBase64String(protectedBytes);
    }

    public string Unprotect(string protectedPayload)
    {
        EnsureWindows();
        var payloadBytes = Convert.FromBase64String(protectedPayload);
        var entropyBytes = Encoding.UTF8.GetBytes(_options.InstanceName);
        var plaintextBytes = InvokeProtectedData("Unprotect", payloadBytes, entropyBytes);
        return Encoding.UTF8.GetString(plaintextBytes);
    }

    private byte[] InvokeProtectedData(string methodName, byte[] payload, byte[] entropy)
    {
        var protectedDataType = Type.GetType("System.Security.Cryptography.ProtectedData, System.Security.Cryptography.ProtectedData", throwOnError: true)!;
        var scopeType = Type.GetType("System.Security.Cryptography.DataProtectionScope, System.Security.Cryptography.ProtectedData", throwOnError: true)!;
        var scopeValue = Enum.Parse(
            scopeType,
            string.Equals(_options.DpapiScope, "CurrentUser", StringComparison.OrdinalIgnoreCase)
                ? "CurrentUser"
                : "LocalMachine",
            ignoreCase: true);

        var method = protectedDataType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, [typeof(byte[]), typeof(byte[]), scopeType]);
        if (method is null)
        {
            throw new MissingMethodException(protectedDataType.FullName, methodName);
        }

        return (byte[])(method.Invoke(null, [payload, entropy, scopeValue]) ?? throw new InvalidOperationException($"DPAPI {methodName} returned null."));
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI protection is only available on Windows.");
        }
    }
}
