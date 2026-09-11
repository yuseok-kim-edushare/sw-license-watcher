using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Infrastructure.SqlServer;

internal sealed class SqlServerDeviceQueryRepository(SqlServerDataContext context) : IDeviceQuery
{
    public Task<(int TotalCount, List<DeviceSummary> Items)> ListDevicesAsync(
        int skip,
        int take,
        string? search,
        int? staleAfterHours,
        CancellationToken cancellationToken) =>
        context.ListDevicesAsync(skip, take, search, staleAfterHours, cancellationToken);

    public Task<DeviceDetail?> GetDeviceAsync(
        string deviceCode,
        string? classification,
        CancellationToken cancellationToken) =>
        context.GetDeviceAsync(deviceCode, classification, cancellationToken);

    public Task<DeviceAgentAssignment?> GetDeviceAssignmentAsync(
        string deviceCode,
        string? deviceId,
        CancellationToken cancellationToken) =>
        context.GetDeviceAssignmentAsync(deviceCode, deviceId, cancellationToken);

    public Task<DeviceEnrollmentKeys?> GetDeviceEnrollmentKeysAsync(
        string deviceCode,
        string? deviceId,
        CancellationToken cancellationToken) =>
        context.GetDeviceEnrollmentKeysAsync(deviceCode, deviceId, cancellationToken);

    public Task<DeviceProfileUpdateResult> UpdateDeviceProfileAsync(
        string deviceCode,
        DeviceProfileWriteRequest request,
        CancellationToken cancellationToken) =>
        context.UpdateDeviceProfileAsync(deviceCode, request, cancellationToken);

    public Task BindDeviceEnrollmentAsync(
        string deviceCode,
        string? deviceId,
        string devicePublicKey,
        string deviceCertificate,
        string issuedDeviceId,
        CancellationToken cancellationToken) =>
        context.BindDeviceEnrollmentAsync(
            deviceCode, deviceId, devicePublicKey, deviceCertificate, issuedDeviceId, cancellationToken);
}
