namespace SwLicenseWatcher.Setup.Core;

public interface IUpgradeAuthorizationClient
{
    Task<UpgradeAuthorizationResponse> AuthorizeAsync(
        UpgradeAuthorizationRequest request,
        CancellationToken cancellationToken);
}

public interface IUpgradeProofFactory
{
    bool TryCreate(
        StoredLocalDeviceIdentity identity,
        string deviceCode,
        out UpgradeAuthorizationRequest request,
        out string error);
}
