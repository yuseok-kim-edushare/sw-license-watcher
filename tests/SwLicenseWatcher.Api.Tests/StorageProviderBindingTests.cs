using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Api;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api.Tests;

public class StorageProviderBindingTests
{
    [Fact]
    public void SqlServer_remains_the_active_section_when_provider_is_omitted()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Storage:SqlServer:ConnectionString"] = "Server=sql",
            ["Storage:SqlServer:SchemaName"] = "inventory",
            ["Storage:MySql:ConnectionString"] = "Server=mysql"
        });

        Assert.Equal("Server=sql", options.ConnectionString);
        Assert.Equal("inventory", options.SchemaName);
    }

    [Fact]
    public void PostgreSql_section_is_used_when_selected()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "PostgreSql",
            ["Storage:SqlServer:ConnectionString"] = "Server=sql",
            ["Storage:PostgreSql:ConnectionString"] = "Host=pg",
            ["Storage:PostgreSql:SchemaName"] = "assets",
            ["Storage:PostgreSql:PcTable:TableName"] = "asset_pc"
        });

        Assert.Equal("Host=pg", options.ConnectionString);
        Assert.Equal("assets", options.SchemaName);
        Assert.Equal("asset_pc", options.PcTable.TableName);
        Assert.Equal("device_code", options.PcTable.DeviceCodeColumn);
        Assert.Equal("installed_at_utc", options.PcTable.InstalledAtUtcColumn);
        Assert.Equal("pc_installed_sw", options.InstalledSoftwareTable.TableName);
        Assert.Equal("installed_at_utc", options.InstalledSoftwareTable.InstalledAtUtcColumn);
        Assert.Equal("pc_user_message", options.UserMessageTable.TableName);
    }

    [Fact]
    public void Blank_or_omitted_column_names_use_the_built_in_defaults()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Storage:SqlServer:ConnectionString"] = "Server=sql",
            ["Storage:SqlServer:PcTable:TableName"] = "company_pc",
            ["Storage:SqlServer:PcTable:HostNameColumn"] = "",
            ["Storage:SqlServer:PcTable:InstalledAtUtcColumn"] = " ",
            ["Storage:SqlServer:InstalledSoftwareTable:DisplayNameColumn"] = " sw_name ",
            ["Storage:SqlServer:SoftwarePolicyTable:TableName"] = "company_sw_policy"
        });

        Assert.Equal("company_pc", options.PcTable.TableName);
        Assert.Equal("host_name", options.PcTable.HostNameColumn);
        Assert.Equal("device_code", options.PcTable.DeviceCodeColumn);
        Assert.Equal("installed_at_utc", options.PcTable.InstalledAtUtcColumn);
        Assert.Equal("sw_name", options.InstalledSoftwareTable.DisplayNameColumn);
        Assert.Equal("display_version", options.InstalledSoftwareTable.DisplayVersionColumn);
        Assert.Equal("installed_at_utc", options.InstalledSoftwareTable.InstalledAtUtcColumn);
        Assert.Equal("company_sw_policy", options.SoftwarePolicyTable.TableName);
        Assert.Equal("product_name", options.SoftwarePolicyTable.ProductNameColumn);
        Assert.Equal("pc_user_message", options.UserMessageTable.TableName);
        Assert.Equal("title", options.UserMessageTable.TitleColumn);
    }

    [Fact]
    public void MariaDB_uses_the_MySql_section()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "MariaDB",
            ["Storage:MySql:ConnectionString"] = "Server=mariadb;Database=swlw"
        });

        Assert.Equal("Server=mariadb;Database=swlw", options.ConnectionString);
    }

    private static SqlServerStorageOptions Bind(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddApiOptions(configuration);
        services.AddOptions<UpdateManifestOptions>()
            .Configure(options => options.PackageUrl = "https://updates.example/worker.zip");
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<SqlServerStorageOptions>>().Value;
    }
}
