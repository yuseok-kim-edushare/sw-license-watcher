using System.Collections.Concurrent;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Application;

public static class InventoryDecisions
{
    public static List<InstalledSoftwareEntry> ApplyLicenseSources(
        IReadOnlyList<InstalledSoftwareEntry> installed,
        IReadOnlyDictionary<string, string> assignments,
        IReadOnlyList<SoftwarePolicyEntry> policies)
    {
        var result = new List<InstalledSoftwareEntry>(installed.Count);
        foreach (var entry in installed)
        {
            assignments.TryGetValue(entry.Name, out var assignment);
            var match = SoftwarePolicyMatcher.Match(entry, policies);
            var policyDefault = match.Classification == SoftwarePolicyClassification.Managed
                ? match.Policy?.DefaultLicenseSource
                : null;
            var classification = entry.Classification ?? match.StoredClassification;
            result.Add(entry with
            {
                LicenseSource = LicenseSourceNames.ResolveEffective(assignment, classification, policyDefault),
                LicenseSourceOverride = assignment
            });
        }

        return result;
    }

    public static List<SoftwareDevice> ApplySoftwareDeviceLicenseSources(
        string softwareName,
        IReadOnlyList<SoftwareDevice> items,
        IReadOnlyList<SoftwarePolicyEntry> policies)
    {
        var result = new List<SoftwareDevice>(items.Count);
        foreach (var item in items)
        {
            var match = SoftwarePolicyMatcher.Match(
                new InstalledSoftwareEntry(softwareName, item.Version, item.Publisher, null, "query", "query", item.Classification),
                policies);
            var policyDefault = match.Classification == SoftwarePolicyClassification.Managed
                ? match.Policy?.DefaultLicenseSource
                : null;
            LicenseSourceNames.TryParse(item.LicenseSourceOverride, out var assignment);
            result.Add(item with
            {
                LicenseSource = LicenseSourceNames.ResolveEffective(assignment, item.Classification, policyDefault),
                LicenseSourceOverride = assignment
            });
        }

        return result;
    }

    public static Dictionary<string, NewBlacklistViolation> CollectCurrentViolations(
        IReadOnlyList<SoftwarePolicyMatch> matches)
    {
        var current = new Dictionary<string, NewBlacklistViolation>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in matches)
        {
            if (!match.IsBlacklisted || match.Policy is null)
            {
                continue;
            }

            var name = Truncate(match.Software.Name.Trim(), 256);
            if (!string.IsNullOrEmpty(name))
            {
                current.TryAdd(name, new NewBlacklistViolation(match.Software, match.Policy));
            }
        }

        return current;
    }

    public static IReadOnlyList<NewBlacklistViolation> FindNewlyDetectedViolations(
        IReadOnlyDictionary<string, NewBlacklistViolation> current,
        IReadOnlyCollection<string> existingSoftwareNames)
    {
        var existing = new HashSet<string>(existingSoftwareNames, StringComparer.OrdinalIgnoreCase);
        var added = new List<NewBlacklistViolation>();
        foreach (var (name, violation) in current)
        {
            if (!existing.Contains(name))
            {
                added.Add(violation);
            }
        }

        return added;
    }

    public static List<StalePcHeartbeat> TakeNewlyStale(
        IReadOnlyList<StalePcHeartbeat> stalePcs,
        ConcurrentDictionary<string, byte> notifiedDeviceCodes)
    {
        var staleCodes = new HashSet<string>(stalePcs.Select(pc => pc.DeviceCode), StringComparer.OrdinalIgnoreCase);
        foreach (var deviceCode in notifiedDeviceCodes.Keys)
        {
            if (!staleCodes.Contains(deviceCode))
            {
                notifiedDeviceCodes.TryRemove(deviceCode, out _);
            }
        }

        return stalePcs.Where(pc => notifiedDeviceCodes.TryAdd(pc.DeviceCode, 0)).ToList();
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
