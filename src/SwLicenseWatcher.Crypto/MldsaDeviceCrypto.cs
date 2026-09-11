using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Crypto;

public static class MldsaDeviceCrypto
{
    // ML-DSA-87 is the FIPS 204 top tier. Keygen/sign/verify are slower than 44/65; that is accepted.
    public static MLDsaParameters Parameters { get; } = MLDsaParameters.ml_dsa_87;

    public static (byte[] PublicKey, byte[] PrivateKey) GenerateKeyPair()
    {
        var generator = new MLDsaKeyPairGenerator();
        generator.Init(new MLDsaKeyGenerationParameters(new SecureRandom(), Parameters));
        var pair = generator.GenerateKeyPair();
        var publicKey = (MLDsaPublicKeyParameters)pair.Public;
        var privateKey = (MLDsaPrivateKeyParameters)pair.Private;
        return (publicKey.GetEncoded(), privateKey.GetEncoded());
    }

    public static byte[] Sign(byte[] privateKey, byte[] data)
    {
        var signer = new MLDsaSigner(Parameters, deterministic: true);
        signer.Init(true, DecodePrivateKey(privateKey));
        signer.BlockUpdate(data, 0, data.Length);
        return signer.GenerateSignature();
    }

    public static bool Verify(byte[] publicKey, byte[] data, byte[] signature)
    {
        var verifier = new MLDsaSigner(Parameters, deterministic: true);
        verifier.Init(false, DecodePublicKey(publicKey));
        verifier.BlockUpdate(data, 0, data.Length);
        return verifier.VerifySignature(signature);
    }

    public static string ToBase64(byte[] value) => Convert.ToBase64String(value);

    public static bool TryFromBase64(string? value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(value.Trim());
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static DeviceCertificateDocument Issue(byte[] caPrivateKey, string deviceId, string publicKeyBase64)
    {
        var issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = Sign(caPrivateKey, CanonicalBytes(deviceId, publicKeyBase64, issuedAt));
        return new DeviceCertificateDocument(1, DeviceIdentityAlgorithms.Mldsa87, deviceId, publicKeyBase64, issuedAt, ToBase64(signature));
    }

    public static bool TryVerifyCertificate(byte[] caPublicKey, DeviceCertificateDocument certificate, out string error)
    {
        if (certificate.V != 1 ||
            !string.Equals(certificate.Alg, DeviceIdentityAlgorithms.Mldsa87, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(certificate.Id) ||
            string.IsNullOrWhiteSpace(certificate.Pk) ||
            !TryFromBase64(certificate.Sig, out var signature))
        {
            error = "The device certificate is malformed.";
            return false;
        }

        if (!Verify(caPublicKey, CanonicalBytes(certificate.Id, certificate.Pk, certificate.Iat), signature))
        {
            error = "The device certificate signature is invalid.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static byte[] CanonicalBytes(string deviceId, string publicKeyBase64, long issuedAt) =>
        Encoding.UTF8.GetBytes($"1\n{DeviceIdentityAlgorithms.Mldsa87}\n{deviceId}\n{publicKeyBase64}\n{issuedAt}\n");

    private static MLDsaPublicKeyParameters DecodePublicKey(byte[] encoded) =>
        MLDsaPublicKeyParameters.FromEncoding(Parameters, encoded);

    private static MLDsaPrivateKeyParameters DecodePrivateKey(byte[] encoded) =>
        MLDsaPrivateKeyParameters.FromEncoding(Parameters, encoded);
}
