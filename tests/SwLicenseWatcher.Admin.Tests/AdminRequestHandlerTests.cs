using SwLicenseWatcher.Admin.Services;

namespace SwLicenseWatcher.Admin.Tests;

public class AdminRequestHandlerTests
{
    [Fact]
    public async Task Unauthorized_notifies_shell_without_setting_view_error()
    {
        var handler = new AdminRequestHandler();
        var unauthorized = false;
        var error = "";
        handler.UnauthorizedAsync = () =>
        {
            unauthorized = true;
            return Task.CompletedTask;
        };

        var succeeded = await handler.RunAsync(
            () => throw new AdminApiException("unauthorized", 401),
            message => error = message);

        Assert.False(succeeded);
        Assert.True(unauthorized);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Api_error_is_reported_to_owning_view()
    {
        var handler = new AdminRequestHandler();
        var error = "";

        var succeeded = await handler.RunAsync(
            () => throw new AdminApiException("bad request", 400),
            message => error = message);

        Assert.False(succeeded);
        Assert.Equal("bad request", error);
    }

    [Fact]
    public async Task Connection_error_uses_operation_specific_message()
    {
        var handler = new AdminRequestHandler();
        var error = "";

        var succeeded = await handler.RunAsync(
            () => throw new HttpRequestException(),
            message => error = message,
            "CSV를 받지 못했습니다.");

        Assert.False(succeeded);
        Assert.Equal("CSV를 받지 못했습니다.", error);
    }
}
