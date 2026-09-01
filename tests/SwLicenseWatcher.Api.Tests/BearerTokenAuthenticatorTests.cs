using SwLicenseWatcher.Api;

namespace SwLicenseWatcher.Api.Tests;

public class BearerTokenAuthenticatorTests
{
    private const string Token = "abcdefghijklmnopqrstuvwxyz012345";

    [Fact]
    public void IsAuthorized_accepts_the_matching_bearer_header()
    {
        Assert.True(BearerTokenAuthenticator.IsAuthorized("Bearer " + Token, Token));
    }

    [Fact]
    public void IsAuthorized_rejects_a_wrong_token()
    {
        Assert.False(BearerTokenAuthenticator.IsAuthorized("Bearer " + Token[..^1] + "Z", Token));
    }

    [Fact]
    public void IsAuthorized_rejects_a_missing_header()
    {
        Assert.False(BearerTokenAuthenticator.IsAuthorized(null, Token));
        Assert.False(BearerTokenAuthenticator.IsAuthorized(string.Empty, Token));
    }

    [Fact]
    public void IsAuthorized_rejects_a_header_without_the_bearer_prefix()
    {
        Assert.False(BearerTokenAuthenticator.IsAuthorized(Token, Token));
    }

    [Fact]
    public void IsAuthorized_rejects_extra_whitespace()
    {
        Assert.False(BearerTokenAuthenticator.IsAuthorized("Bearer  " + Token, Token));
    }

    [Fact]
    public void IsAuthorized_is_case_sensitive_for_the_bearer_scheme()
    {
        Assert.False(BearerTokenAuthenticator.IsAuthorized("bearer " + Token, Token));
    }
}
