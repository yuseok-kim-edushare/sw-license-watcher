using SwLicenseWatcher.Core;
using SwLicenseWatcher.Crypto;

namespace SwLicenseWatcher.Crypto.Tests;

public class MldsaDeviceCryptoTests
{
    [Fact]
    public void Issue_and_verify_an_ml_dsa_87_device_certificate()
    {
        var (caPublic, caPrivate) = MldsaDeviceCrypto.GenerateKeyPair();
        var (devicePublic, devicePrivate) = MldsaDeviceCrypto.GenerateKeyPair();
        var deviceId = Guid.NewGuid().ToString("D");
        var publicKey = MldsaDeviceCrypto.ToBase64(devicePublic);

        var certificate = MldsaDeviceCrypto.Issue(caPrivate, deviceId, publicKey);
        Assert.Equal(DeviceIdentityAlgorithms.Mldsa87, certificate.Alg);
        Assert.True(MldsaDeviceCrypto.TryVerifyCertificate(caPublic, certificate, out var error));
        Assert.Equal(string.Empty, error);

        var proof = MldsaDeviceCrypto.Sign(devicePrivate, DeviceProofs.Payload(deviceId, "FIELD-1"));
        Assert.True(MldsaDeviceCrypto.Verify(devicePublic, DeviceProofs.Payload(deviceId, "FIELD-1"), proof));
        Assert.False(MldsaDeviceCrypto.Verify(devicePublic, DeviceProofs.Payload(deviceId, "OTHER"), proof));

        var tampered = certificate with { Pk = MldsaDeviceCrypto.ToBase64(devicePrivate) };
        Assert.False(MldsaDeviceCrypto.TryVerifyCertificate(caPublic, tampered, out _));
    }
}
