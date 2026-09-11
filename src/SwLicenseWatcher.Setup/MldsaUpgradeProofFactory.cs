using SwLicenseWatcher.Core;
using SwLicenseWatcher.Crypto;
using SwLicenseWatcher.Setup.Core;

namespace SwLicenseWatcher.Setup;

internal sealed class MldsaUpgradeProofFactory : IUpgradeProofFactory
{
    public bool TryCreate(
        StoredLocalDeviceIdentity identity,
        string deviceCode,
        out UpgradeAuthorizationRequest request,
        out string error)
    {
        request = new UpgradeAuthorizationRequest();
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(identity.PublicKey) || string.IsNullOrWhiteSpace(identity.PrivateKey))
        {
            error = "0.1.0 이후 설치본은 서버 장치 키 확인이 필요합니다. 로컬 장치 키를 읽지 못했습니다.";
            return false;
        }

        if (!MldsaDeviceCrypto.TryFromBase64(identity.PrivateKey, out var privateKey))
        {
            error = "로컬 장치 개인키를 읽지 못했습니다.";
            return false;
        }

        request = new UpgradeAuthorizationRequest
        {
            DeviceCode = deviceCode,
            DeviceId = identity.DeviceId,
            DevicePublicKey = identity.PublicKey,
            DeviceCertificate = identity.Certificate,
            DeviceProof = MldsaDeviceCrypto.ToBase64(
                MldsaDeviceCrypto.Sign(privateKey, DeviceProofs.Payload(identity.DeviceId ?? "", deviceCode)))
        };
        return true;
    }
}
