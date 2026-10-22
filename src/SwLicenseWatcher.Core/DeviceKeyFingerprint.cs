using System.Security.Cryptography;
using System.Text;

namespace SwLicenseWatcher.Core;

public static class DeviceKeyFingerprint
{
    public static string? Sha256Hex(string? publicKey)
    {
        if (string.IsNullOrWhiteSpace(publicKey))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(publicKey.Trim()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
