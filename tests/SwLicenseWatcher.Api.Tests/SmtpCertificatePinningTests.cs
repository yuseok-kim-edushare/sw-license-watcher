using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SwLicenseWatcher.Api;

namespace SwLicenseWatcher.Api.Tests;

public class SmtpCertificatePinningTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("aa:bb-cc dd", "AABBCCDD")]
    [InlineData("  AB:CD-EF  ", "ABCDEF")]
    public void NormalizeThumbprint_strips_separators_and_uppercases(string? value, string expected)
    {
        Assert.Equal(expected, SmtpCertificatePinning.NormalizeThumbprint(value));
    }

    [Fact]
    public void NormalizeThumbprint_rejects_overlong_input()
    {
        Assert.Equal(string.Empty, SmtpCertificatePinning.NormalizeThumbprint(new string('A', 129)));
    }

    [Fact]
    public void CreateAllowedSet_ignores_blank_entries()
    {
        var allowed = SmtpCertificatePinning.CreateAllowedSet(["", "  ", "ab:cd", null!]);

        Assert.Equal("ABCD", Assert.Single(allowed));
        Assert.False(SmtpCertificatePinning.HasAllowedThumbprints([]));
        Assert.False(SmtpCertificatePinning.HasAllowedThumbprints(["", " "]));
        Assert.True(SmtpCertificatePinning.HasAllowedThumbprints(["ab-cd"]));
    }

    [Fact]
    public void Accept_allows_system_trusted_certificates_without_a_pin()
    {
        using var cert = CreateSelfSigned();

        Assert.True(SmtpCertificatePinning.Accept(cert, SslPolicyErrors.None, SmtpCertificatePinning.CreateAllowedSet([])));
    }

    [Fact]
    public void Accept_rejects_untrusted_certificates_that_are_not_pinned()
    {
        using var cert = CreateSelfSigned();
        var allowed = SmtpCertificatePinning.CreateAllowedSet(["DEADBEEF"]);

        Assert.False(SmtpCertificatePinning.Accept(cert, SslPolicyErrors.RemoteCertificateChainErrors, allowed));
    }

    [Fact]
    public void Accept_allows_a_private_ca_certificate_when_the_sha1_thumbprint_is_pinned()
    {
        using var cert = CreateSelfSigned();
        var allowed = SmtpCertificatePinning.CreateAllowedSet([InsertColons(cert.Thumbprint)]);

        Assert.True(SmtpCertificatePinning.Accept(cert, SslPolicyErrors.RemoteCertificateChainErrors, allowed));
    }

    [Fact]
    public void Accept_allows_a_private_ca_certificate_when_the_sha256_thumbprint_is_pinned()
    {
        using var cert = CreateSelfSigned();
        var sha256 = cert.GetCertHashString(HashAlgorithmName.SHA256);
        var allowed = SmtpCertificatePinning.CreateAllowedSet([InsertColons(sha256).ToLowerInvariant()]);

        Assert.True(SmtpCertificatePinning.Accept(cert, SslPolicyErrors.RemoteCertificateNameMismatch, allowed));
    }

    private static X509Certificate2 CreateSelfSigned()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=smtp.contoso.local", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static string InsertColons(string hex)
    {
        var chars = hex.Where(char.IsLetterOrDigit).ToArray();
        return string.Join(':', Enumerable.Range(0, chars.Length / 2).Select(i => $"{chars[i * 2]}{chars[(i * 2) + 1]}"));
    }
}
