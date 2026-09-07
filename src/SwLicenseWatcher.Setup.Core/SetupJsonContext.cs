using System.Text.Json.Serialization;

namespace SwLicenseWatcher.Setup.Core;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(CompanySettings))]
[JsonSerializable(typeof(UninstallRequestCreateBody))]
[JsonSerializable(typeof(UninstallRequestCreated))]
[JsonSerializable(typeof(AgentUninstallRequest))]
[JsonSerializable(typeof(UninstallRequestConsumeBody))]
public partial class SetupJsonContext : JsonSerializerContext;
