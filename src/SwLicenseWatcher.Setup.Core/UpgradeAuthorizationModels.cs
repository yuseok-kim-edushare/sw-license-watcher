using System.Text.Json.Serialization;

namespace SwLicenseWatcher.Setup.Core;

public sealed class UpgradeAuthorizationRequest
{
    [JsonPropertyName("DeviceCode")]
    public string DeviceCode { get; set; } = string.Empty;

    [JsonPropertyName("DeviceId")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("DevicePublicKey")]
    public string? DevicePublicKey { get; set; }

    [JsonPropertyName("DeviceCertificate")]
    public string? DeviceCertificate { get; set; }

    [JsonPropertyName("DeviceProof")]
    public string? DeviceProof { get; set; }
}

public sealed class UpgradeAuthorizationResponse
{
    public string DeviceCode { get; set; } = string.Empty;

    public string? DeviceId { get; set; }

    public string? DevicePublicKey { get; set; }

    public string? DeviceCertificate { get; set; }
}
