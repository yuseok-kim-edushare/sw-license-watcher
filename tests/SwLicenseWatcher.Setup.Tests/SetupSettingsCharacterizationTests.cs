using System.Text.Json;
using SwLicenseWatcher.Setup.Core;

namespace SwLicenseWatcher.Setup.Tests;

public class SetupSettingsCharacterizationTests
{
    [Fact]
    public void CompanySettingsStore_serialize_normalizes_values_and_uses_camel_case()
    {
        var settings = new CompanySettings
        {
            ServerBaseUrl = "  https://license.example.local/api  ",
            AgentToken = "  " + new string('t', 32) + "  ",
            Version = "  1.2.3  "
        };

        var json = CompanySettingsStore.Serialize(settings);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("https://license.example.local/api", document.RootElement.GetProperty("serverBaseUrl").GetString());
        Assert.Equal(new string('t', 32), document.RootElement.GetProperty("agentToken").GetString());
        Assert.Equal("1.2.3", document.RootElement.GetProperty("version").GetString());
        Assert.False(document.RootElement.TryGetProperty("ServerBaseUrl", out _));
        Assert.Contains(Environment.NewLine, json, StringComparison.Ordinal);
    }

    [Fact]
    public void CompanySettingsStore_save_and_load_round_trips_and_appends_newline()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "nested", "company.json");
        var expected = Valid();

        CompanySettingsStore.Save(path, expected);
        var loaded = CompanySettingsStore.Load(path);

        Assert.EndsWith(Environment.NewLine, File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal(expected.ServerBaseUrl, loaded.ServerBaseUrl);
        Assert.Equal(expected.AgentToken, loaded.AgentToken);
        Assert.Equal(expected.Version, loaded.Version);
    }

    [Fact]
    public void CompanySettingsStore_load_reads_property_names_case_insensitively()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "company.json");
        File.WriteAllText(
            path,
            $$"""{"SERVERBASEURL":"https://license.example.local","AGENTTOKEN":"{{new string('t', 32)}}","VERSION":"1.2.3"}""");

        var loaded = CompanySettingsStore.Load(path);

        Assert.Equal("https://license.example.local", loaded.ServerBaseUrl);
        Assert.Equal("1.2.3", loaded.Version);
    }

    [Theory]
    [InlineData("--UNINSTALL")]
    [InlineData("-uninstall")]
    [InlineData("/Uninstall")]
    public void SetupArguments_parse_uninstall_switch_case_insensitively(string argument)
    {
        Assert.True(SetupArguments.Parse([argument]).Uninstall);
    }

    [Fact]
    public void SetupArguments_parse_trims_quotes_and_ignores_empty_or_unknown_values()
    {
        var parsed = SetupArguments.Parse(
        [
            """--source-exe=  "C:\build output\setup.exe"  """,
            "--payload-dir=",
            "--other=value"
        ]);

        Assert.Equal(@"C:\build output\setup.exe", parsed.SourceExePath);
        Assert.Null(parsed.PayloadDirectory);
    }

    private static CompanySettings Valid() =>
        new()
        {
            ServerBaseUrl = "https://license.example.local",
            AgentToken = new string('t', 32),
            Version = "1.2.3"
        };
}
