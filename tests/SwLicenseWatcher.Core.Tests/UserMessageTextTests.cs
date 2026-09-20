using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Core.Tests;

public class UserMessageTextTests
{
    [Fact]
    public void TryNormalize_trims_and_accepts_title_and_body()
    {
        Assert.True(UserMessageText.TryNormalize("  제목  ", "  본문  ", out var title, out var body, out var error));
        Assert.Equal("제목", title);
        Assert.Equal("본문", body);
        Assert.Equal(string.Empty, error);
    }

    [Theory]
    [InlineData(null, "body")]
    [InlineData(" ", "body")]
    [InlineData("title", null)]
    [InlineData("title", " ")]
    public void TryNormalize_rejects_blank_fields(string? title, string? body)
    {
        Assert.False(UserMessageText.TryNormalize(title, body, out _, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryNormalize_rejects_overlong_title()
    {
        Assert.False(UserMessageText.TryNormalize(new string('a', 129), "body", out _, out _, out var error));
        Assert.Contains("128", error);
    }
}
