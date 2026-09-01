using System.Text.Json;
using System.Text.Json.Serialization;

namespace SwLicenseWatcher.Core;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(InventoryIngestionRequest))]
[JsonSerializable(typeof(AgentHeartbeat))]
[JsonSerializable(typeof(UpdateManifest))]
[JsonSerializable(typeof(WorkerHealthReport))]
public sealed partial class InventoryJsonSerializerContext : JsonSerializerContext;
