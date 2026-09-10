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

}
