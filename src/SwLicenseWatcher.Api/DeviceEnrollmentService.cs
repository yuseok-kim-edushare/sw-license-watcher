using System.Text.Json;
using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;
using SwLicenseWatcher.Crypto;

namespace SwLicenseWatcher.Api;

internal sealed class DeviceEnrollmentService(
    IDeviceQuery devices,
    IDeviceCertificateAuthority authority)
{
    public bool TryAccept(string? deviceId, string? publicKey, string? certificateJson, string? proof, string deviceCode, out string error)
    {
        error = string.Empty;
        DeviceCertificateDocument? certificate = null;
        if (!string.IsNullOrWhiteSpace(certificateJson))
        {
            try
            {
                certificate = JsonSerializer.Deserialize(certificateJson, InventoryJsonSerializerContext.Default.DeviceCertificateDocument);
            }
            catch (JsonException)
            {
                error = "The device certificate is malformed.";
                return false;
            }

            if (certificate is null || !authority.TryVerify(certificate, out error))
            {
                error = string.IsNullOrEmpty(error) ? "The device certificate is invalid." : error;
                return false;
            }

            if (!string.IsNullOrWhiteSpace(deviceId) &&
                !string.Equals(deviceId, certificate.Id, StringComparison.Ordinal))
            {
                error = "The device id does not match the certificate.";
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(proof))
        {
            return true;
        }

        var key = publicKey;
        if (string.IsNullOrWhiteSpace(key) && certificate is not null)
        {
            key = certificate.Pk;
        }

        if (!MldsaDeviceCrypto.TryFromBase64(key, out var publicBytes) ||
            !MldsaDeviceCrypto.TryFromBase64(proof, out var proofBytes))
        {
            error = "The device proof is malformed.";
            return false;
        }

        var id = deviceId ?? certificate?.Id ?? "";
        if (!MldsaDeviceCrypto.Verify(publicBytes, DeviceProofs.Payload(id, deviceCode), proofBytes))
        {
            error = "The device proof is invalid.";
            return false;
        }

        return true;
    }

    public async Task<DeviceAgentAssignment?> CompleteAsync(
        string deviceCode,
        string? deviceId,
        string? publicKey,
        CancellationToken cancellationToken)
    {
        var assignment = await devices.GetDeviceAssignmentAsync(deviceCode, deviceId, cancellationToken);
        if (assignment is null)
        {
            return null;
        }

        if (assignment.DeviceId is null && !string.IsNullOrWhiteSpace(publicKey))
        {
            var issuedId = Guid.NewGuid().ToString("D");
            var certificate = authority.Issue(issuedId, publicKey);
            var json = JsonSerializer.Serialize(certificate, InventoryJsonSerializerContext.Default.DeviceCertificateDocument);
            await devices.BindDeviceEnrollmentAsync(deviceCode, deviceId, publicKey, json, issuedId, cancellationToken);
            return await devices.GetDeviceAssignmentAsync(deviceCode, issuedId, cancellationToken);
        }

        return assignment;
    }

    public async Task<(DeviceUpgradeAuthorizationResponse? Response, string? Error)> AuthorizeUpgradeAsync(
        DeviceUpgradeAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceProof))
        {
            return (null, "The device proof is required.");
        }

        var keys = await devices.GetDeviceEnrollmentKeysAsync(
            request.DeviceCode,
            request.DeviceId,
            cancellationToken);
        if (keys is null)
        {
            return (null, null);
        }

        if (!string.IsNullOrWhiteSpace(keys.DevicePublicKey) &&
            !string.IsNullOrWhiteSpace(request.DevicePublicKey) &&
            !string.Equals(keys.DevicePublicKey, request.DevicePublicKey, StringComparison.Ordinal))
        {
            return (null, "The device public key does not match the registered key.");
        }

        var publicKey = string.IsNullOrWhiteSpace(keys.DevicePublicKey)
            ? request.DevicePublicKey
            : keys.DevicePublicKey;
        var certificate = string.IsNullOrWhiteSpace(request.DeviceCertificate)
            ? keys.DeviceCertificate
            : request.DeviceCertificate;
        if (!TryAccept(request.DeviceId, publicKey, certificate, request.DeviceProof, request.DeviceCode, out var error))
        {
            return (null, error);
        }

        return (new DeviceUpgradeAuthorizationResponse(
            DeviceCodes.Official(keys.DeviceCode, keys.AssignedDeviceCode),
            keys.DeviceId,
            keys.DevicePublicKey,
            keys.DeviceCertificate), null);
    }
}
