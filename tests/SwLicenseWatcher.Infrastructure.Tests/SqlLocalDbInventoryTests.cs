using Microsoft.Data.SqlClient;
using SwLicenseWatcher.Infrastructure.Relational;

namespace SwLicenseWatcher.Infrastructure.Tests;

[Collection(SqlLocalDbCollection.Name)]
[Trait("Category", "SqlIntegration")]
public sealed class SqlLocalDbInventoryTests(SqlLocalDbFixture fixture) : RelationalInventoryTests
{
    protected override RelationalDataContext Context => fixture.Context;

    protected override void EnsureAvailable() => fixture.EnsureAvailable();

    [Fact]
    public async Task Company_schema_tables_exist_after_apply()
    {
        fixture.EnsureAvailable();
        var token = TestContext.Current.CancellationToken;
        await using var connection = new SqlConnection(fixture.Storage.ConnectionString);
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sys.tables
            WHERE SCHEMA_NAME(schema_id) = N'inventory'
              AND name IN (
                N'company_pc',
                N'company_pc_installed_sw',
                N'company_sw_policy',
                N'company_sw_violation',
                N'company_stale_heartbeat_notification',
                N'company_pc_uninstall_request',
                N'company_pc_sw_license',
                N'company_worker_update_pin');
            """;
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(token));
        Assert.Equal(8, count);
    }
}
