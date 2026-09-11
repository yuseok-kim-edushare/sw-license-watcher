using System.Text.Json.Serialization;

namespace SwLicenseWatcher.Setup.Core;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(CompanySettings))]
[JsonSerializable(typeof(UninstallRequestCreateBody))]
[JsonSerializable(typeof(UninstallRequestCreated))]
[JsonSerializable(typeof(AgentUninstallRequest))]
[JsonSerializable(typeof(UninstallRequestConsumeBody))]
[JsonSerializable(typeof(UpgradeAuthorizationRequest))]
[JsonSerializable(typeof(UpgradeAuthorizationResponse))]
public partial class SetupJsonContext : JsonSerializerContext;
