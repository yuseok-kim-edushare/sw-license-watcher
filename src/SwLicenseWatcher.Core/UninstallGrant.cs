using System.Security.Cryptography;
using System.Text;

namespace SwLicenseWatcher.Core;

public static class UninstallGrant
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Consumed = "consumed";
    public const string Denied = "denied";
    public const string Expired = "expired";

    public const int CodeLength = 8;
    public static readonly TimeSpan ApprovalLifetime = TimeSpan.FromMinutes(15);

    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string CreateCode()
    {
        var characters = new char[CodeLength];
        var bytes = new byte[CodeLength];
        RandomNumberGenerator.Fill(bytes);
        for (var i = 0; i < CodeLength; i++)
        {
            characters[i] = CodeAlphabet[bytes[i] % CodeAlphabet.Length];
        }

        return new string(characters);
    }

    public static string HashCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        var normalized = NormalizeCode(code);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    public static bool CodeMatchesHash(string? suppliedCode, string? expectedHash)
    {
        if (string.IsNullOrWhiteSpace(suppliedCode) || string.IsNullOrWhiteSpace(expectedHash))
        {
            return false;
        }

        byte[] expectedBytes;
        try
        {
            expectedBytes = Convert.FromHexString(expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeCode(suppliedCode)));
        return expectedBytes.Length == suppliedHash.Length
            && CryptographicOperations.FixedTimeEquals(suppliedHash, expectedBytes);
    }

    public static string ResolveStatus(string storedStatus, DateTimeOffset? expiresAtUtc, DateTimeOffset utcNow)
    {
        if (string.Equals(storedStatus, Approved, StringComparison.OrdinalIgnoreCase)
            && expiresAtUtc is DateTimeOffset expires
            && expires <= utcNow)
        {
            return Expired;
        }

        return storedStatus;
    }

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
}
