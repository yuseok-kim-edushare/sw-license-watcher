using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal static class InventoryQueryApi
{
    internal const int DefaultTake = QueryList.DefaultTake;
    internal const int CsvDefaultTake = QueryList.CsvDefaultTake;
    internal const int MaxTake = QueryList.MaxTake;
    internal const int MaxSearchLength = QueryList.MaxSearchLength;

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

            if (!QueryList.TryValidateSearch(search, out var searchError))
            {
                return Results.BadRequest(searchError);
            }

            var csv = QueryList.WantsCsv(format);
            var (normalizedSkip, normalizedTake) = QueryList.NormalizePaging(skip, take, csv);
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
            string? classification,
            string? format,
            CancellationToken cancellationToken) =>
        {
            if (!QueryList.TryValidateSearch(search, out var searchError))
            {
                return Results.BadRequest(searchError);
            }

            if (!TryNormalizeClassification(classification, out var normalizedClassification, out var classificationError))
            {
                return Results.BadRequest(classificationError);
            }

            var csv = QueryList.WantsCsv(format);
            var (normalizedSkip, normalizedTake) = QueryList.NormalizePaging(skip, take, csv);
            var (totalCount, items) = await repository.ListSoftwareAsync(
                normalizedSkip, normalizedTake, search, normalizedClassification, cancellationToken);

            if (csv)
            {
                return InventoryCsv.File(
                    "software.csv",
                    ["Name", "Version", "Classification", "DeviceCount"],
                    items.Select(entry => new[]
                    {
                        entry.Name,
                        entry.Version,
                        entry.Classification,
                        entry.DeviceCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }));
            }

            return Results.Ok(new SoftwareAggregateListResponse(normalizedSkip, normalizedTake, totalCount, items));
        });

        app.MapGet("/api/inventory/software/{name}/devices", async (
            string name,
            SqlServerInventoryRepository repository,
            int? skip,
            int? take,
            string? classification,
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

            if (!TryNormalizeClassification(classification, out var normalizedClassification, out var classificationError))
            {
                return Results.BadRequest(classificationError);
            }

            var csv = QueryList.WantsCsv(format);
            var (normalizedSkip, normalizedTake) = QueryList.NormalizePaging(skip, take, csv);
            var (totalCount, items) = await repository.ListSoftwareDevicesAsync(
                name, normalizedSkip, normalizedTake, normalizedClassification, cancellationToken);

            if (csv)
            {
                return InventoryCsv.File(
                    $"software-{InventoryCsv.SafeFileName(name)}-devices.csv",
                    ["Name", "DeviceCode", "HostName", "DomainName", "OperatingSystem", "AgentVersion", "LastHeartbeatUtc", "LastInventoryUtc", "Version", "Publisher", "Classification"],
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
                        device.Publisher,
                        device.Classification
                    }));
            }

            return Results.Ok(new SoftwareDeviceListResponse(name, normalizedSkip, normalizedTake, totalCount, items));
        });
    }

    private static async Task<IResult> GetDeviceAsync(
        string deviceCode,
        SqlServerInventoryRepository repository,
        string? classification,
        string? format,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceCode))
        {
            return Results.BadRequest("deviceCode is required.");
        }

        if (!TryNormalizeClassification(classification, out var normalizedClassification, out var classificationError))
        {
            return Results.BadRequest(classificationError);
        }

        var detail = await repository.GetDeviceAsync(deviceCode, normalizedClassification, cancellationToken);
        if (detail is null)
        {
            return Results.NotFound();
        }

        if (QueryList.WantsCsv(format))
        {
            IEnumerable<string?[]> rows = detail.InstalledSoftware.Count == 0
                ? [DeviceSoftwareRow(detail, null)]
                : detail.InstalledSoftware.Select(entry => DeviceSoftwareRow(detail, entry));
            return InventoryCsv.File(
                $"device-{InventoryCsv.SafeFileName(detail.DeviceCode)}.csv",
                ["DeviceCode", "HostName", "DomainName", "OperatingSystem", "AgentVersion", "LastHeartbeatUtc", "LastInventoryUtc", "Name", "Version", "Publisher", "InstallLocation", "DiscoveryScope", "DiscoverySource", "Classification"],
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
        entry?.DiscoverySource,
        entry?.Classification
    ];

    internal static bool TryNormalizeClassification(string? classification, out string? normalized, out string error)
    {
        if (string.IsNullOrWhiteSpace(classification))
        {
            normalized = null;
            error = string.Empty;
            return true;
        }

        if (!SoftwarePolicyClassificationNames.TryParseInstalledSoftware(classification, out var storage))
        {
            normalized = null;
            error = "classification must be white, managed, black, or unclassified.";
            return false;
        }

        normalized = storage;
        error = string.Empty;
        return true;
    }

}
