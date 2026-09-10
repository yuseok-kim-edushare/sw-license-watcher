using System.Collections.Concurrent;
using System.Data;
using Microsoft.Data.SqlClient;
using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

internal sealed partial class SqlServerDataContext
{
    public async Task<(int TotalCount, List<DeviceSummary> Items)> ListDevicesAsync(
        int skip,
        int take,
        string? search,
        int? staleAfterHours,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var table = options.PcTable;
        DateTimeOffset? staleCutoff = staleAfterHours is int hours
            ? DateTimeOffset.UtcNow - TimeSpan.FromHours(hours)
            : null;
        var sql = $"""
            SELECT
                COUNT(*) OVER() AS total_count,
                {Name(table.DeviceCodeColumn)}, {Name(table.HostNameColumn)}, {Name(table.DomainNameColumn)},
                {Name(table.OperatingSystemColumn)}, {Name(table.AgentVersionColumn)},
                {Name(table.LastHeartbeatUtcColumn)}, {Name(table.LastInventoryUtcColumn)}
            FROM {Name(options.SchemaName, table.TableName)}
            WHERE (@search IS NULL
                OR {Name(table.DeviceCodeColumn)} LIKE @search
                OR {Name(table.HostNameColumn)} LIKE @search)
              AND (@staleCutoff IS NULL
                OR {Name(table.LastHeartbeatUtcColumn)} IS NULL
                OR {Name(table.LastHeartbeatUtcColumn)} < @staleCutoff)
            ORDER BY {Name(table.HostNameColumn)}, {Name(table.DeviceCodeColumn)}
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@search", DbValue(ToContainsPattern(search))));
        command.Parameters.Add(new SqlParameter("@staleCutoff", DbValue(staleCutoff)));
        command.Parameters.Add(new SqlParameter("@skip", skip));
        command.Parameters.Add(new SqlParameter("@take", take));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<DeviceSummary>();
        var totalCount = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (items.Count == 0)
            {
                totalCount = reader.GetInt32(reader.GetOrdinal("total_count"));
            }

            items.Add(new DeviceSummary(
                reader.GetString(reader.GetOrdinal(table.DeviceCodeColumn)),
                reader.GetString(reader.GetOrdinal(table.HostNameColumn)),
                reader.GetString(reader.GetOrdinal(table.DomainNameColumn)),
                reader.GetString(reader.GetOrdinal(table.OperatingSystemColumn)),
                reader.GetString(reader.GetOrdinal(table.AgentVersionColumn)),
                ReadNullableDateTimeOffset(reader, table.LastHeartbeatUtcColumn),
                ReadNullableDateTimeOffset(reader, table.LastInventoryUtcColumn)));
        }

        return (totalCount, items);
    }

    public async Task<DeviceDetail?> GetDeviceAsync(
        string deviceCode,
        string? classification,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var table = options.PcTable;
        var sql = $"""
            SELECT {Name(table.PrimaryKeyColumn)}, {Name(table.DeviceCodeColumn)}, {Name(table.HostNameColumn)},
                   {Name(table.DomainNameColumn)}, {Name(table.OperatingSystemColumn)}, {Name(table.AgentVersionColumn)},
                   {Name(table.LastHeartbeatUtcColumn)}, {Name(table.LastInventoryUtcColumn)}
            FROM {Name(options.SchemaName, table.TableName)}
            WHERE {Name(table.DeviceCodeColumn)} = @deviceCode;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@deviceCode", deviceCode));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var pcId = reader.GetInt64(reader.GetOrdinal(table.PrimaryKeyColumn));
        var detail = new DeviceDetail(
            reader.GetString(reader.GetOrdinal(table.DeviceCodeColumn)),
            reader.GetString(reader.GetOrdinal(table.HostNameColumn)),
            reader.GetString(reader.GetOrdinal(table.DomainNameColumn)),
            reader.GetString(reader.GetOrdinal(table.OperatingSystemColumn)),
            reader.GetString(reader.GetOrdinal(table.AgentVersionColumn)),
            ReadNullableDateTimeOffset(reader, table.LastHeartbeatUtcColumn),
            ReadNullableDateTimeOffset(reader, table.LastInventoryUtcColumn),
            []);
        await reader.CloseAsync();

        var installed = await ReadInstalledSoftwareAsync(connection, transaction: null, pcId, classification, cancellationToken);
        var assignments = await ReadLicenseAssignmentsAsync(connection, transaction: null, pcId, cancellationToken);
        var policies = await ListEnabledPoliciesAsync(connection, transaction: null, cancellationToken);
        return detail with { InstalledSoftware = InventoryDecisions.ApplyLicenseSources(installed, assignments, policies) };
    }

}
