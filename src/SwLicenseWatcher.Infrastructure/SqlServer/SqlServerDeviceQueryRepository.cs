using SwLicenseWatcher.Application;
using SwLicenseWatcher.Infrastructure.Relational;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

internal sealed class SqlServerDeviceQueryRepository(RelationalDataContext context) : IDeviceQuery
{
    public Task<(int TotalCount, List<DeviceSummary> Items)> ListDevicesAsync(
        int skip,
        int take,
        string? search,
        int? staleAfterHours,
        CancellationToken cancellationToken,
        string? sort = null,
        bool sortDescending = false) =>
        context.ListDevicesAsync(skip, take, search, staleAfterHours, cancellationToken, sort, sortDescending);

    public Task<DeviceDetail?> GetDeviceAsync(
        string deviceCode,
        string? classification,
        CancellationToken cancellationToken) =>
        context.GetDeviceAsync(deviceCode, classification, cancellationToken);

    public Task<DeviceAgentAssignment?> GetDeviceAssignmentAsync(
        string deviceCode,
        string? deviceId,
        CancellationToken cancellationToken,
        string? devicePublicKey = null) =>
        context.GetDeviceAssignmentAsync(deviceCode, deviceId, cancellationToken, devicePublicKey);

    public Task<DeviceEnrollmentKeys?> GetDeviceEnrollmentKeysAsync(
        string deviceCode,
        string? deviceId,
        CancellationToken cancellationToken,
        string? devicePublicKey = null) =>
        context.GetDeviceEnrollmentKeysAsync(deviceCode, deviceId, cancellationToken, devicePublicKey);

    public Task<DeviceProfileUpdateResult> UpdateDeviceProfileAsync(
        string deviceCode,
        DeviceProfileWriteRequest request,
        CancellationToken cancellationToken) =>
        context.UpdateDeviceProfileAsync(deviceCode, request, cancellationToken);

    public Task<bool> DeleteDeviceAsync(
        string deviceCode,
        CancellationToken cancellationToken) =>
        context.DeleteDeviceAsync(deviceCode, cancellationToken);

    public Task BindDeviceEnrollmentAsync(
        string deviceCode,
        string? deviceId,
        string devicePublicKey,
        string deviceCertificate,
        string issuedDeviceId,
        CancellationToken cancellationToken,
        DateTimeOffset? installedAtUtc = null) =>
        context.BindDeviceEnrollmentAsync(
            deviceCode, deviceId, devicePublicKey, deviceCertificate, issuedDeviceId, cancellationToken, installedAtUtc);
}
