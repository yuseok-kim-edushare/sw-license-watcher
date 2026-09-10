using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;
using SwLicenseWatcher.Infrastructure.SqlServer;

namespace SwLicenseWatcher.Api.Tests;

public class SchemaReconcileServiceTests
{
    [Fact]
    public async Task TryApplyAsync_returns_false_when_sql_is_unreachable()
    {
        var applicator = new SqlServerSchemaApplicator(
            new SqlServerStorageOptions
            {
                ConnectionString = "Server=127.0.0.1,1;Database=missing;User ID=sa;Password=invalid;Connect Timeout=1;TrustServerCertificate=True"
            },
            Options.Create(new DatabaseOptions()),
            new SqlServerSchemaScriptBuilder(),
            NullLogger<SqlServerSchemaApplicator>.Instance);
        var sut = new SchemaReconcileService(
            applicator,
            Options.Create(new DatabaseOptions()),
            NullLogger<SchemaReconcileService>.Instance);

        Assert.False(await sut.TryApplyAsync(CancellationToken.None));
        Assert.False(applicator.Last.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(applicator.Last.LastError));
    }

    [Fact]
    public async Task ExecuteAsync_exits_when_background_apply_is_disabled()
    {
        var applicator = new SqlServerSchemaApplicator(
            new SqlServerStorageOptions { ConnectionString = "Server=(local);Database=missing;" },
            Options.Create(new DatabaseOptions { ApplySchemaInBackground = false }),
            new SqlServerSchemaScriptBuilder(),
            NullLogger<SqlServerSchemaApplicator>.Instance);
        var sut = new SchemaReconcileService(
            applicator,
            Options.Create(new DatabaseOptions { ApplySchemaInBackground = false }),
            NullLogger<SchemaReconcileService>.Instance);

        await sut.StartAsync(CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);

        Assert.Null(applicator.Last.Succeeded);
    }
}
