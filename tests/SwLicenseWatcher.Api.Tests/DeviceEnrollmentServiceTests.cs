using SwLicenseWatcher.Api;
using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;
using SwLicenseWatcher.Crypto;

namespace SwLicenseWatcher.Api.Tests;

public class DeviceEnrollmentServiceTests
{
    [Fact]
    public async Task AuthorizeUpgrade_requires_a_device_proof()
    {
        var sut = new DeviceEnrollmentService(new StubDevices(), new AcceptingAuthority());

        var (response, error) = await sut.AuthorizeUpgradeAsync(
            new DeviceUpgradeAuthorizationRequest("ASSET-1"),
            CancellationToken.None);

        Assert.Null(response);
        Assert.Equal("The device proof is required.", error);
    }

    [Fact]
    public async Task AuthorizeUpgrade_returns_not_found_when_the_device_is_unknown()
    {
        var sut = new DeviceEnrollmentService(new StubDevices(), new AcceptingAuthority());

        var (response, error) = await sut.AuthorizeUpgradeAsync(
            new DeviceUpgradeAuthorizationRequest("ASSET-1", DeviceProof: "proof"),
            CancellationToken.None);

        Assert.Null(response);
        Assert.Null(error);
    }

    [Fact]
    public async Task AuthorizeUpgrade_returns_stored_keys_when_the_proof_matches()
    {
        var (publicKey, privateKey) = MldsaDeviceCrypto.GenerateKeyPair();
        var publicKeyText = MldsaDeviceCrypto.ToBase64(publicKey);
        var proof = MldsaDeviceCrypto.ToBase64(
            MldsaDeviceCrypto.Sign(privateKey, DeviceProofs.Payload("id-1", "ASSET-1")));
        var devices = new StubDevices
        {
            Keys = new DeviceEnrollmentKeys("ASSET-1", null, "id-1", publicKeyText, null)
        };
        var sut = new DeviceEnrollmentService(devices, new AcceptingAuthority());

        var (response, error) = await sut.AuthorizeUpgradeAsync(
            new DeviceUpgradeAuthorizationRequest(
                "ASSET-1",
                "id-1",
                publicKeyText,
                DeviceProof: proof),
            CancellationToken.None);

        Assert.Null(error);
        Assert.NotNull(response);
        Assert.Equal("ASSET-1", response.DeviceCode);
        Assert.Equal("id-1", response.DeviceId);
        Assert.Equal(publicKeyText, response.DevicePublicKey);
        Assert.Null(response.DeviceCertificate);
    }

    private sealed class StubDevices : IDeviceQuery
    {
        public DeviceEnrollmentKeys? Keys { get; set; }

        public Task<(int TotalCount, List<DeviceSummary> Items)> ListDevicesAsync(
            int skip, int take, string? search, int? staleAfterHours, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DeviceDetail?> GetDeviceAsync(
            string deviceCode, string? classification, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DeviceAgentAssignment?> GetDeviceAssignmentAsync(
            string deviceCode, string? deviceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DeviceEnrollmentKeys?> GetDeviceEnrollmentKeysAsync(
            string deviceCode, string? deviceId, CancellationToken cancellationToken) =>
            Task.FromResult(Keys);

        public Task<DeviceProfileUpdateResult> UpdateDeviceProfileAsync(
            string deviceCode, DeviceProfileWriteRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task BindDeviceEnrollmentAsync(
            string deviceCode,
            string? deviceId,
            string devicePublicKey,
            string deviceCertificate,
            string issuedDeviceId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class AcceptingAuthority : IDeviceCertificateAuthority
    {
        public DeviceCertificateDocument Issue(string deviceId, string publicKeyBase64) =>
            throw new NotSupportedException();

        public bool TryVerify(DeviceCertificateDocument certificate, out string error)
        {
            error = string.Empty;
            return true;
        }
    }
}
