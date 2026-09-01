using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

public sealed class WebhookNotificationSender(
    IHttpClientFactory httpClientFactory,
    IOptions<NotificationOptions> options,
    ILogger<WebhookNotificationSender> logger) : INotificationSender
{
    public const string HttpClientName = "notifications-webhook";

    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        var webhook = options.Value.Webhook;
        if (!webhook.Enabled)
        {
            return;
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var text = string.Concat(message.Subject, Environment.NewLine, Environment.NewLine, message.Body);
            using var response = await client.PostAsJsonAsync(
                webhook.Url,
                new WebhookPayload(text),
                ApiJsonSerializerContext.Default.WebhookPayload,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Webhook notification '{Subject}' failed with HTTP {StatusCode}.",
                    message.Subject,
                    (int)response.StatusCode);
                return;
            }

            logger.LogInformation("Sent webhook notification '{Subject}'.", message.Subject);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Webhook notification '{Subject}' failed.", message.Subject);
        }
    }
}
