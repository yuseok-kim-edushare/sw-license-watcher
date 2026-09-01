using SwLicenseWatcher.Api;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api.Tests;

public class BearerTokenAuthenticatorTests
{
    private const string Token = "abcdefghijklmnopqrstuvwxyz012345";
    private const string AgentToken = "agent-token-abcdefghijklmnopqrst";
    private const string AdminToken = "admin-token-abcdefghijklmnopqrst";

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

    [Theory]
    [InlineData("/api/inventory/snapshots")]
    [InlineData("/api/agents/heartbeats")]
    [InlineData("/api/updates/worker/manifest")]
    [InlineData("/api/inventory/devices")]
    [InlineData("/api/policies")]
    [InlineData("/api/violations")]
    [InlineData("/api/design")]
    [InlineData("/api/schema/sql")]
    public void IsAuthorized_legacy_token_grants_all_endpoints(string path)
    {
        var security = new ApiSecurityOptions { Token = Token };

        Assert.True(BearerTokenAuthenticator.IsAuthorized("Bearer " + Token, security, path));
    }

    [Theory]
    [InlineData("/api/inventory/snapshots")]
    [InlineData("/api/agents/heartbeats")]
    [InlineData("/api/updates/worker/manifest")]
    public void IsAuthorized_agent_token_is_accepted_on_agent_endpoints(string path)
    {
        var security = RoleSeparated();

        Assert.True(BearerTokenAuthenticator.IsAuthorized("Bearer " + AgentToken, security, path));
        Assert.True(BearerTokenAuthenticator.IsAuthorized("Bearer " + AdminToken, security, path));
    }

    [Theory]
    [InlineData("/api/inventory/devices")]
    [InlineData("/api/inventory/snapshots/PC-01")]
    [InlineData("/api/inventory/software")]
    [InlineData("/api/policies")]
    [InlineData("/api/policies/1")]
    [InlineData("/api/violations")]
    [InlineData("/api/design")]
    [InlineData("/api/schema/sql")]
    public void IsAuthorized_agent_token_is_rejected_on_admin_endpoints(string path)
    {
        var security = RoleSeparated();

        Assert.False(BearerTokenAuthenticator.IsAuthorized("Bearer " + AgentToken, security, path));
        Assert.True(BearerTokenAuthenticator.IsAuthorized("Bearer " + AdminToken, security, path));
    }

    [Fact]
    public void IsAuthorized_legacy_token_still_grants_all_endpoints_when_role_tokens_are_also_set()
    {
        var security = RoleSeparated();
        security.Token = Token;

        Assert.True(BearerTokenAuthenticator.IsAuthorized("Bearer " + Token, security, "/api/inventory/devices"));
        Assert.True(BearerTokenAuthenticator.IsAuthorized("Bearer " + Token, security, "/api/inventory/snapshots"));
    }

    [Fact]
    public void IsAuthorized_rejects_an_unknown_token_when_roles_are_configured()
    {
        var security = RoleSeparated();

        Assert.False(BearerTokenAuthenticator.IsAuthorized("Bearer " + Token, security, "/api/inventory/snapshots"));
        Assert.False(BearerTokenAuthenticator.IsAuthorized(null, security, "/api/inventory/snapshots"));
    }

    [Fact]
    public void IsAuthorized_ignores_blank_role_tokens()
    {
        var security = new ApiSecurityOptions
        {
            Token = Token,
            AgentToken = " ",
            AdminToken = string.Empty
        };

        Assert.True(BearerTokenAuthenticator.IsAuthorized("Bearer " + Token, security, "/api/policies"));
        Assert.False(BearerTokenAuthenticator.IsAuthorized("Bearer  ", security, "/api/inventory/snapshots"));
    }

    [Theory]
    [InlineData("/api/inventory/snapshots")]
    [InlineData("/API/INVENTORY/SNAPSHOTS")]
    [InlineData("/api/agents/heartbeats")]
    [InlineData("/api/updates/worker/manifest")]
    public void IsAgentEndpoint_recognizes_agent_paths_case_insensitively(string path)
    {
        Assert.True(BearerTokenAuthenticator.IsAgentEndpoint(path));
    }

    [Theory]
    [InlineData("/api/inventory/snapshots/PC-01")]
    [InlineData("/api/inventory/devices")]
    [InlineData("/health")]
    public void IsAgentEndpoint_rejects_non_agent_paths(string path)
    {
        Assert.False(BearerTokenAuthenticator.IsAgentEndpoint(path));
    }

    private static ApiSecurityOptions RoleSeparated() =>
        new()
        {
            AgentToken = AgentToken,
            AdminToken = AdminToken
        };
}
