using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Api;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api.Tests;

public class RequestAuthenticatorTests
{
    private const string AgentToken = "agent-token-abcdefghijklmnopqrst";
    private const string AdminToken = "admin-token-abcdefghijklmnopqrst";

    [Fact]
    public async Task Static_admin_token_is_accepted_without_jwt()
    {
        var jwt = new RecordingJwtAuthenticator();
        var context = CreateContext("/api/design", "GET");

        var allowed = await RequestAuthenticator.IsAuthorizedAsync(
            context,
            "Bearer " + AdminToken,
            RoleSeparated(),
            jwt,
            CancellationToken.None);

        Assert.True(allowed);
        Assert.Equal(0, jwt.Calls);
    }

    [Fact]
    public async Task Jwt_is_accepted_on_admin_endpoints()
    {
        var jwt = new RecordingJwtAuthenticator { Succeed = true, Principal = new ClaimsPrincipal(new ClaimsIdentity("jwt")) };
        var context = CreateContext("/api/design", "GET");

        var allowed = await RequestAuthenticator.IsAuthorizedAsync(
            context,
            "Bearer header.payload.signature",
            RoleSeparated(),
            jwt,
            CancellationToken.None);

        Assert.True(allowed);
        Assert.Equal(1, jwt.Calls);
        Assert.Same(jwt.Principal, context.User);
    }

    [Fact]
    public async Task Jwt_is_accepted_on_shared_worker_manifest_get()
    {
        var jwt = new RecordingJwtAuthenticator { Succeed = true };
        var context = CreateContext("/api/updates/worker/manifest", "GET");

        var allowed = await RequestAuthenticator.IsAuthorizedAsync(
            context,
            "Bearer header.payload.signature",
            RoleSeparated(),
            jwt,
            CancellationToken.None);

        Assert.True(allowed);
        Assert.Equal(1, jwt.Calls);
    }

    [Fact]
    public async Task Jwt_is_rejected_on_agent_only_endpoints()
    {
        var jwt = new RecordingJwtAuthenticator { Succeed = true };
        var context = CreateContext("/api/inventory/snapshots", "POST");

        var allowed = await RequestAuthenticator.IsAuthorizedAsync(
            context,
            "Bearer header.payload.signature",
            RoleSeparated(),
            jwt,
            CancellationToken.None);

        Assert.False(allowed);
        Assert.Equal(0, jwt.Calls);
    }

    [Fact]
    public async Task Static_agent_token_still_works_on_agent_endpoints()
    {
        var jwt = new RecordingJwtAuthenticator { Succeed = true };
        var context = CreateContext("/api/inventory/snapshots", "POST");

        var allowed = await RequestAuthenticator.IsAuthorizedAsync(
            context,
            "Bearer " + AgentToken,
            RoleSeparated(),
            jwt,
            CancellationToken.None);

        Assert.True(allowed);
        Assert.Equal(0, jwt.Calls);
    }

    [Fact]
    public async Task Middleware_sets_www_authenticate_on_unauthorized()
    {
        var context = CreateContext("/api/design", "GET");
        context.RequestServices = new ServiceCollection()
            .AddSingleton<IOptions<ApiSecurityOptions>>(Options.Create(RoleSeparated()))
            .AddSingleton<IJwtAccessTokenAuthenticator>(new RecordingJwtAuthenticator())
            .BuildServiceProvider();

        await RequestPolicyMiddleware.InvokeAsync(context, () => Task.CompletedTask);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("Bearer", context.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task Middleware_allows_health_without_authorization()
    {
        var context = CreateContext("/health", "GET");
        context.RequestServices = new ServiceCollection()
            .AddSingleton<IOptions<ApiSecurityOptions>>(Options.Create(RoleSeparated()))
            .AddSingleton<IJwtAccessTokenAuthenticator>(new RecordingJwtAuthenticator())
            .BuildServiceProvider();

        var called = false;
        await RequestPolicyMiddleware.InvokeAsync(context, () =>
        {
            called = true;
            return Task.CompletedTask;
        });

        Assert.True(called);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    private static DefaultHttpContext CreateContext(string path, string method)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static ApiSecurityOptions RoleSeparated() =>
        new()
        {
            AgentToken = AgentToken,
            AdminToken = AdminToken,
            RequireHttps = false
        };

    private sealed class RecordingJwtAuthenticator : IJwtAccessTokenAuthenticator
    {
        internal int Calls { get; private set; }

        internal bool Succeed { get; set; }

        internal ClaimsPrincipal? Principal { get; set; }

        public bool IsEnabled => true;

        public Task<JwtAdminAuthenticationResult> AuthenticateAdminAsync(
            string? authorizationHeader,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Succeed
                ? new JwtAdminAuthenticationResult(true, Principal)
                : JwtAdminAuthenticationResult.Failed);
        }
    }
}
