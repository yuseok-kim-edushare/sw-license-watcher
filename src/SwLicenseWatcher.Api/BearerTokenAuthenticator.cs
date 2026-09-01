using System.Security.Cryptography;
using System.Text;

namespace SwLicenseWatcher.Api;

public static class BearerTokenAuthenticator
{
    public static bool IsAuthorized(string? suppliedAuthorizationHeader, string expectedToken)
    {
        var supplied = suppliedAuthorizationHeader ?? string.Empty;
        var expected = string.Concat("Bearer ", expectedToken);
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
    }
}
