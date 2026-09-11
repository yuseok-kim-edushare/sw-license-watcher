using System.Text.Json;
using SwLicenseWatcher.Core;
using SwLicenseWatcher.Setup.Core;

namespace SwLicenseWatcher.Setup.Tests;

public class InPlaceUpgradePolicyTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0.0.9", false)]
    [InlineData("0.0.15", false)]
    [InlineData("0.1.0", true)]
    [InlineData("0.1.0-preview", true)]
    [InlineData("1.2.3", true)]
    public void Requires_server_key_authorization_from_0_1_0(string? version, bool required)
    {
        Assert.Equal(required, InPlaceUpgradePolicy.RequiresServerKeyAuthorization(version));
    }
}

public class InPlaceUpgradeAuthorizerTests
{
    [Fact]
    public async Task Pre_0_1_0_skips_server_key_authorization()
    {
        var api = new RecordingUpgradeApi();
        var proofs = new RecordingProofFactory();
        var sut = new InPlaceUpgradeAuthorizer(
            api,
            proofs,
            new InstalledDeviceIdentityStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), new PassthroughProtector()));
        var existing = new ExistingAgentInstallation(
            true, true, true, "ASSET-1", null, "CORP", false, "0.0.15");

        await sut.AuthorizeAsync(existing, "ASSET-1", CancellationToken.None);

        Assert.Empty(api.Calls);
        Assert.Empty(proofs.Calls);
    }

    [Fact]
    public async Task Version_0_1_0_fetches_server_keys_without_uninstall_requests()
    {
        using var directory = new TempDirectory();
        var identityPath = SetupPaths.DeviceIdentityPath(directory.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(identityPath)!);
        var store = new InstalledDeviceIdentityStore(directory.Path, new PassthroughProtector());
        File.WriteAllText(
            identityPath,
            JsonSerializer.Serialize(
                new StoredDeviceIdentity("id-1", "pk", "sk", null),
                InventoryJsonSerializerContext.Default.StoredDeviceIdentity));
        var api = new RecordingUpgradeApi
        {
            Response = new UpgradeAuthorizationResponse
            {
                DeviceCode = "ASSET-1",
                DeviceId = "id-1",
                DevicePublicKey = "pk",
                DeviceCertificate = "cert-from-server"
            }
        };
        var proofs = new RecordingProofFactory();
        var sut = new InPlaceUpgradeAuthorizer(api, proofs, store);
        var existing = new ExistingAgentInstallation(
            true, true, true, "ASSET-1", null, "CORP", true, "0.1.0");

        await sut.AuthorizeAsync(existing, "ASSET-1", CancellationToken.None);

        Assert.Equal(["ASSET-1"], api.Calls);
        Assert.Equal(["ASSET-1"], proofs.Calls);
        var updated = store.Read();
        Assert.Equal("id-1", updated.DeviceId);
        Assert.Equal("cert-from-server", updated.Certificate);
        Assert.Equal("sk", updated.PrivateKey);
    }

    private sealed class RecordingUpgradeApi : IUpgradeAuthorizationClient
    {
        public List<string> Calls { get; } = [];

        public UpgradeAuthorizationResponse Response { get; set; } = new() { DeviceCode = "ASSET-1" };

        public Task<UpgradeAuthorizationResponse> AuthorizeAsync(
            UpgradeAuthorizationRequest request,
            CancellationToken cancellationToken)
        {
            Calls.Add(request.DeviceCode);
            return Task.FromResult(Response);
        }
    }

    private sealed class RecordingProofFactory : IUpgradeProofFactory
    {
        public List<string> Calls { get; } = [];

        public bool TryCreate(
            StoredLocalDeviceIdentity identity,
            string deviceCode,
            out UpgradeAuthorizationRequest request,
            out string error)
        {
            Calls.Add(deviceCode);
            request = new UpgradeAuthorizationRequest
            {
                DeviceCode = deviceCode,
                DeviceId = identity.DeviceId,
                DevicePublicKey = identity.PublicKey,
                DeviceProof = "proof"
            };
            error = string.Empty;
            return true;
        }
    }

    private sealed class PassthroughProtector : SwLicenseWatcher.Core.ILocalStateProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string protectedPayload) => protectedPayload;
    }
}
