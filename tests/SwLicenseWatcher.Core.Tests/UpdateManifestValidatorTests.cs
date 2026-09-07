using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Core.Tests;

public class UpdateManifestValidatorTests
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Accepts_a_complete_https_pin()
    {
        Assert.True(UpdateManifestValidator.TryValidate(Valid(), out var error));
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void Accepts_appsettings_placeholders_so_the_table_can_be_seeded()
    {
        Assert.True(UpdateManifestValidator.TryValidate(
            Valid() with
            {
                Version = "REPLACE_ME_WORKER_VERSION",
                Sha256 = "REPLACE_ME_WORKER_SHA256"
            },
            out _));
    }

    [Fact]
    public void Rejects_http_package_urls_and_short_digests()
    {
        Assert.False(UpdateManifestValidator.TryValidate(Valid() with { PackageUrl = "http://example.local/worker.zip" }, out var httpError));
        Assert.Contains("HTTPS", httpError, StringComparison.Ordinal);
        Assert.False(UpdateManifestValidator.TryValidate(Valid() with { Sha256 = "abc" }, out var shaError));
        Assert.Contains("SHA-256", shaError, StringComparison.Ordinal);
        Assert.False(UpdateManifestValidator.TryValidate(Valid() with { RollbackAfterMinutes = 0 }, out var rollbackError));
        Assert.Contains("1 and 60", rollbackError, StringComparison.Ordinal);
    }

    private static UpdateManifest Valid() =>
        new(
            "SwLicenseWatcher.Agent.Worker",
            "0.0.20",
            "https://github.com/example/repo/releases/download/0.0.20/worker.zip",
            Sha,
            true,
            10);
}
