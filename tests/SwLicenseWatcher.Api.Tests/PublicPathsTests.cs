using Microsoft.AspNetCore.Http;
using SwLicenseWatcher.Api;

namespace SwLicenseWatcher.Api.Tests;

public class PublicPathsTests
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/HEALTH")]
    [InlineData("/admin")]
    [InlineData("/admin/")]
    [InlineData("/ADMIN/index.html")]
    [InlineData("/admin/admin.js")]
    [InlineData("/admin/admin.css")]
    public void IsAnonymous_allows_health_and_admin_assets(string path)
    {
        Assert.True(PublicPaths.IsAnonymous(path));
    }

    [Theory]
    [InlineData("/api/inventory/devices")]
    [InlineData("/api/policies")]
    [InlineData("/api/violations")]
    [InlineData("/api/schema/sql")]
    [InlineData("/api/updates/worker/manifest")]
    [InlineData("/api/admin")]
    [InlineData("/adminfoo")]
    [InlineData("/healthz")]
    [InlineData("/")]
    public void IsAnonymous_rejects_api_and_lookalike_paths(string path)
    {
        Assert.False(PublicPaths.IsAnonymous(path));
    }

    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/")]
    [InlineData("/admin/index.html")]
    [InlineData("/Admin/admin.js")]
    public void IsAdminAsset_matches_admin_prefix(string path)
    {
        Assert.True(PublicPaths.IsAdminAsset(path));
    }

    [Theory]
    [InlineData("/admin")]
    [InlineData("/ADMIN")]
    [InlineData("/admin/")]
    public void IsAdminIndex_matches_the_dashboard_entry(string path)
    {
        Assert.True(PublicPaths.IsAdminIndex(path));
    }

    [Theory]
    [InlineData("/admin/index.html")]
    [InlineData("/admin/admin.js")]
    [InlineData("/health")]
    public void IsAdminIndex_rejects_non_entry_paths(string path)
    {
        Assert.False(PublicPaths.IsAdminIndex(path));
    }

    [Fact]
    public void ApplySecurityHeaders_sets_csp_nosniff_referrer_and_no_store()
    {
        var headers = new HeaderDictionary();

        AdminStaticFiles.ApplySecurityHeaders(headers);

        Assert.Equal(AdminStaticFiles.ContentSecurityPolicy, headers["Content-Security-Policy"]);
        Assert.Equal("nosniff", headers["X-Content-Type-Options"]);
        Assert.Equal("no-referrer", headers["Referrer-Policy"]);
        Assert.Equal("no-store", headers["Cache-Control"]);
        Assert.Contains("script-src 'self'", headers["Content-Security-Policy"].ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", headers["Content-Security-Policy"].ToString(), StringComparison.Ordinal);
    }
}
