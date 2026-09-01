using SwLicenseWatcher.Api;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api.Tests;

public class SqlServerInventorySqlTests
{
    [Fact]
    public void Name_quotes_and_joins_identifiers()
    {
        Assert.Equal("[inventory].[pc_entity]", SqlServerInventoryRepository.Name("inventory", "pc_entity"));
    }

    [Fact]
    public void Name_escapes_closing_brackets_inside_identifiers()
    {
        Assert.Equal("[a]]b]", SqlServerInventoryRepository.Name("a]b"));
        Assert.Equal("[schema]]].[ta]]ble]", SqlServerInventoryRepository.Name("schema]", "ta]ble"));
    }

    [Fact]
    public void Truncate_shortens_values_that_exceed_the_limit()
    {
        Assert.Equal("hel", SqlServerInventoryRepository.Truncate("hello", 3));
    }

    [Fact]
    public void Truncate_preserves_null_and_short_values()
    {
        Assert.Null(SqlServerInventoryRepository.Truncate(null, 3));
        Assert.Equal("hi", SqlServerInventoryRepository.Truncate("hi", 3));
        Assert.Equal("hey", SqlServerInventoryRepository.Truncate("hey", 3));
    }

    [Fact]
    public void ToContainsPattern_returns_null_for_blank_search()
    {
        Assert.Null(SqlServerInventoryRepository.ToContainsPattern(null));
        Assert.Null(SqlServerInventoryRepository.ToContainsPattern(" "));
    }

    [Fact]
    public void ToContainsPattern_wraps_and_escapes_like_wildcards()
    {
        Assert.Equal("%widget%", SqlServerInventoryRepository.ToContainsPattern("widget"));
        Assert.Equal("%a[[]b]%", SqlServerInventoryRepository.ToContainsPattern("a[b]"));
        Assert.Equal("%100[%][_]%", SqlServerInventoryRepository.ToContainsPattern("100%_"));
        Assert.Equal("%trimmed%", SqlServerInventoryRepository.ToContainsPattern("  trimmed  "));
    }

    [Fact]
    public void BuildListPoliciesSql_pages_and_searches_name_pattern_and_publisher()
    {
        var repository = new SqlServerInventoryRepository(new SqlServerStorageOptions());
        var sql = repository.BuildListPoliciesSql();
        Assert.Contains("COUNT(*) OVER() AS total_count", sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY", sql, StringComparison.Ordinal);
        Assert.Contains("[product_name] LIKE @search", sql, StringComparison.Ordinal);
        Assert.Contains("[version_pattern] LIKE @search", sql, StringComparison.Ordinal);
        Assert.Contains("[publisher] LIKE @search", sql, StringComparison.Ordinal);
        Assert.Contains("[classification] = @classification", sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[software_policy_list]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildListPoliciesSql_quotes_identifiers_with_closing_brackets()
    {
        var options = new SqlServerStorageOptions { SchemaName = "inv]entory" };
        options.SoftwarePolicyTable.TableName = "sw]policy";
        options.SoftwarePolicyTable.ProductNameColumn = "name]";
        var sql = new SqlServerInventoryRepository(options).BuildListPoliciesSql();
        Assert.Contains("[inv]]entory].[sw]]policy]", sql, StringComparison.Ordinal);
        Assert.Contains("[name]]]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildListViolationsSql_pages_and_filters_device_software_and_since()
    {
        var repository = new SqlServerInventoryRepository(new SqlServerStorageOptions());
        var sql = repository.BuildListViolationsSql();
        Assert.Contains("COUNT(*) OVER() AS total_count", sql, StringComparison.Ordinal);
        Assert.Contains("OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY", sql, StringComparison.Ordinal);
        Assert.Contains("p.[device_code] LIKE @search", sql, StringComparison.Ordinal);
        Assert.Contains("p.[host_name] LIKE @search", sql, StringComparison.Ordinal);
        Assert.Contains("v.[display_name] LIKE @search", sql, StringComparison.Ordinal);
        Assert.Contains("v.[detected_at_utc] >= @since", sql, StringComparison.Ordinal);
        Assert.Contains("[inventory].[software_violation]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildListViolationsSql_quotes_join_identifiers()
    {
        var options = new SqlServerStorageOptions { SchemaName = "inv" };
        options.SoftwareViolationTable.TableName = "sw]violation";
        options.PcTable.TableName = "pc]entity";
        var sql = new SqlServerInventoryRepository(options).BuildListViolationsSql();
        Assert.Contains("[inv].[sw]]violation]", sql, StringComparison.Ordinal);
        Assert.Contains("[inv].[pc]]entity]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildGetStaleHeartbeatsSql_selects_pcs_whose_last_heartbeat_is_older_than_the_cutoff()
    {
        var sql = new SqlServerInventoryRepository(new SqlServerStorageOptions()).BuildGetStaleHeartbeatsSql();
        Assert.Contains("SELECT [device_code], [host_name], [last_heartbeat_utc]", sql, StringComparison.Ordinal);
        Assert.Contains("FROM [inventory].[pc_entity]", sql, StringComparison.Ordinal);
        Assert.Contains("[last_heartbeat_utc] IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[last_heartbeat_utc] < @cutoff", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY [last_heartbeat_utc], [device_code]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildGetStaleHeartbeatsSql_quotes_identifiers_with_closing_brackets()
    {
        var options = new SqlServerStorageOptions { SchemaName = "inv]entory" };
        options.PcTable.TableName = "pc]entity";
        options.PcTable.DeviceCodeColumn = "device]code";
        options.PcTable.LastHeartbeatUtcColumn = "last]heartbeat";
        var sql = new SqlServerInventoryRepository(options).BuildGetStaleHeartbeatsSql();
        Assert.Contains("[inv]]entory].[pc]]entity]", sql, StringComparison.Ordinal);
        Assert.Contains("[device]]code]", sql, StringComparison.Ordinal);
        Assert.Contains("[last]]heartbeat] < @cutoff", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildGetNotifiedStaleHeartbeatDeviceCodesSql_joins_notification_rows_to_pcs()
    {
        var sql = new SqlServerInventoryRepository(new SqlServerStorageOptions()).BuildGetNotifiedStaleHeartbeatDeviceCodesSql();
        Assert.Contains("SELECT p.[device_code]", sql, StringComparison.Ordinal);
        Assert.Contains("FROM [inventory].[stale_heartbeat_notification] AS n", sql, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN [inventory].[pc_entity] AS p", sql, StringComparison.Ordinal);
        Assert.Contains("p.[pc_id] = n.[pc_id]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildClearRecoveredStaleHeartbeatNotificationsSql_deletes_pcs_that_are_no_longer_stale()
    {
        var sql = new SqlServerInventoryRepository(new SqlServerStorageOptions()).BuildClearRecoveredStaleHeartbeatNotificationsSql();
        Assert.Contains("DELETE n", sql, StringComparison.Ordinal);
        Assert.Contains("FROM [inventory].[stale_heartbeat_notification] AS n", sql, StringComparison.Ordinal);
        Assert.Contains("[last_heartbeat_utc] IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[last_heartbeat_utc] >= @cutoff", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildInsertStaleHeartbeatNotificationSql_inserts_by_device_code_when_missing()
    {
        var sql = new SqlServerInventoryRepository(new SqlServerStorageOptions()).BuildInsertStaleHeartbeatNotificationSql();
        Assert.Contains("INSERT INTO [inventory].[stale_heartbeat_notification]", sql, StringComparison.Ordinal);
        Assert.Contains("([pc_id], [notified_at_utc])", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE p.[device_code] = @deviceCode", sql, StringComparison.Ordinal);
        Assert.Contains("SELECT p.[pc_id], @notifiedAt", sql, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildClearStaleHeartbeatNotificationIfHeartbeatAppliedSql_deletes_only_when_heartbeat_matches()
    {
        var sql = new SqlServerInventoryRepository(new SqlServerStorageOptions()).BuildClearStaleHeartbeatNotificationIfHeartbeatAppliedSql();
        Assert.Contains("DELETE n", sql, StringComparison.Ordinal);
        Assert.Contains("FROM [inventory].[stale_heartbeat_notification] AS n", sql, StringComparison.Ordinal);
        Assert.Contains("n.[pc_id] = @pcId", sql, StringComparison.Ordinal);
        Assert.Contains("p.[last_heartbeat_utc] = @heartbeatAt", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Stale_heartbeat_notification_sql_quotes_identifiers_with_closing_brackets()
    {
        var options = new SqlServerStorageOptions { SchemaName = "inv]entory" };
        options.PcTable.TableName = "pc]entity";
        options.PcTable.PrimaryKeyColumn = "pc]id";
        options.PcTable.DeviceCodeColumn = "device]code";
        options.PcTable.LastHeartbeatUtcColumn = "last]heartbeat";
        options.StaleHeartbeatNotificationTable.TableName = "stale]notify";
        options.StaleHeartbeatNotificationTable.PcForeignKeyColumn = "pc]fk";
        options.StaleHeartbeatNotificationTable.NotifiedAtUtcColumn = "notified]at";
        var repository = new SqlServerInventoryRepository(options);

        var notified = repository.BuildGetNotifiedStaleHeartbeatDeviceCodesSql();
        Assert.Contains("[inv]]entory].[stale]]notify]", notified, StringComparison.Ordinal);
        Assert.Contains("[inv]]entory].[pc]]entity]", notified, StringComparison.Ordinal);
        Assert.Contains("p.[pc]]id] = n.[pc]]fk]", notified, StringComparison.Ordinal);

        var insert = repository.BuildInsertStaleHeartbeatNotificationSql();
        Assert.Contains("([pc]]fk], [notified]]at])", insert, StringComparison.Ordinal);
        Assert.Contains("p.[device]]code] = @deviceCode", insert, StringComparison.Ordinal);
    }
}
