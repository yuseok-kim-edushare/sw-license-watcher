using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Api;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api.Tests;

public class WebhookNotificationSenderTests
{
    [Fact]
    public async Task SendAsync_does_not_call_the_network_when_the_webhook_is_disabled()
    {
        var handler = new RecordingHandler();
        var factory = new RecordingHttpClientFactory(handler);
        var sender = CreateSender(factory, enabled: false);

        await sender.SendAsync(new NotificationMessage("subject", "body"), CancellationToken.None);

        Assert.Null(factory.LastName);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task SendAsync_posts_subject_and_body_as_a_text_json_payload()
    {
        var handler = new RecordingHandler();
        var factory = new RecordingHttpClientFactory(handler);
        var sender = CreateSender(factory, enabled: true, url: "https://example.local/hooks/notify");
        var message = new NotificationMessage("신규 소프트웨어 설치 감지", "PC HOST (PC-001)에서 감지되었습니다.");

        await sender.SendAsync(message, CancellationToken.None);

        Assert.Equal(WebhookNotificationSender.HttpClientName, factory.LastName);
        Assert.Equal(1, handler.RequestCount);
        Assert.NotNull(handler.Request);
        Assert.Equal(HttpMethod.Post, handler.Request.Method);
        Assert.Equal("https://example.local/hooks/notify", handler.Request.RequestUri?.ToString());
        Assert.Equal("application/json", handler.Request.Content?.Headers.ContentType?.MediaType);

        var payload = JsonSerializer.Deserialize(handler.Body!, ApiJsonSerializerContext.Default.WebhookPayload);
        Assert.NotNull(payload);
        Assert.Equal(string.Concat(message.Subject, Environment.NewLine, Environment.NewLine, message.Body), payload.Text);
    }

    [Fact]
    public async Task SendAsync_does_not_throw_when_the_webhook_returns_an_error_status()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadGateway);
        var sender = CreateSender(new RecordingHttpClientFactory(handler), enabled: true);

        await sender.SendAsync(new NotificationMessage("subject", "body"), CancellationToken.None);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SendAsync_does_not_throw_when_the_http_call_fails()
    {
        var sender = CreateSender(new RecordingHttpClientFactory(new ThrowingHandler()), enabled: true);

        await sender.SendAsync(new NotificationMessage("subject", "body"), CancellationToken.None);
    }

    private static WebhookNotificationSender CreateSender(
        IHttpClientFactory factory,
        bool enabled,
        string url = "https://example.local/webhook")
    {
        var options = new NotificationOptions
        {
            Webhook =
            {
                Enabled = enabled,
                Url = url
            }
        };
        return new WebhookNotificationSender(factory, Options.Create(options), NullLogger<WebhookNotificationSender>.Instance);
    }

    private sealed class RecordingHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        private readonly HttpClient _client = new(handler);

        public string? LastName { get; private set; }

        public HttpClient CreateClient(string name)
        {
            LastName = name;
            return _client;
        }
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("unreachable");
    }
}
