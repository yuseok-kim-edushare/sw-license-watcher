using System.Text.Json.Serialization;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

public sealed record NotificationMessage(string Subject, string Body);

public sealed record WebhookPayload([property: JsonPropertyName("text")] string Text);
