using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Core.Tests;

public class ExecutableVersionTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData(" 1.2.3\n", "1.2.3")]
    [InlineData("1.2.3+abcdef", "1.2.3")]
    [InlineData("1.2.3.0", "1.2.3")]
    [InlineData("0.0.1", "0.0.1")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    public void Normalizes_release_versions(string raw, string expected)
    {
        Assert.Equal(expected, ExecutableVersion.Normalize(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0.0.0")]
    [InlineData("0.0.0.0")]
    [InlineData("not-a-version")]
    public void Rejects_missing_or_zero_versions(string? raw)
    {
        Assert.Null(ExecutableVersion.Normalize(raw));
    }

    [Fact]
    public void Reads_the_product_version_embedded_in_an_executable()
    {
        var source = typeof(ExecutableVersionTests).Assembly.Location;
        var expected = ExecutableVersion.Normalize(FileVersionInfoVersion(source));
        Assert.NotNull(expected);

        Assert.Equal(expected, ExecutableVersion.TryRead(source));
    }

    [Fact]
    public void Unversioned_file_has_no_product_version()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "SwLicenseWatcher.Agent.Worker.exe");
        File.WriteAllText(path, "not a pe");

        Assert.Null(ExecutableVersion.TryRead(path));
    }

    private static string? FileVersionInfoVersion(string path)
    {
        var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
        return string.IsNullOrWhiteSpace(info.ProductVersion) ? info.FileVersion : info.ProductVersion;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "swlw-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
