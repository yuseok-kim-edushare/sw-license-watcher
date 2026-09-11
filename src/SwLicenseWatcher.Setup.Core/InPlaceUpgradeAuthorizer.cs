namespace SwLicenseWatcher.Setup.Core;

public sealed class InPlaceUpgradeAuthorizer
{
    private readonly IUpgradeAuthorizationClient _api;
    private readonly IUpgradeProofFactory _proofs;
    private readonly InstalledDeviceIdentityStore _identityStore;

    public InPlaceUpgradeAuthorizer(
        IUpgradeAuthorizationClient api,
        IUpgradeProofFactory proofs,
        InstalledDeviceIdentityStore? identityStore = null)
    {
        _api = api;
        _proofs = proofs;
        _identityStore = identityStore ?? new InstalledDeviceIdentityStore();
    }

    public async Task AuthorizeAsync(
        ExistingAgentInstallation existing,
        string deviceCode,
        CancellationToken cancellationToken)
    {
        if (!existing.IsPresent ||
            !InPlaceUpgradePolicy.RequiresServerKeyAuthorization(existing.InstalledVersion))
        {
            return;
        }

        var identity = _identityStore.Read();
        if (!_proofs.TryCreate(identity, deviceCode, out var request, out var error))
        {
            throw new InvalidOperationException(error);
        }

        var keys = await _api.AuthorizeAsync(request, cancellationToken);
        _identityStore.ApplyServerKeys(keys);
    }
}
