using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Core.Tests;

public class ReleaseChecksumParserTests
{
    [Fact]
    public void Reads_gnu_and_coreutils_sha256sum_lines()
    {
        const string zip = "SwLicenseWatcher.Agent.Worker-1.2.3.zip";
        const string sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var text = $"""
            aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  SwLicenseWatcher.zip
            {sha}  {zip}
            bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb *SwLicenseWatcher.Packager-1.2.3.zip
            """;

        Assert.True(ReleaseChecksumParser.TryGetSha256(text, zip, out var parsed));
        Assert.Equal(sha, parsed);
        Assert.True(ReleaseChecksumParser.TryGetSha256(
            $"{sha} *{zip}",
            zip,
            out var starred));
        Assert.Equal(sha, starred);
    }

    [Fact]
    public void Rejects_missing_or_malformed_entries()
    {
        Assert.False(ReleaseChecksumParser.TryGetSha256("", "worker.zip", out _));
        Assert.False(ReleaseChecksumParser.TryGetSha256("not-a-hash  worker.zip", "worker.zip", out _));
        Assert.False(ReleaseChecksumParser.TryGetSha256(
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef  other.zip",
            "worker.zip",
            out _));
    }
}

public class WorkerUpdatePackageNamesTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("V0.0.20", "0.0.20")]
    [InlineData(" release ", "release")]
    public void NormalizeVersion_strips_a_leading_v_before_digits(string input, string expected)
    {
        Assert.Equal(expected, WorkerUpdatePackageNames.NormalizeVersion(input));
    }

    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("0.0.20", true)]
    [InlineData("build_1", true)]
    [InlineData("..", false)]
    [InlineData("../evil", false)]
    [InlineData("1/2", false)]
    [InlineData("", false)]
    public void IsSafeVersion_rejects_path_tokens(string version, bool expected)
    {
        Assert.Equal(expected, WorkerUpdatePackageNames.IsSafeVersion(version));
    }
}

public class UpdatePackageUriTests
{
    [Theory]
    [InlineData("https://github.com/org/repo/releases/download/1.0.0/worker.zip")]
    [InlineData("http://127.0.0.1:5080/api/updates/worker/package/1.0.0")]
    [InlineData("http://localhost/api/updates/worker/package/1.0.0")]
    public void Accepts_https_and_loopback_http(string url)
    {
        Assert.True(UpdatePackageUri.TryCreateAllowed(url, out _));
    }

    [Theory]
    [InlineData("http://example.local/worker.zip")]
    [InlineData("/api/updates/worker/package/1.0.0")]
    [InlineData("ftp://127.0.0.1/worker.zip")]
    public void Rejects_non_https_remote_and_relative_urls(string url)
    {
        Assert.False(UpdatePackageUri.TryCreateAllowed(url, out _));
    }
}
