using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.Relational;

public sealed partial class RelationalDataContext
{
    public async Task<(int TotalCount, List<DeviceSummary> Items)> ListDevicesAsync(
        int skip,
        int take,
        string? search,
        int? staleAfterHours,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
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
                {Name(table.LastHeartbeatUtcColumn)}, {Name(table.LastInventoryUtcColumn)},
                {Name(table.AssignedHostNameColumn)}, {Name(table.AdminNotesColumn)},
                {Name(table.AssignedDeviceCodeColumn)}, {Name(table.DeviceIdColumn)}
            FROM {Name(options.SchemaName, table.TableName)}
            WHERE (@search IS NULL OR {HostNameSearchSql()})
              AND (@staleCutoff IS NULL
                OR {Name(table.LastHeartbeatUtcColumn)} IS NULL
                OR {Name(table.LastHeartbeatUtcColumn)} < @staleCutoff)
            ORDER BY {DisplayHostNameSql()}, {Name(table.DeviceCodeColumn)}
            {Pagination};
            """;
        await using var command = CreateCommand(connection, null, sql);
        Bind(command, ("@search", DbValue(ToContainsPattern(search))));
        Bind(command, ("@staleCutoff", DbValue(staleCutoff)));
        Bind(command, ("@skip", skip));
        Bind(command, ("@take", take));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<DeviceSummary>();
        var totalCount = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (items.Count == 0)
            {
                totalCount = ReadInt32(reader, "total_count");
            }

            var storedCode = reader.GetString(reader.GetOrdinal(table.DeviceCodeColumn));
            var assignedCode = ReadNullableString(reader, table.AssignedDeviceCodeColumn);
            items.Add(new DeviceSummary(
                DeviceCodes.Official(storedCode, assignedCode),
                reader.GetString(reader.GetOrdinal(table.HostNameColumn)),
                reader.GetString(reader.GetOrdinal(table.DomainNameColumn)),
                reader.GetString(reader.GetOrdinal(table.OperatingSystemColumn)),
                reader.GetString(reader.GetOrdinal(table.AgentVersionColumn)),
                ReadNullableDateTimeOffset(reader, table.LastHeartbeatUtcColumn),
                ReadNullableDateTimeOffset(reader, table.LastInventoryUtcColumn),
                ReadNullableString(reader, table.AssignedHostNameColumn),
                ReadNullableString(reader, table.AdminNotesColumn),
                ReadNullableString(reader, table.DeviceIdColumn),
                assignedCode));
        }

        return (totalCount, items);
    }

    public async Task<DeviceDetail?> GetDeviceAsync(
        string deviceCode,
        string? classification,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var table = options.PcTable;
        var sql = $"""
            SELECT {Name(table.PrimaryKeyColumn)}, {Name(table.DeviceCodeColumn)}, {Name(table.HostNameColumn)},
                   {Name(table.DomainNameColumn)}, {Name(table.OperatingSystemColumn)}, {Name(table.AgentVersionColumn)},
                   {Name(table.LastHeartbeatUtcColumn)}, {Name(table.LastInventoryUtcColumn)},
                   {Name(table.AssignedHostNameColumn)}, {Name(table.AdminNotesColumn)},
                   {Name(table.AssignedDeviceCodeColumn)}, {Name(table.DeviceIdColumn)}
            FROM {Name(options.SchemaName, table.TableName)}
            WHERE {PcLookupPredicate()};
            """;
        await using var command = CreateCommand(connection, null, sql);
        Bind(command, ("@deviceCode", deviceCode));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var pcId = reader.GetInt64(reader.GetOrdinal(table.PrimaryKeyColumn));
        var storedCode = reader.GetString(reader.GetOrdinal(table.DeviceCodeColumn));
        var assignedCode = ReadNullableString(reader, table.AssignedDeviceCodeColumn);
        var detail = new DeviceDetail(
            DeviceCodes.Official(storedCode, assignedCode),
            reader.GetString(reader.GetOrdinal(table.HostNameColumn)),
            reader.GetString(reader.GetOrdinal(table.DomainNameColumn)),
            reader.GetString(reader.GetOrdinal(table.OperatingSystemColumn)),
            reader.GetString(reader.GetOrdinal(table.AgentVersionColumn)),
            ReadNullableDateTimeOffset(reader, table.LastHeartbeatUtcColumn),
            ReadNullableDateTimeOffset(reader, table.LastInventoryUtcColumn),
            [],
            ReadNullableString(reader, table.AssignedHostNameColumn),
            ReadNullableString(reader, table.AdminNotesColumn),
            ReadNullableString(reader, table.DeviceIdColumn),
            assignedCode);
        await reader.CloseAsync();

        var installed = await ReadInstalledSoftwareAsync(connection, transaction: null, pcId, classification, cancellationToken);
        var assignments = await ReadLicenseAssignmentsAsync(connection, transaction: null, pcId, cancellationToken);
        var policies = await ListEnabledPoliciesAsync(connection, transaction: null, cancellationToken);
        return detail with { InstalledSoftware = InventoryDecisions.ApplyLicenseSources(installed, assignments, policies) };
    }

    public async Task<DeviceAgentAssignment?> GetDeviceAssignmentAsync(
        string deviceCode,
        string? deviceId,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var table = options.PcTable;
        var sql = $"""
            SELECT {Name(table.DeviceCodeColumn)}, {Name(table.AssignedDeviceCodeColumn)},
                   {Name(table.AssignedHostNameColumn)}, {Name(table.DeviceIdColumn)},
                   {Name(table.DeviceCertificateColumn)}
            FROM {Name(options.SchemaName, table.TableName)}
            WHERE {PcLookupPredicate()}
               OR (@deviceId IS NOT NULL AND {Name(table.DeviceIdColumn)} = @deviceId);
            """;
        await using var command = CreateCommand(connection, null, sql);
        Bind(command, ("@deviceCode", deviceCode));
        Bind(command, ("@deviceId", DbValue(NullIfWhiteSpace(deviceId))));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var storedCode = reader.GetString(reader.GetOrdinal(table.DeviceCodeColumn));
        var assignedCode = ReadNullableString(reader, table.AssignedDeviceCodeColumn);
        return new DeviceAgentAssignment(
            DeviceCodes.Official(storedCode, assignedCode),
            ReadNullableString(reader, table.AssignedHostNameColumn),
            ReadNullableString(reader, table.DeviceIdColumn),
            ReadNullableString(reader, table.DeviceCertificateColumn));
    }

    public async Task<DeviceEnrollmentKeys?> GetDeviceEnrollmentKeysAsync(
        string deviceCode,
        string? deviceId,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var table = options.PcTable;
        var sql = $"""
            SELECT {Name(table.DeviceCodeColumn)}, {Name(table.AssignedDeviceCodeColumn)},
                   {Name(table.DeviceIdColumn)}, {Name(table.DevicePublicKeyColumn)},
                   {Name(table.DeviceCertificateColumn)}
            FROM {Name(options.SchemaName, table.TableName)}
            WHERE {PcLookupPredicate()}
               OR (@deviceId IS NOT NULL AND {Name(table.DeviceIdColumn)} = @deviceId);
            """;
        await using var command = CreateCommand(connection, null, sql);
        Bind(command, ("@deviceCode", deviceCode));
        Bind(command, ("@deviceId", DbValue(NullIfWhiteSpace(deviceId))));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var storedCode = reader.GetString(reader.GetOrdinal(table.DeviceCodeColumn));
        var assignedCode = ReadNullableString(reader, table.AssignedDeviceCodeColumn);
        return new DeviceEnrollmentKeys(
            storedCode,
            assignedCode,
            ReadNullableString(reader, table.DeviceIdColumn),
            ReadNullableString(reader, table.DevicePublicKeyColumn),
            ReadNullableString(reader, table.DeviceCertificateColumn));
    }

    public async Task<DeviceProfileUpdateResult> UpdateDeviceProfileAsync(
        string deviceCode,
        DeviceProfileWriteRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var table = options.PcTable;
        var assignedCode = NullIfWhiteSpace(request.AssignedDeviceCode);
        if (assignedCode is not null)
        {
            var conflict = $"""
                SELECT 1
                FROM {Name(options.SchemaName, table.TableName)}
                WHERE ({Name(table.DeviceCodeColumn)} = @assignedDeviceCode
                    OR {Name(table.AssignedDeviceCodeColumn)} = @assignedDeviceCode)
                  AND NOT ({PcLookupPredicate()});
                """;
            await using var conflictCommand = CreateCommand(connection, null, conflict);
            Bind(conflictCommand, ("@assignedDeviceCode", assignedCode));
            Bind(conflictCommand, ("@deviceCode", deviceCode));
            if (await conflictCommand.ExecuteScalarAsync(cancellationToken) is not null and not DBNull)
            {
                return DeviceProfileUpdateResult.Conflict;
            }
        }

        var sql = $"""
            UPDATE {Name(options.SchemaName, table.TableName)}
            SET {Name(table.AssignedHostNameColumn)} = @assignedHostName,
                {Name(table.AdminNotesColumn)} = @adminNotes,
                {Name(table.AssignedDeviceCodeColumn)} = @assignedDeviceCode
            WHERE {PcLookupPredicate()};
            """;
        await using var command = CreateCommand(connection, null, sql);
        Bind(command, ("@deviceCode", deviceCode));
        Bind(command, ("@assignedHostName", DbValue(NullIfWhiteSpace(request.AssignedHostName))));
        Bind(command, ("@adminNotes", DbValue(NullIfWhiteSpace(request.AdminNotes))));
        Bind(command, ("@assignedDeviceCode", DbValue(assignedCode)));
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0
            ? DeviceProfileUpdateResult.Updated
            : DeviceProfileUpdateResult.NotFound;
    }

    public async Task BindDeviceEnrollmentAsync(
        string deviceCode,
        string? deviceId,
        string devicePublicKey,
        string deviceCertificate,
        string issuedDeviceId,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var table = options.PcTable;
        var sql = $"""
            UPDATE {Name(options.SchemaName, table.TableName)}
            SET {Name(table.DeviceIdColumn)} = @issuedDeviceId,
                {Name(table.DevicePublicKeyColumn)} = @devicePublicKey,
                {Name(table.DeviceCertificateColumn)} = @deviceCertificate
            WHERE ({PcLookupPredicate()} OR (@deviceId IS NOT NULL AND {Name(table.DeviceIdColumn)} = @deviceId))
              AND {Name(table.DeviceIdColumn)} IS NULL;
            """;
        await using var command = CreateCommand(connection, null, sql);
        Bind(command, ("@deviceCode", deviceCode));
        Bind(command, ("@deviceId", DbValue(NullIfWhiteSpace(deviceId))));
        Bind(command, ("@issuedDeviceId", issuedDeviceId));
        Bind(command, ("@devicePublicKey", devicePublicKey));
        Bind(command, ("@deviceCertificate", deviceCertificate));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

}
