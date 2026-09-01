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
[JsonSerializable(typeof(PolicyListResponse))]
[JsonSerializable(typeof(ViolationListResponse))]
[JsonSerializable(typeof(InstalledSoftwareEntry))]
[JsonSerializable(typeof(SoftwarePolicyEntry))]
[JsonSerializable(typeof(SoftwareViolationEntry))]
[JsonSerializable(typeof(IReadOnlyList<DeviceSummary>))]
[JsonSerializable(typeof(IReadOnlyList<InstalledSoftwareEntry>))]
[JsonSerializable(typeof(IReadOnlyList<SoftwareAggregate>))]
[JsonSerializable(typeof(IReadOnlyList<SoftwareDevice>))]
[JsonSerializable(typeof(IReadOnlyList<SoftwarePolicyEntry>))]
[JsonSerializable(typeof(IReadOnlyList<SoftwareViolationEntry>))]
[JsonSerializable(typeof(WebhookPayload))]
[JsonSerializable(typeof(NewBlacklistViolation))]
[JsonSerializable(typeof(IReadOnlyList<NewBlacklistViolation>))]
public sealed partial class ApiJsonSerializerContext : JsonSerializerContext;
