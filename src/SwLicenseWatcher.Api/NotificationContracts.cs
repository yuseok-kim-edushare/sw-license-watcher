using System.Text.Json.Serialization;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

public sealed record NotificationMessage(string Subject, string Body);

public sealed record WebhookPayload([property: JsonPropertyName("text")] string Text);

public sealed record SnapshotSaveResult(bool Applied, IReadOnlyList<InstalledSoftwareEntry> PreviousSoftware);

public sealed record StalePcHeartbeat(string DeviceCode, string HostName, DateTimeOffset LastHeartbeatUtc);
