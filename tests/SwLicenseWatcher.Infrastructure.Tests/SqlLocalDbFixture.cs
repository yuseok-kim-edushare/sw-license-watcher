using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;
using SwLicenseWatcher.Infrastructure.SqlServer;

namespace SwLicenseWatcher.Infrastructure.Tests;

[CollectionDefinition(SqlLocalDbCollection.Name)]
public sealed class SqlLocalDbCollection : ICollectionFixture<SqlLocalDbFixture>
{
    public const string Name = "SqlLocalDb";
}

public sealed class SqlLocalDbFixture : IAsyncLifetime
{
    public const string ConnectionEnvironmentName = "SWLW_SQLSERVER_CONNECTION";

    private string? _databaseName;
    private string? _masterConnectionString;

    public string? SkipReason { get; private set; }

    internal SqlServerDataContext Context { get; private set; } = null!;

    public SqlServerStorageOptions Storage { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var master = ResolveMasterConnectionString();
        try
        {
            await using (var probe = new SqlConnection(master))
            {
                await probe.OpenAsync();
            }
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            var reason =
                $"SQL Server LocalDB is not reachable ({ex.GetType().Name}: {ex.Message}). " +
                "Start MSSQLLocalDB or set SWLW_SQLSERVER_CONNECTION.";
            if (IsCi)
            {
                throw new InvalidOperationException(reason, ex);
            }

            SkipReason = reason;
            return;
        }

        _masterConnectionString = master;
        _databaseName = "swlw_ci_" + Guid.NewGuid().ToString("N");
        await ExecuteOnMasterAsync($"CREATE DATABASE [{_databaseName}];");

        var database = new SqlConnectionStringBuilder(master)
        {
            InitialCatalog = _databaseName
        };
        Storage = CompanySqlStorage.Create(database.ConnectionString);
        var applicator = new SqlServerSchemaApplicator(
            Storage,
            Options.Create(new DatabaseOptions { ApplySchemaOnStartup = false }),
            new SqlServerSchemaScriptBuilder(),
            NullLogger<SqlServerSchemaApplicator>.Instance);
        await applicator.ApplyAsync(CancellationToken.None);
        await applicator.ApplyAsync(CancellationToken.None);
        Context = new SqlServerDataContext(Storage);
    }

    public async ValueTask DisposeAsync()
    {
        if (_masterConnectionString is null || _databaseName is null)
        {
            return;
        }

        try
        {
            await ExecuteOnMasterAsync(
                $"""
                IF DB_ID(N'{_databaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{_databaseName}];
                END
                """);
        }
        catch (SqlException)
        {
            // LocalDB may already have torn down the instance at process exit.
        }
    }

    public void EnsureAvailable()
    {
        if (SkipReason is not null)
        {
            Assert.Skip(SkipReason);
        }
    }

    internal static string ResolveMasterConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionEnvironmentName);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var builder = new SqlConnectionStringBuilder(configured)
            {
                InitialCatalog = "master"
            };
            return builder.ConnectionString;
        }

        return new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB",
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 8,
            InitialCatalog = "master"
        }.ConnectionString;
    }

    private async Task ExecuteOnMasterAsync(string sql)
    {
        await using var connection = new SqlConnection(_masterConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        await command.ExecuteNonQueryAsync();
    }

    private static bool IsCi =>
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
}

internal static class CompanySqlStorage
{
    internal static SqlServerStorageOptions Create(string connectionString)
    {
        var storage = new SqlServerStorageOptions { ConnectionString = connectionString, SchemaName = "inventory" };
        storage.PcTable.TableName = "company_pc";
        storage.PcTable.PrimaryKeyColumn = "company_pc_id";
        storage.PcTable.DeviceCodeColumn = "asset_code";
        storage.PcTable.OperatingSystemColumn = "os_name";
        storage.InstalledSoftwareTable.TableName = "company_pc_installed_sw";
        storage.InstalledSoftwareTable.PcForeignKeyColumn = "company_pc_id";
        storage.InstalledSoftwareTable.DisplayNameColumn = "sw_name";
        storage.InstalledSoftwareTable.DisplayVersionColumn = "sw_version";
        storage.InstalledSoftwareTable.PublisherColumn = "publisher_name";
        storage.SoftwarePolicyTable.TableName = "company_sw_policy";
        storage.SoftwarePolicyTable.PrimaryKeyColumn = "sw_policy_id";
        storage.SoftwarePolicyTable.ClassificationColumn = "policy_type";
        storage.SoftwarePolicyTable.ProductNameColumn = "sw_name";
        storage.SoftwarePolicyTable.PublisherColumn = "publisher_name";
        storage.SoftwarePolicyTable.EnabledColumn = "is_enabled";
        storage.SoftwareViolationTable.TableName = "company_sw_violation";
        storage.SoftwareViolationTable.PrimaryKeyColumn = "sw_violation_id";
        storage.SoftwareViolationTable.PcForeignKeyColumn = "company_pc_id";
        storage.SoftwareViolationTable.PolicyForeignKeyColumn = "sw_policy_id";
        storage.SoftwareViolationTable.DisplayNameColumn = "sw_name";
        storage.SoftwareViolationTable.DisplayVersionColumn = "sw_version";
        storage.SoftwareViolationTable.PublisherColumn = "publisher_name";
        storage.StaleHeartbeatNotificationTable.TableName = "company_stale_heartbeat_notification";
        storage.StaleHeartbeatNotificationTable.PcForeignKeyColumn = "company_pc_id";
        storage.UninstallRequestTable.TableName = "company_pc_uninstall_request";
        storage.UninstallRequestTable.PcForeignKeyColumn = "company_pc_id";
        storage.SoftwareLicenseTable.TableName = "company_pc_sw_license";
        storage.SoftwareLicenseTable.PcForeignKeyColumn = "company_pc_id";
        storage.WorkerUpdatePinTable.TableName = "company_worker_update_pin";
        return storage;
    }
}
