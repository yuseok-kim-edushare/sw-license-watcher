using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api.Tests;

public class ApiSecurityOptionsValidatorTests
{
    private const string Usable = "abcdefghijklmnopqrstuvwxyz012345";
    private const string Agent = "agent-token-abcdefghijklmnopqrst";
    private const string Admin = "admin-token-abcdefghijklmnopqrst";

    [Fact]
    public void HasAtLeastOneUsableToken_accepts_legacy_token_only()
    {
        Assert.True(ApiSecurityOptionsValidator.HasAtLeastOneUsableToken(new ApiSecurityOptions { Token = Usable }));
    }

    [Fact]
    public void HasAtLeastOneUsableToken_accepts_role_tokens_without_legacy_token()
    {
        Assert.True(ApiSecurityOptionsValidator.HasAtLeastOneUsableToken(new ApiSecurityOptions
        {
            AgentToken = Agent,
            AdminToken = Admin
        }));
    }

    [Fact]
    public void HasAtLeastOneUsableToken_rejects_when_no_token_is_configured()
    {
        Assert.False(ApiSecurityOptionsValidator.HasAtLeastOneUsableToken(new ApiSecurityOptions()));
        Assert.False(ApiSecurityOptionsValidator.HasAtLeastOneUsableToken(new ApiSecurityOptions
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
            Token = Usable,
            AgentToken = "too-short"
        }));
        Assert.True(ApiSecurityOptionsValidator.HasValidConfiguredTokenLengths(new ApiSecurityOptions
        {
            Token = Usable,
            AgentToken = Agent,
            AdminToken = string.Empty
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
    public void HasDistinctRoleTokens_rejects_matching_agent_and_legacy_tokens()
    {
        Assert.False(ApiSecurityOptionsValidator.HasDistinctRoleTokens(new ApiSecurityOptions
        {
            Token = Agent,
            AgentToken = Agent,
            AdminToken = Admin
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
        Assert.True(ApiSecurityOptionsValidator.HasDistinctRoleTokens(new ApiSecurityOptions
        {
            Token = Usable,
            AdminToken = Admin
        }));
    }
}
