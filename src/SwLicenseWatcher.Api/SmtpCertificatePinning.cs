using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SwLicenseWatcher.Api;

internal static class SmtpCertificatePinning
{
    private const int MaxThumbprintLength = 128;

    internal static string NormalizeThumbprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return NormalizeThumbprint(value.AsSpan().Trim());
    }

    internal static string NormalizeThumbprint(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty || text.Length > MaxThumbprintLength)
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[text.Length];
        var pos = 0;
        foreach (var c in text)
        {
            if (c is ' ' or ':' or '-')
            {
                continue;
            }

            buffer[pos++] = char.ToUpperInvariant(c);
        }

        return pos == 0 ? string.Empty : new string(buffer[..pos]);
    }

    internal static HashSet<string> CreateAllowedSet(IEnumerable<string>? thumbprints)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (thumbprints is null)
        {
            return allowed;
        }

        foreach (var thumbprint in thumbprints)
        {
            var normalized = NormalizeThumbprint(thumbprint);
            if (normalized.Length > 0)
            {
                allowed.Add(normalized);
            }
        }

        return allowed;
    }

    internal static bool HasAllowedThumbprints(IEnumerable<string>? thumbprints)
    {
        if (thumbprints is null)
        {
            return false;
        }

        foreach (var thumbprint in thumbprints)
        {
            if (NormalizeThumbprint(thumbprint).Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool TryGetThumbprints(X509Certificate? certificate, out string sha1, out string sha256)
    {
        sha1 = string.Empty;
        sha256 = string.Empty;
        if (certificate is null)
        {
            return false;
        }

        X509Certificate2? owned = null;
        var cert2 = certificate as X509Certificate2 ?? (owned = new X509Certificate2(certificate));
        try
        {
            sha1 = NormalizeThumbprint(cert2.Thumbprint);
            sha256 = NormalizeThumbprint(cert2.GetCertHashString(HashAlgorithmName.SHA256));
            return sha1.Length > 0 || sha256.Length > 0;
        }
        finally
        {
            owned?.Dispose();
        }
    }

    internal static bool IsPinned(string sha1, string sha256, IReadOnlySet<string> allowedThumbprints) =>
        (sha1.Length > 0 && allowedThumbprints.Contains(sha1))
        || (sha256.Length > 0 && allowedThumbprints.Contains(sha256));

    internal static bool Accept(
        X509Certificate? certificate,
        SslPolicyErrors sslPolicyErrors,
        IReadOnlySet<string> allowedThumbprints)
    {
        if (TryGetThumbprints(certificate, out var sha1, out var sha256)
            && IsPinned(sha1, sha256, allowedThumbprints))
        {
            return true;
        }

        return sslPolicyErrors == SslPolicyErrors.None;
    }
}
