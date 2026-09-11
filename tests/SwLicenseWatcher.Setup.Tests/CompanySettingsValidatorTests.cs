using SwLicenseWatcher.Setup.Core;

namespace SwLicenseWatcher.Setup.Tests;

public class CompanySettingsValidatorTests
{
    [Fact]
    public void Accepts_https_and_loopback_http()
    {
        Assert.True(CompanySettingsValidator.TryValidate(Valid("https://license.example.local"), out _));
        Assert.True(CompanySettingsValidator.TryValidate(Valid("http://127.0.0.1:5080"), out _));
        Assert.True(CompanySettingsValidator.TryValidate(Valid("http://localhost"), out _));
    }

    [Fact]
    public void Rejects_non_loopback_http_and_short_tokens()
    {
        Assert.False(CompanySettingsValidator.TryValidate(Valid("http://192.168.1.10"), out var httpError));
        Assert.Contains("HTTPS", httpError, StringComparison.Ordinal);
        Assert.False(CompanySettingsValidator.TryValidate(
            new CompanySettings
            {
                ServerBaseUrl = "https://ok.example",
                AgentToken = "too-short",
                Version = "1.0.0"
            },
            out var tokenError));
        Assert.Contains("32", tokenError, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceCode_defaults_to_the_machine_name_and_rejects_long_values()
    {
        Assert.True(DeviceCodeResolver.TryResolve("  ", "PC-01", out var fallback, out _));
        Assert.Equal("PC-01", fallback);
        Assert.True(DeviceCodeResolver.TryResolve("ASSET-9", "PC-01", out var asset, out _));
        Assert.Equal("ASSET-9", asset);
        Assert.False(DeviceCodeResolver.TryResolve(new string('x', 129), "PC-01", out _, out var error));
        Assert.Contains("128", error, StringComparison.Ordinal);
        Assert.True(DeviceCodeResolver.TryResolveForInstall("  ", "PC-01", "ASSET-7", out var kept, out _));
        Assert.Equal("ASSET-7", kept);
    }

    [Fact]
    public void SetupArguments_parse_uninstall_and_paths()
    {
        var parsed = SetupArguments.Parse(
        [
            "/uninstall",
            @"--source-exe=C:\pack\SwLicenseWatcher-Setup.exe",
            @"--payload-dir=C:\temp\payload"
        ]);
        Assert.True(parsed.Uninstall);
        Assert.Equal(@"C:\pack\SwLicenseWatcher-Setup.exe", parsed.SourceExePath);
        Assert.Equal(@"C:\temp\payload", parsed.PayloadDirectory);
    }

    private static CompanySettings Valid(string url) => new()
    {
        ServerBaseUrl = url,
        AgentToken = new string('t', 32),
        Version = "0.0.15"
    };
}
