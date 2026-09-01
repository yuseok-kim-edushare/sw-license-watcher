using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Api;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api.Tests;

public class SqlServerSchemaApplicatorTests
{
    [Fact]
    public async Task ApplyIfEnabledAsync_does_nothing_when_disabled()
    {
        var applicator = CreateApplicator(
            new SqlServerStorageOptions { ConnectionString = "Server=(local);Database=missing;" },
            applyOnStartup: false);

        await applicator.ApplyIfEnabledAsync(CancellationToken.None);
    }

    [Fact]
    public void BuildBatches_emits_idempotent_schema_ddl_from_storage_options()
    {
        var options = new SqlServerStorageOptions { SchemaName = "inventory" };
        var batches = CreateApplicator(options, applyOnStartup: false).BuildBatches();

        var sql = Assert.Single(batches);
        Assert.Contains("IF SCHEMA_ID(N'inventory') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'[inventory].[pc_entity]', N'U') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [inventory].[pc_entity]", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [inventory].[stale_heartbeat_notification]", sql, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'[inventory].[stale_heartbeat_notification]', N'U') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pc_installed_sw_pc_id'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyIfEnabledAsync_wraps_connection_failures_when_enabled()
    {
        var applicator = CreateApplicator(
            new SqlServerStorageOptions { ConnectionString = "Server=127.0.0.1,1;Database=missing;User ID=sa;Password=invalid;Connect Timeout=1;TrustServerCertificate=True" },
            applyOnStartup: true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => applicator.ApplyIfEnabledAsync(CancellationToken.None));
        Assert.Contains("Failed to apply SQL Server schema on startup", ex.Message, StringComparison.Ordinal);
        Assert.NotNull(ex.InnerException);
    }

    private static SqlServerSchemaApplicator CreateApplicator(SqlServerStorageOptions storage, bool applyOnStartup) =>
        new(
            storage,
            Options.Create(new DatabaseOptions { ApplySchemaOnStartup = applyOnStartup }),
            new SqlServerSchemaScriptBuilder(),
            NullLogger<SqlServerSchemaApplicator>.Instance);
}
