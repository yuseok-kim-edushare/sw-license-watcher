namespace SwLicenseWatcher.Setup.Core;

public sealed class UpgradeAuthorizationRequest
{
    public string DeviceCode { get; set; } = string.Empty;

    public string? DeviceId { get; set; }

    public string? DevicePublicKey { get; set; }

    public string? DeviceCertificate { get; set; }

    public string? DeviceProof { get; set; }
}

public sealed class UpgradeAuthorizationResponse
{
    public string DeviceCode { get; set; } = string.Empty;

    public string? DeviceId { get; set; }

    public string? DevicePublicKey { get; set; }

    public string? DeviceCertificate { get; set; }
}
