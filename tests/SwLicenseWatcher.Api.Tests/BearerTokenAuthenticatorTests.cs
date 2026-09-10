using Microsoft.AspNetCore.Http;
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
    [InlineData("/api/agents/uninstall-requests")]
    [InlineData("/api/agents/uninstall-requests/12")]
    [InlineData("/api/agents/uninstall-requests/12/consume")]
    public void IsAuthorized_agent_token_is_accepted_on_agent_endpoints(string path)
    {
        var security = RoleSeparated();

        Assert.True(BearerTokenAuthenticator.IsAuthorized("Bearer " + AgentToken, security, path));
        Assert.False(BearerTokenAuthenticator.IsAuthorized("Bearer " + AdminToken, security, path));
    }

    [Theory]
    [InlineData("/api/inventory/devices")]
    [InlineData("/api/inventory/snapshots/PC-01")]
    [InlineData("/api/inventory/software")]
    [InlineData("/api/policies")]
    [InlineData("/api/policies/1")]
    [InlineData("/api/violations")]
    [InlineData("/api/uninstall-requests")]
    [InlineData("/api/uninstall-requests/12/approve")]
    [InlineData("/api/uninstall-requests/12/deny")]
    [InlineData("/api/design")]
    [InlineData("/api/schema")]
    [InlineData("/api/schema/sql")]
    public void IsAuthorized_admin_token_is_accepted_on_admin_endpoints(string path)
    {
        var security = RoleSeparated();

        Assert.False(BearerTokenAuthenticator.IsAuthorized("Bearer " + AgentToken, security, path));
        Assert.True(BearerTokenAuthenticator.IsAuthorized("Bearer " + AdminToken, security, path));
    }

    [Fact]
    public void IsAuthorized_both_tokens_can_read_the_worker_update_pin()
    {
        var security = RoleSeparated();
        const string path = "/api/updates/worker/manifest";

        Assert.True(BearerTokenAuthenticator.IsAuthorized("Bearer " + AgentToken, security, path, HttpMethods.Get));
        Assert.True(BearerTokenAuthenticator.IsAuthorized("Bearer " + AdminToken, security, path, HttpMethods.Get));
        Assert.False(BearerTokenAuthenticator.IsAuthorized("Bearer " + AgentToken, security, path, HttpMethods.Put));
        Assert.True(BearerTokenAuthenticator.IsAuthorized("Bearer " + AdminToken, security, path, HttpMethods.Put));
    }

    [Fact]
    public void IsAuthorized_legacy_token_is_not_accepted()
    {
        var security = RoleSeparated();
        security.Token = Token;

        Assert.False(BearerTokenAuthenticator.IsAuthorized("Bearer " + Token, security, "/api/inventory/devices"));
        Assert.False(BearerTokenAuthenticator.IsAuthorized("Bearer " + Token, security, "/api/inventory/snapshots"));
    }

    [Fact]
    public void IsAuthorized_rejects_an_unknown_token_when_roles_are_configured()
    {
        var security = RoleSeparated();

        Assert.False(BearerTokenAuthenticator.IsAuthorized("Bearer " + Token, security, "/api/inventory/snapshots"));
        Assert.False(BearerTokenAuthenticator.IsAuthorized(null, security, "/api/inventory/snapshots"));
    }

    [Theory]
    [InlineData("/api/inventory/snapshots")]
    [InlineData("/API/INVENTORY/SNAPSHOTS")]
    [InlineData("/api/agents/heartbeats")]
    [InlineData("/api/updates/worker/manifest")]
    [InlineData("/api/agents/uninstall-requests")]
    [InlineData("/API/AGENTS/UNINSTALL-REQUESTS/3/CONSUME")]
    public void IsAgentEndpoint_recognizes_agent_paths_case_insensitively(string path)
    {
        Assert.True(EndpointPolicies.IsAgentEndpoint(path, HttpMethods.Get));
    }

    [Theory]
    [InlineData("/api/inventory/snapshots/PC-01")]
    [InlineData("/api/inventory/devices")]
    [InlineData("/api/uninstall-requests")]
    [InlineData("/api/uninstall-requests/3/approve")]
    [InlineData("/health")]
    public void IsAgentEndpoint_rejects_non_agent_paths(string path)
    {
        Assert.False(EndpointPolicies.IsAgentEndpoint(path, HttpMethods.Get));
    }

    [Fact]
    public void IsAgentEndpoint_treats_manifest_put_as_admin_only()
    {
        Assert.True(EndpointPolicies.IsAgentEndpoint(AgentPaths.WorkerManifest, HttpMethods.Get));
        Assert.False(EndpointPolicies.IsAgentEndpoint(AgentPaths.WorkerManifest, HttpMethods.Put));
        Assert.True(EndpointPolicies.IsAdminEndpoint(AgentPaths.WorkerManifest, HttpMethods.Get));
        Assert.True(EndpointPolicies.IsAdminEndpoint(AgentPaths.WorkerManifest, HttpMethods.Put));
    }

    [Fact]
    public void IsAdminEndpoint_rejects_agent_ingestion_paths()
    {
        Assert.False(EndpointPolicies.IsAdminEndpoint(AgentPaths.InventorySnapshots, HttpMethods.Post));
        Assert.False(EndpointPolicies.IsAdminEndpoint(AgentPaths.Heartbeats, HttpMethods.Post));
        Assert.False(EndpointPolicies.IsAdminEndpoint(AgentPaths.UninstallRequests, HttpMethods.Post));
    }

    private static ApiSecurityOptions RoleSeparated() =>
        new()
        {
            AgentToken = AgentToken,
            AdminToken = AdminToken
        };
}
