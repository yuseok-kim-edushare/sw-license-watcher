using System.Globalization;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal static class PolicyQueryApi
{
    public static void MapPolicyQuery(this WebApplication app)
    {
        app.MapGet("/api/policies", async (
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

            if (!TryNormalizePolicyClassification(classification, out var normalizedClassification, out var classificationError))
            {
                return Results.BadRequest(classificationError);
            }

            var csv = QueryList.WantsCsv(format);
            var (normalizedSkip, normalizedTake) = QueryList.NormalizePaging(skip, take, csv);
            var (totalCount, items) = await repository.ListPoliciesAsync(
                normalizedSkip, normalizedTake, search, normalizedClassification, cancellationToken);

            if (csv)
            {
                return InventoryCsv.File(
                    "policies.csv",
                    ["Id", "ProductName", "Publisher", "VersionPattern", "Classification", "Notes", "Enabled", "UpdatedAtUtc"],
                    items.Select(PolicyCsvRow));
            }

            return Results.Ok(new PolicyListResponse(normalizedSkip, normalizedTake, totalCount, items));
        });

        app.MapGet("/api/violations", async (
            SqlServerInventoryRepository repository,
            int? skip,
            int? take,
            string? search,
            string? since,
            string? format,
            CancellationToken cancellationToken) =>
        {
            if (!QueryList.TryValidateSearch(search, out var searchError))
            {
                return Results.BadRequest(searchError);
            }

            if (!QueryList.TryParseSince(since, out var parsedSince, out var sinceError))
            {
                return Results.BadRequest(sinceError);
            }

            var csv = QueryList.WantsCsv(format);
            var (normalizedSkip, normalizedTake) = QueryList.NormalizePaging(skip, take, csv);
            var (totalCount, items) = await repository.ListViolationsAsync(
                normalizedSkip, normalizedTake, search, parsedSince, cancellationToken);

            if (csv)
            {
                return InventoryCsv.File(
                    "violations.csv",
                    ["Id", "DeviceCode", "HostName", "SoftwareName", "SoftwareVersion", "Publisher", "PolicyId", "PolicyProductName", "Classification", "DetectedAtUtc", "LastSeenAtUtc"],
                    items.Select(ViolationCsvRow));
            }

            return Results.Ok(new ViolationListResponse(normalizedSkip, normalizedTake, totalCount, items));
        });
    }

    internal static bool TryNormalizePolicyClassification(string? classification, out string? normalized, out string error)
    {
        if (string.IsNullOrWhiteSpace(classification))
        {
            normalized = null;
            error = string.Empty;
            return true;
        }

        if (!SoftwarePolicyClassificationNames.TryParse(classification, out var parsed))
        {
            normalized = null;
            error = "classification must be white, managed, or black.";
            return false;
        }

        normalized = SoftwarePolicyClassificationNames.ToStorage(parsed);
        error = string.Empty;
        return true;
    }

    internal static string?[] PolicyCsvRow(SoftwarePolicyEntry policy) =>
    [
        policy.Id.ToString(CultureInfo.InvariantCulture),
        policy.ProductName,
        policy.Publisher,
        policy.VersionPattern,
        SoftwarePolicyClassificationNames.ToStorage(policy.Classification),
        policy.Notes,
        policy.Enabled ? "true" : "false",
        InventoryCsv.Format(policy.UpdatedAtUtc)
    ];

    internal static string?[] ViolationCsvRow(SoftwareViolationEntry violation) =>
    [
        violation.Id.ToString(CultureInfo.InvariantCulture),
        violation.DeviceCode,
        violation.HostName,
        violation.SoftwareName,
        violation.SoftwareVersion,
        violation.Publisher,
        violation.PolicyId.ToString(CultureInfo.InvariantCulture),
        violation.PolicyProductName,
        SoftwarePolicyClassificationNames.ToStorage(violation.Classification),
        InventoryCsv.Format(violation.DetectedAtUtc),
        InventoryCsv.Format(violation.LastSeenAtUtc)
    ];
}
