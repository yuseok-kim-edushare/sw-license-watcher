using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal static class InventoryQueryApi
{
    internal const int DefaultTake = 100;
    internal const int CsvDefaultTake = 10_000;
    internal const int MaxTake = 10_000;
    internal const int MaxSearchLength = 256;

    public static void MapInventoryQuery(this WebApplication app)
    {
        app.MapGet("/api/inventory/devices", async (
            SqlServerInventoryRepository repository,
            int? skip,
            int? take,
            string? search,
            int? staleAfterHours,
            string? format,
            CancellationToken cancellationToken) =>
        {
            if (staleAfterHours is < 1)
            {
                return Results.BadRequest("staleAfterHours must be a positive integer.");
            }

            if (search is { Length: > MaxSearchLength })
            {
                return Results.BadRequest($"search must be at most {MaxSearchLength} characters.");
            }

            var csv = WantsCsv(format);
            var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take, csv);
            var (totalCount, items) = await repository.ListDevicesAsync(
                normalizedSkip, normalizedTake, search, staleAfterHours, cancellationToken);

            if (csv)
            {
                return InventoryCsv.File(
                    "devices.csv",
                    ["DeviceCode", "HostName", "DomainName", "OperatingSystem", "AgentVersion", "LastHeartbeatUtc", "LastInventoryUtc"],
                    items.Select(device => new[]
                    {
                        device.DeviceCode,
                        device.HostName,
                        device.DomainName,
                        device.OperatingSystem,
                        device.AgentVersion,
                        InventoryCsv.Format(device.LastHeartbeatUtc),
                        InventoryCsv.Format(device.LastInventoryUtc)
                    }));
            }

            return Results.Ok(new DeviceListResponse(normalizedSkip, normalizedTake, totalCount, items));
        });

        app.MapGet("/api/inventory/devices/{deviceCode}", GetDeviceAsync);
        app.MapGet("/api/inventory/snapshots/{deviceCode}", GetDeviceAsync);

        app.MapGet("/api/inventory/software", async (
            SqlServerInventoryRepository repository,
            int? skip,
            int? take,
            string? search,
            string? format,
            CancellationToken cancellationToken) =>
        {
            if (search is { Length: > MaxSearchLength })
            {
                return Results.BadRequest($"search must be at most {MaxSearchLength} characters.");
            }

            var csv = WantsCsv(format);
            var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take, csv);
            var (totalCount, items) = await repository.ListSoftwareAsync(
                normalizedSkip, normalizedTake, search, cancellationToken);

            if (csv)
            {
                return InventoryCsv.File(
                    "software.csv",
                    ["Name", "Version", "DeviceCount"],
                    items.Select(entry => new[] { entry.Name, entry.Version, entry.DeviceCount.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
            }

            return Results.Ok(new SoftwareAggregateListResponse(normalizedSkip, normalizedTake, totalCount, items));
        });

        app.MapGet("/api/inventory/software/{name}/devices", async (
            string name,
            SqlServerInventoryRepository repository,
            int? skip,
            int? take,
            string? format,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.BadRequest("Software name is required.");
            }

            if (name.Length > MaxSearchLength)
            {
                return Results.BadRequest($"Software name must be at most {MaxSearchLength} characters.");
            }

            var csv = WantsCsv(format);
            var (normalizedSkip, normalizedTake) = NormalizePaging(skip, take, csv);
            var (totalCount, items) = await repository.ListSoftwareDevicesAsync(
                name, normalizedSkip, normalizedTake, cancellationToken);

            if (csv)
            {
                return InventoryCsv.File(
                    $"software-{InventoryCsv.SafeFileName(name)}-devices.csv",
                    ["Name", "DeviceCode", "HostName", "DomainName", "OperatingSystem", "AgentVersion", "LastHeartbeatUtc", "LastInventoryUtc", "Version", "Publisher"],
                    items.Select(device => new[]
                    {
                        name,
                        device.DeviceCode,
                        device.HostName,
                        device.DomainName,
                        device.OperatingSystem,
                        device.AgentVersion,
                        InventoryCsv.Format(device.LastHeartbeatUtc),
                        InventoryCsv.Format(device.LastInventoryUtc),
                        device.Version,
                        device.Publisher
                    }));
            }

            return Results.Ok(new SoftwareDeviceListResponse(name, normalizedSkip, normalizedTake, totalCount, items));
        });
    }

    private static async Task<IResult> GetDeviceAsync(
        string deviceCode,
        SqlServerInventoryRepository repository,
        string? format,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            return Results.BadRequest("deviceCode is required.");
        }

        var detail = await repository.GetDeviceAsync(deviceCode, cancellationToken);
        if (detail is null)
        {
            return Results.NotFound();
        }

        if (WantsCsv(format))
        {
            IEnumerable<string?[]> rows = detail.InstalledSoftware.Count == 0
                ? [DeviceSoftwareRow(detail, null)]
                : detail.InstalledSoftware.Select(entry => DeviceSoftwareRow(detail, entry));
            return InventoryCsv.File(
                $"device-{InventoryCsv.SafeFileName(detail.DeviceCode)}.csv",
                ["DeviceCode", "HostName", "DomainName", "OperatingSystem", "AgentVersion", "LastHeartbeatUtc", "LastInventoryUtc", "Name", "Version", "Publisher", "InstallLocation", "DiscoveryScope", "DiscoverySource"],
                rows);
        }

        return Results.Ok(detail);
    }

    private static string?[] DeviceSoftwareRow(DeviceDetail detail, InstalledSoftwareEntry? entry) =>
    [
        detail.DeviceCode,
        detail.HostName,
        detail.DomainName,
        detail.OperatingSystem,
        detail.AgentVersion,
        InventoryCsv.Format(detail.LastHeartbeatUtc),
        InventoryCsv.Format(detail.LastInventoryUtc),
        entry?.Name,
        entry?.Version,
        entry?.Publisher,
        entry?.InstallLocation,
        entry?.DiscoveryScope,
        entry?.DiscoverySource
    ];

    private static bool WantsCsv(string? format) =>
        string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);

    private static (int Skip, int Take) NormalizePaging(int? skip, int? take, bool csv)
    {
        var normalizedSkip = Math.Max(skip.GetValueOrDefault(), 0);
        var requestedTake = take ?? (csv ? CsvDefaultTake : DefaultTake);
        var normalizedTake = Math.Clamp(requestedTake, 1, MaxTake);
        return (normalizedSkip, normalizedTake);
    }
}
