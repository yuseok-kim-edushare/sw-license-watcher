using System.Text.Json;
using System.Text.Json.Serialization;

namespace SwLicenseWatcher.Core;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(InventoryIngestionRequest))]
[JsonSerializable(typeof(AgentHeartbeat))]
[JsonSerializable(typeof(UpdateManifest))]
[JsonSerializable(typeof(WorkerHealthReport))]
[JsonSerializable(typeof(SoftwarePolicyEntry))]
[JsonSerializable(typeof(SoftwarePolicyEntry[]))]
[JsonSerializable(typeof(List<SoftwarePolicyEntry>))]
[JsonSerializable(typeof(SoftwarePolicyWriteRequest))]
[JsonSerializable(typeof(SoftwareViolationEntry))]
[JsonSerializable(typeof(SoftwareViolationEntry[]))]
[JsonSerializable(typeof(List<SoftwareViolationEntry>))]
public sealed partial class InventoryJsonSerializerContext : JsonSerializerContext;
