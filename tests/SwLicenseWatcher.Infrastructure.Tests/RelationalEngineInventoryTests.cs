using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;
using SwLicenseWatcher.Infrastructure.Relational;

namespace SwLicenseWatcher.Infrastructure.Tests;

public sealed class MySqlInventoryFixture : RelationalEngineFixture
{
    public MySqlInventoryFixture()
        : base("SWLW_MYSQL_CONNECTION", MySqlDialect.Instance, new MySqlSchemaScriptBuilder())
    {
    }
}

public sealed class MariaDbInventoryFixture : RelationalEngineFixture
{
    public MariaDbInventoryFixture()
        : base("SWLW_MARIADB_CONNECTION", MySqlDialect.Instance, new MySqlSchemaScriptBuilder())
    {
    }
}

public sealed class PostgreSqlInventoryFixture : RelationalEngineFixture
{
    public PostgreSqlInventoryFixture()
        : base("SWLW_POSTGRES_CONNECTION", PostgreSqlDialect.Instance, new PostgreSqlSchemaScriptBuilder())
    {
    }
}

public class RelationalEngineFixture : IAsyncLifetime
{
    private readonly string _environmentName;
    private readonly IRelationalDialect _dialect;
    private readonly ISchemaScriptBuilder _schemaBuilder;

    protected RelationalEngineFixture(string environmentName, IRelationalDialect dialect, ISchemaScriptBuilder schemaBuilder)
    {
        _environmentName = environmentName;
        _dialect = dialect;
        _schemaBuilder = schemaBuilder;
    }

    public string? SkipReason { get; private set; }

    public RelationalDataContext Context { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(_environmentName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            SkipReason = $"{_environmentName} is not set.";
            return;
        }

        var storage = CompanySqlStorage.Create(connectionString);
        var applicator = new RelationalSchemaApplicator(
            storage,
            Options.Create(new DatabaseOptions { ApplySchemaOnStartup = false }),
            _schemaBuilder,
            _dialect,
            NullLogger<RelationalSchemaApplicator>.Instance);
        await applicator.ApplyAsync(CancellationToken.None);
        await applicator.ApplyAsync(CancellationToken.None);
        Context = new RelationalDataContext(storage, _dialect);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void EnsureAvailable()
    {
        if (SkipReason is not null)
        {
            Assert.Skip(SkipReason);
        }
    }
}

[Trait("Category", "RelationalIntegration")]
public sealed class MySqlInventoryTests(MySqlInventoryFixture fixture) : RelationalInventoryTests, IClassFixture<MySqlInventoryFixture>
{
    protected override RelationalDataContext Context => fixture.Context;

    protected override void EnsureAvailable() => fixture.EnsureAvailable();
}

[Trait("Category", "RelationalIntegration")]
public sealed class MariaDbInventoryTests(MariaDbInventoryFixture fixture) : RelationalInventoryTests, IClassFixture<MariaDbInventoryFixture>
{
    protected override RelationalDataContext Context => fixture.Context;

    protected override void EnsureAvailable() => fixture.EnsureAvailable();
}

[Trait("Category", "RelationalIntegration")]
public sealed class PostgreSqlInventoryTests(PostgreSqlInventoryFixture fixture) : RelationalInventoryTests, IClassFixture<PostgreSqlInventoryFixture>
{
    protected override RelationalDataContext Context => fixture.Context;

    protected override void EnsureAvailable() => fixture.EnsureAvailable();
}
