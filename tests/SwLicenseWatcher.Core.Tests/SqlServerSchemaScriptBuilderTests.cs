using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Core.Tests;

public class SqlServerSchemaScriptBuilderTests
{
    [Fact]
    public void Build_emits_schema_tables_indexes_and_constraints_for_default_options()
    {
        var sql = new SqlServerSchemaScriptBuilder().Build(new SqlServerStorageOptions());

        Assert.Contains("IF SCHEMA_ID(N'inventory') IS NULL EXEC(N'CREATE SCHEMA [inventory]');", sql);
        Assert.Contains("CREATE TABLE [inventory].[pc_entity]", sql);
        Assert.Contains("[device_code] NVARCHAR(128) NOT NULL", sql);
        Assert.Contains("[agent_version] NVARCHAR(32) NOT NULL", sql);
        Assert.Contains("CONSTRAINT [UX_pc_entity_device_code] UNIQUE ([device_code])", sql);
        Assert.Contains("CREATE TABLE [inventory].[pc_installed_sw]", sql);
        Assert.Contains("[display_name] NVARCHAR(256) NOT NULL", sql);
        Assert.Contains("[discovery_source] NVARCHAR(64) NOT NULL", sql);
        Assert.Contains("[classification] NVARCHAR(32) NOT NULL", sql);
        Assert.Contains("CONSTRAINT [FK_pc_installed_sw_pc_entity] FOREIGN KEY ([pc_id]) REFERENCES [inventory].[pc_entity]([pc_id])", sql);
        Assert.Contains("CREATE TABLE [inventory].[software_policy_list]", sql);
        Assert.Contains("[default_license_source] NVARCHAR(16) NULL", sql);
        Assert.Contains("CONSTRAINT [DF_software_policy_list_enabled] DEFAULT(1)", sql);
        Assert.Contains("CREATE TABLE [inventory].[pc_sw_license]", sql);
        Assert.Contains("[sw_name] NVARCHAR(256) NOT NULL", sql);
        Assert.Contains("[license_source] NVARCHAR(16) NOT NULL", sql);
        Assert.Contains("CONSTRAINT [UX_pc_sw_license_pc_id_sw_name] UNIQUE ([pc_id], [sw_name])", sql);
        Assert.Contains("CONSTRAINT [FK_pc_sw_license_pc_entity] FOREIGN KEY ([pc_id]) REFERENCES [inventory].[pc_entity]([pc_id]) ON DELETE CASCADE", sql);
        Assert.Contains("CREATE TABLE [inventory].[worker_update_pin]", sql);
        Assert.Contains("[package_url] NVARCHAR(2048) NOT NULL", sql);
        Assert.Contains("[sha256] NVARCHAR(64) NOT NULL", sql);
        Assert.Contains("CONSTRAINT [PK_worker_update_pin] PRIMARY KEY ([target_service_name])", sql);
        Assert.Contains("IF COL_LENGTH(N'[inventory].[software_policy_list]', N'default_license_source') IS NULL", sql);
        Assert.Contains("ALTER TABLE [inventory].[software_policy_list] ADD [default_license_source] NVARCHAR(16) NULL;", sql);
        Assert.Contains("[assigned_host_name] NVARCHAR(128) NULL", sql);
        Assert.Contains("[admin_notes] NVARCHAR(1024) NULL", sql);
        Assert.Contains("[assigned_device_code] NVARCHAR(128) NULL", sql);
        Assert.Contains("[device_id] NVARCHAR(36) NULL", sql);
        Assert.Contains("[device_public_key] NVARCHAR(MAX) NULL", sql);
        Assert.Contains("[device_certificate] NVARCHAR(MAX) NULL", sql);
        Assert.Contains("CREATE UNIQUE INDEX [UX_pc_entity_device_id] ON [inventory].[pc_entity]([device_id]) WHERE [device_id] IS NOT NULL;", sql);
        Assert.Contains("IF COL_LENGTH(N'[inventory].[pc_entity]', N'assigned_host_name') IS NULL", sql);
        Assert.Contains("ALTER TABLE [inventory].[pc_entity] ADD [assigned_host_name] NVARCHAR(128) NULL;", sql);
        Assert.Contains("IF COL_LENGTH(N'[inventory].[pc_entity]', N'admin_notes') IS NULL", sql);
        Assert.Contains("ALTER TABLE [inventory].[pc_entity] ADD [admin_notes] NVARCHAR(1024) NULL;", sql);
        Assert.Contains("CREATE TABLE [inventory].[software_violation]", sql);
        Assert.Contains("CONSTRAINT [UX_software_violation_pc_id_display_name] UNIQUE ([pc_id], [display_name])", sql);
        Assert.Contains("CREATE TABLE [inventory].[stale_heartbeat_notification]", sql);
        Assert.Contains("[notified_at_utc] DATETIMEOFFSET NOT NULL", sql);
        Assert.Contains("CONSTRAINT [FK_stale_heartbeat_notification_pc_entity] FOREIGN KEY ([pc_id]) REFERENCES [inventory].[pc_entity]([pc_id]) ON DELETE CASCADE", sql);
        Assert.Contains("CONSTRAINT [UX_stale_heartbeat_notification_pc_id] UNIQUE ([pc_id])", sql);
        Assert.Contains("CREATE TABLE [inventory].[pc_uninstall_request]", sql);
        Assert.Contains("[code_hash] NVARCHAR(64) NULL", sql);
        Assert.Contains("[code] NVARCHAR(16) NULL", sql);
        Assert.Contains("[origin] NVARCHAR(16) NOT NULL CONSTRAINT [DF_pc_uninstall_request_origin] DEFAULT(N'agent')", sql);
        Assert.Contains("IF COL_LENGTH(N'[inventory].[pc_uninstall_request]', N'origin') IS NULL", sql);
        Assert.Contains("CONSTRAINT [FK_pc_uninstall_request_pc_entity] FOREIGN KEY ([pc_id]) REFERENCES [inventory].[pc_entity]([pc_id]) ON DELETE CASCADE", sql);
        Assert.Contains("CREATE INDEX [IX_pc_installed_sw_pc_id] ON [inventory].[pc_installed_sw]([pc_id]);", sql);
        Assert.Contains("CREATE INDEX [IX_pc_installed_sw_classification] ON [inventory].[pc_installed_sw]([classification]);", sql);
        Assert.Contains("CREATE INDEX [IX_software_policy_list_classification] ON [inventory].[software_policy_list]([classification]);", sql);
        Assert.Contains("CREATE INDEX [IX_software_violation_policy_id] ON [inventory].[software_violation]([policy_id]);", sql);
        Assert.Contains("CREATE INDEX [IX_pc_uninstall_request_pc_id] ON [inventory].[pc_uninstall_request]([pc_id]);", sql);
        Assert.Contains("CREATE INDEX [IX_pc_uninstall_request_status] ON [inventory].[pc_uninstall_request]([status]);", sql);
        Assert.Contains("CREATE INDEX [IX_pc_sw_license_pc_id] ON [inventory].[pc_sw_license]([pc_id]);", sql);
        Assert.Contains("IF OBJECT_ID(N'[inventory].[pc_entity]', N'U') IS NULL", sql);
        Assert.Contains("IF OBJECT_ID(N'[inventory].[pc_installed_sw]', N'U') IS NULL", sql);
        Assert.Contains("IF OBJECT_ID(N'[inventory].[software_policy_list]', N'U') IS NULL", sql);
        Assert.Contains("IF OBJECT_ID(N'[inventory].[software_violation]', N'U') IS NULL", sql);
        Assert.Contains("IF OBJECT_ID(N'[inventory].[stale_heartbeat_notification]', N'U') IS NULL", sql);
        Assert.Contains("IF OBJECT_ID(N'[inventory].[pc_uninstall_request]', N'U') IS NULL", sql);
        Assert.Contains("IF OBJECT_ID(N'[inventory].[pc_sw_license]', N'U') IS NULL", sql);
        Assert.Contains("IF OBJECT_ID(N'[inventory].[worker_update_pin]', N'U') IS NULL", sql);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pc_installed_sw_pc_id' AND object_id = OBJECT_ID(N'[inventory].[pc_installed_sw]'))", sql);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_pc_installed_sw_classification' AND object_id = OBJECT_ID(N'[inventory].[pc_installed_sw]'))", sql);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_software_policy_list_classification' AND object_id = OBJECT_ID(N'[inventory].[software_policy_list]'))", sql);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_software_violation_policy_id' AND object_id = OBJECT_ID(N'[inventory].[software_violation]'))", sql);
    }

    [Fact]
    public void Build_uses_custom_identifiers_in_generated_ddl()
    {
        var options = new SqlServerStorageOptions
        {
            SchemaName = "ops_inventory",
            PcTable = new PcTableOptions { TableName = "machines", PrimaryKeyColumn = "id", DeviceCodeColumn = "code" }
        };

        var sql = new SqlServerSchemaScriptBuilder().Build(options);

        Assert.Contains("[ops_inventory].[machines]", sql);
        Assert.Contains("CONSTRAINT [UX_machines_code] UNIQUE ([code])", sql);
        Assert.Contains("REFERENCES [ops_inventory].[machines]([id])", sql);
        Assert.Contains("CREATE TABLE [ops_inventory].[stale_heartbeat_notification]", sql);
        Assert.Contains("CONSTRAINT [FK_stale_heartbeat_notification_machines] FOREIGN KEY ([pc_id]) REFERENCES [ops_inventory].[machines]([id]) ON DELETE CASCADE", sql);
        Assert.Contains("CREATE TABLE [ops_inventory].[pc_uninstall_request]", sql);
        Assert.Contains("CONSTRAINT [FK_pc_uninstall_request_machines] FOREIGN KEY ([pc_id]) REFERENCES [ops_inventory].[machines]([id]) ON DELETE CASCADE", sql);
        Assert.Contains("CREATE TABLE [ops_inventory].[pc_sw_license]", sql);
        Assert.Contains("CONSTRAINT [FK_pc_sw_license_machines] FOREIGN KEY ([pc_id]) REFERENCES [ops_inventory].[machines]([id]) ON DELETE CASCADE", sql);
        Assert.Contains("CREATE TABLE [ops_inventory].[worker_update_pin]", sql);
    }

    [Fact]
    public void Build_hashes_constraint_names_that_exceed_128_characters()
    {
        var table = new string('T', 128);
        var column = new string('C', 128);
        var options = new SqlServerStorageOptions
        {
            PcTable = new PcTableOptions { TableName = table, DeviceCodeColumn = column }
        };

        var sql = new SqlServerSchemaScriptBuilder().Build(options);

        const string prefix = "CONSTRAINT [";
        var start = sql.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var nameStart = start + prefix.Length;
        var nameEnd = sql.IndexOf(']', nameStart);
        var constraintName = sql[nameStart..nameEnd];

        Assert.Equal(128, constraintName.Length);
        Assert.StartsWith("UX_" + new string('T', 128)[..(128 - 16 - 1 - 3)], constraintName);
        Assert.Matches("_[0-9A-F]{16}$", constraintName);
    }

    [Fact]
    public void Build_rejects_invalid_identifiers()
    {
        var options = new SqlServerStorageOptions { SchemaName = "inventory;DROP" };

        var ex = Assert.Throws<ArgumentException>(() => new SqlServerSchemaScriptBuilder().Build(options));
        Assert.Contains("SQL identifiers must start with a letter or underscore", ex.Message);
    }

    [Theory]
    [InlineData("name", "name")]
    [InlineData("a]b", "a]]b")]
    [InlineData("]]", "]]]]")]
    public void Escape_doubles_closing_brackets(string identifier, string expected)
    {
        Assert.Equal(expected, SqlServerSchemaScriptBuilder.Escape(identifier));
    }

    [Theory]
    [InlineData("inventory", "inventory")]
    [InlineData("O'Brien", "O''Brien")]
    [InlineData("a''b", "a''''b")]
    public void EscapeSqlLiteral_doubles_single_quotes(string value, string expected)
    {
        Assert.Equal(expected, SqlServerSchemaScriptBuilder.EscapeSqlLiteral(value));
    }
}

public class SqlIdentifierValidatorTests
{
    [Theory]
    [InlineData("inventory")]
    [InlineData("_schema")]
    [InlineData("pc_entity")]
    [InlineData("A")]
    public void IsValid_accepts_ascii_identifiers(string identifier)
    {
        Assert.True(SqlIdentifierValidator.IsValid(identifier));
    }

    [Fact]
    public void IsValid_accepts_128_character_identifier()
    {
        Assert.True(SqlIdentifierValidator.IsValid(new string('A', 128)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1table")]
    [InlineData("pc-entity")]
    [InlineData("pc.entity")]
    [InlineData("테이블")]
    public void IsValid_rejects_illegal_identifiers(string? identifier)
    {
        Assert.False(SqlIdentifierValidator.IsValid(identifier));
    }

    [Fact]
    public void IsValid_rejects_identifiers_longer_than_128_characters()
    {
        Assert.False(SqlIdentifierValidator.IsValid(new string('A', 129)));
    }

    [Fact]
    public void Validate_rejects_invalid_installed_software_classification_column()
    {
        var options = new SqlServerStorageOptions
        {
            InstalledSoftwareTable = new InstalledSoftwareTableOptions { ClassificationColumn = "class-name" }
        };

        var ex = Assert.Throws<ArgumentException>(() => SqlIdentifierValidator.Validate(options));
        Assert.Contains("SQL identifiers must start with a letter or underscore", ex.Message);
    }

    [Fact]
    public void Validate_rejects_invalid_stale_heartbeat_notification_table()
    {
        var options = new SqlServerStorageOptions
        {
            StaleHeartbeatNotificationTable = new StaleHeartbeatNotificationTableOptions { TableName = "stale-heartbeat" }
        };

        var ex = Assert.Throws<ArgumentException>(() => SqlIdentifierValidator.Validate(options));
        Assert.Contains("SQL identifiers must start with a letter or underscore", ex.Message);
    }

    [Fact]
    public void Validate_rejects_invalid_uninstall_request_table()
    {
        var options = new SqlServerStorageOptions
        {
            UninstallRequestTable = new UninstallRequestTableOptions { TableName = "uninstall-request" }
        };

        var ex = Assert.Throws<ArgumentException>(() => SqlIdentifierValidator.Validate(options));
        Assert.Contains("SQL identifiers must start with a letter or underscore", ex.Message);
    }

    [Fact]
    public void Validate_rejects_invalid_software_license_table()
    {
        var options = new SqlServerStorageOptions
        {
            SoftwareLicenseTable = new SoftwareLicenseTableOptions { TableName = "pc-sw-license" }
        };

        var ex = Assert.Throws<ArgumentException>(() => SqlIdentifierValidator.Validate(options));
        Assert.Contains("SQL identifiers must start with a letter or underscore", ex.Message);
    }

    [Fact]
    public void Validate_rejects_invalid_worker_update_pin_table()
    {
        var options = new SqlServerStorageOptions
        {
            WorkerUpdatePinTable = new WorkerUpdatePinTableOptions { TableName = "worker-update-pin" }
        };

        var ex = Assert.Throws<ArgumentException>(() => SqlIdentifierValidator.Validate(options));
        Assert.Contains("SQL identifiers must start with a letter or underscore", ex.Message);
    }

    [Fact]
    public void Validate_rejects_invalid_assigned_host_name_column()
    {
        var options = new SqlServerStorageOptions
        {
            PcTable = new PcTableOptions { AssignedHostNameColumn = "assigned-host" }
        };

        var ex = Assert.Throws<ArgumentException>(() => SqlIdentifierValidator.Validate(options));
        Assert.Contains("SQL identifiers must start with a letter or underscore", ex.Message);
    }

    [Fact]
    public void Validate_rejects_invalid_default_license_source_column()
    {
        var options = new SqlServerStorageOptions
        {
            SoftwarePolicyTable = new SoftwarePolicyTableOptions { DefaultLicenseSourceColumn = "default-source" }
        };

        var ex = Assert.Throws<ArgumentException>(() => SqlIdentifierValidator.Validate(options));
        Assert.Contains("SQL identifiers must start with a letter or underscore", ex.Message);
    }
}
