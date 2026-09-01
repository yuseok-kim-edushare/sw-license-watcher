namespace SwLicenseWatcher.Core;

public static class SoftwarePolicyValidator
{
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

        error = string.Empty;
        return true;
    }
}
