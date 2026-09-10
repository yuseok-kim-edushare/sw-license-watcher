using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api.Tests;

public class ApiSecurityOptionsValidatorTests
{
    private const string Usable = "abcdefghijklmnopqrstuvwxyz012345";
    private const string Agent = "agent-token-abcdefghijklmnopqrst";
    private const string Admin = "admin-token-abcdefghijklmnopqrst";

    [Fact]
    public void HasRequiredRoleTokens_accepts_distinct_agent_and_admin_tokens()
    {
        Assert.True(ApiSecurityOptionsValidator.HasRequiredRoleTokens(new ApiSecurityOptions
        {
            AgentToken = Agent,
            AdminToken = Admin
        }));
    }

    [Fact]
    public void HasRequiredRoleTokens_rejects_legacy_token_only()
    {
        Assert.False(ApiSecurityOptionsValidator.HasRequiredRoleTokens(new ApiSecurityOptions { Token = Usable }));
    }

    [Fact]
    public void HasRequiredRoleTokens_rejects_when_either_role_token_is_missing()
    {
        Assert.False(ApiSecurityOptionsValidator.HasRequiredRoleTokens(new ApiSecurityOptions()));
        Assert.False(ApiSecurityOptionsValidator.HasRequiredRoleTokens(new ApiSecurityOptions
        {
            AgentToken = Agent,
            AdminToken = string.Empty
        }));
        Assert.False(ApiSecurityOptionsValidator.HasRequiredRoleTokens(new ApiSecurityOptions
        {
            Token = " ",
            AgentToken = string.Empty,
            AdminToken = "short"
        }));
    }

    [Fact]
    public void HasValidConfiguredTokenLengths_rejects_a_short_configured_token()
    {
        Assert.False(ApiSecurityOptionsValidator.HasValidConfiguredTokenLengths(new ApiSecurityOptions
        {
            AgentToken = "too-short",
            AdminToken = Admin
        }));
        Assert.True(ApiSecurityOptionsValidator.HasValidConfiguredTokenLengths(new ApiSecurityOptions
        {
            AgentToken = Agent,
            AdminToken = Admin
        }));
    }

    [Fact]
    public void RejectsLegacySharedToken_requires_token_to_be_empty()
    {
        Assert.True(ApiSecurityOptionsValidator.RejectsLegacySharedToken(new ApiSecurityOptions
        {
            AgentToken = Agent,
            AdminToken = Admin
        }));
        Assert.False(ApiSecurityOptionsValidator.RejectsLegacySharedToken(new ApiSecurityOptions
        {
            Token = Usable,
            AgentToken = Agent,
            AdminToken = Admin
        }));
    }

    [Fact]
    public void HasDistinctRoleTokens_rejects_matching_agent_and_admin_tokens()
    {
        Assert.False(ApiSecurityOptionsValidator.HasDistinctRoleTokens(new ApiSecurityOptions
        {
            AgentToken = Agent,
            AdminToken = Agent
        }));
    }

    [Fact]
    public void HasDistinctRoleTokens_accepts_distinct_role_tokens()
    {
        Assert.True(ApiSecurityOptionsValidator.HasDistinctRoleTokens(new ApiSecurityOptions
        {
            AgentToken = Agent,
            AdminToken = Admin
        }));
    }
}
