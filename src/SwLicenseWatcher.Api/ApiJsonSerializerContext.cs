using System.Text.Json.Serialization;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(DesignResponse))]
[JsonSerializable(typeof(SnapshotAcceptedResponse))]
[JsonSerializable(typeof(DeviceSummary))]
[JsonSerializable(typeof(DeviceListResponse))]
[JsonSerializable(typeof(DeviceDetail))]
[JsonSerializable(typeof(SoftwareAggregate))]
[JsonSerializable(typeof(SoftwareAggregateListResponse))]
[JsonSerializable(typeof(SoftwareDevice))]
[JsonSerializable(typeof(SoftwareDeviceListResponse))]
[JsonSerializable(typeof(InstalledSoftwareEntry))]
[JsonSerializable(typeof(IReadOnlyList<DeviceSummary>))]
[JsonSerializable(typeof(IReadOnlyList<InstalledSoftwareEntry>))]
[JsonSerializable(typeof(IReadOnlyList<SoftwareAggregate>))]
[JsonSerializable(typeof(IReadOnlyList<SoftwareDevice>))]
[JsonSerializable(typeof(WebhookPayload))]
public sealed partial class ApiJsonSerializerContext : JsonSerializerContext;
