using System.Text.Json.Serialization;

namespace SwLicenseWatcher.Api;

[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(DesignResponse))]
[JsonSerializable(typeof(SnapshotAcceptedResponse))]
public sealed partial class ApiJsonSerializerContext : JsonSerializerContext;
