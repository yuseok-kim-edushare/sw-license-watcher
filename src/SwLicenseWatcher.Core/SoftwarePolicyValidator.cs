namespace SwLicenseWatcher.Core;

public static class SoftwarePolicyValidator
{
    public const int MaxClassificationBatchItems = 100;
    public const int MaxSoftwareNameLength = 256;

    public static bool TryValidate(SoftwarePolicyWriteRequest? request, out string error)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ProductName))
        {
            error = "The policy product name is required.";
            return false;
        }

        if (request.Classification is null || !Enum.IsDefined(request.Classification.Value))
        {
            error = "The policy classification must be white, managed, or black.";
            return false;
        }

        if (request.ProductName.Length > 256 ||
            (request.Publisher?.Length ?? 0) > 256 ||
            (request.VersionPattern?.Length ?? 0) > 64 ||
            (request.Notes?.Length ?? 0) > 1024)
        {
            error = "The policy exceeds persisted field limits.";
            return false;
        }

        if (!LicenseSourceNames.TryParse(request.DefaultLicenseSource, out _))
        {
            error = "The policy default license source must be company or byo.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateClassification(SoftwareClassificationWriteRequest? request, out string error)
    {
        if (request is null || request.Classification is null || !Enum.IsDefined(request.Classification.Value))
        {
            error = "The policy classification must be white, managed, or black.";
            return false;
        }

        if ((request.Publisher?.Length ?? 0) > 256 ||
            (request.Notes?.Length ?? 0) > 1024)
        {
            error = "The policy exceeds persisted field limits.";
            return false;
        }

        if (!LicenseSourceNames.TryParse(request.DefaultLicenseSource, out _))
        {
            error = "The policy default license source must be company or byo.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateClassificationBatch(
        SoftwareClassificationBatchWriteRequest? request,
        out IReadOnlyList<(string Name, SoftwareClassificationWriteRequest Write)> items,
        out string error)
    {
        items = [];
        if (request?.Items is null || request.Items.Count == 0)
        {
            error = "At least one software classification item is required.";
            return false;
        }

        if (request.Items.Count > MaxClassificationBatchItems)
        {
            error = $"At most {MaxClassificationBatchItems} software classification items are allowed.";
            return false;
        }

        var normalized = new List<(string Name, SoftwareClassificationWriteRequest Write)>(request.Items.Count);
        foreach (var item in request.Items)
        {
            if (item is null)
            {
                error = "Software name is required.";
                return false;
            }

            if (!TryValidateSoftwareName(item.Name, out var name, out error))
            {
                return false;
            }

            var write = new SoftwareClassificationWriteRequest(
                item.Classification,
                item.Publisher,
                item.DefaultLicenseSource);
            if (!TryValidateClassification(write, out error))
            {
                return false;
            }

            normalized.Add((name, write));
        }

        items = normalized;
        error = string.Empty;
        return true;
    }

    public static bool TryValidateSoftwareName(string? name, out string normalized, out string error)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            normalized = string.Empty;
            error = "Software name is required.";
            return false;
        }

        if (name.Length > MaxSoftwareNameLength)
        {
            normalized = string.Empty;
            error = $"Software name must be at most {MaxSoftwareNameLength} characters.";
            return false;
        }

        normalized = name.Trim();
        error = string.Empty;
        return true;
    }
}
