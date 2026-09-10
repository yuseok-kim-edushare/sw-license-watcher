using SwLicenseWatcher.Application;
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
            IDeviceQuery repository,
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
            ISoftwareQuery repository,
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
                    ["Name", "Version", "Classification", "DeviceCount", "CompanyCount", "ByoCount", "UnassignedCount"],
                    items.Select(entry => new[]
                    {
                        entry.Name,
                        entry.Version,
                        entry.Classification,
                        entry.DeviceCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        entry.CompanyCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        entry.ByoCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        entry.UnassignedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }));
            }

            return Results.Ok(new SoftwareAggregateListResponse(normalizedSkip, normalizedTake, totalCount, items));
        });

        app.MapGet("/api/inventory/software/{name}/devices", async (
            string name,
            ISoftwareQuery repository,
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
                    ["Name", "DeviceCode", "HostName", "DomainName", "OperatingSystem", "AgentVersion", "LastHeartbeatUtc", "LastInventoryUtc", "Version", "Publisher", "Classification", "LicenseSource", "LicenseSourceOverride"],
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
                        device.Classification,
                        device.LicenseSource,
                        device.LicenseSourceOverride
                    }));
            }

            return Results.Ok(new SoftwareDeviceListResponse(name, normalizedSkip, normalizedTake, totalCount, items));
        });

        app.MapPut("/api/inventory/software/classifications", async (
            SoftwareClassificationBatchWriteRequest request,
            IPolicyStore repository,
            CancellationToken cancellationToken) =>
        {
            if (!SoftwarePolicyValidator.TryValidateClassificationBatch(request, out var items, out var validationError))
            {
                return Results.BadRequest(validationError);
            }

            var saved = await repository.UpsertSoftwareClassificationsAsync(items, cancellationToken);
            return Results.Ok(new SoftwareClassificationBatchResponse(saved.Count, saved));
        });

        app.MapPut("/api/inventory/software/{name}/classification", async (
            string name,
            SoftwareClassificationWriteRequest request,
            IPolicyStore repository,
            CancellationToken cancellationToken) =>
        {
            if (!SoftwarePolicyValidator.TryValidateSoftwareName(name, out var productName, out var nameError))
            {
                return Results.BadRequest(nameError);
            }

            if (!SoftwarePolicyValidator.TryValidateClassification(request, out var validationError))
            {
                return Results.BadRequest(validationError);
            }

            var saved = await repository.UpsertSoftwareClassificationAsync(productName, request, cancellationToken);
            return Results.Ok(saved);
        });

        app.MapPut("/api/inventory/devices/{deviceCode}/software/{name}/license-source", async (
            string deviceCode,
            string name,
            DeviceSoftwareLicenseSourceWriteRequest request,
            IPolicyStore repository,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(deviceCode))
            {
                return Results.BadRequest("deviceCode is required.");
            }

            if (!SoftwarePolicyValidator.TryValidateSoftwareName(name, out var softwareName, out var nameError))
            {
                return Results.BadRequest(nameError);
            }

            if (!TryNormalizeLicenseSource(request.LicenseSource, out var licenseSource, out var sourceError))
            {
                return Results.BadRequest(sourceError);
            }

            return await repository.SetDeviceSoftwareLicenseSourceAsync(
                deviceCode, softwareName, licenseSource, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound();
        });
    }

    private static async Task<IResult> GetDeviceAsync(
        string deviceCode,
        IDeviceQuery repository,
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
                ["DeviceCode", "HostName", "DomainName", "OperatingSystem", "AgentVersion", "LastHeartbeatUtc", "LastInventoryUtc", "Name", "Version", "Publisher", "InstallLocation", "DiscoveryScope", "DiscoverySource", "Classification", "LicenseSource", "LicenseSourceOverride"],
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
        entry?.Classification,
        entry?.LicenseSource,
        entry?.LicenseSourceOverride
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

    internal static bool TryNormalizeLicenseSource(string? licenseSource, out string? normalized, out string error)
    {
        if (!LicenseSourceNames.TryParse(licenseSource, out normalized))
        {
            error = "licenseSource must be company or byo.";
            return false;
        }

        error = string.Empty;
        return true;
    }

}
