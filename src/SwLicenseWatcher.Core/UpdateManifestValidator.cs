namespace SwLicenseWatcher.Core;

public static class UpdateManifestValidator
{
    public const int MinRollbackAfterMinutes = 1;
    public const int MaxRollbackAfterMinutes = 60;

    public static bool TryValidate(UpdateManifest? manifest, out string error)
    {
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.TargetServiceName))
        {
            error = "The Worker update pin target service name is required.";
            return false;
        }

        if (manifest.TargetServiceName.Length > 128 ||
            string.IsNullOrWhiteSpace(manifest.Version) ||
            manifest.Version.Length > 32 ||
            string.IsNullOrWhiteSpace(manifest.PackageUrl) ||
            manifest.PackageUrl.Length > 2048)
        {
            error = "The Worker update pin exceeds persisted field limits.";
            return false;
        }

        if (!Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out var packageUri) ||
            packageUri.Scheme != Uri.UriSchemeHttps)
        {
            error = "The Worker update package URL must be an absolute HTTPS URI.";
            return false;
        }

        var sha = manifest.Sha256?.Trim() ?? string.Empty;
        var placeholder = IsPlaceholderToken(manifest.Version) ||
            IsPlaceholderToken(sha) ||
            IsPlaceholderToken(manifest.PackageUrl);
        if (placeholder)
        {
            if (sha.Length > 64)
            {
                error = "The Worker update pin SHA-256 must be at most 64 characters.";
                return false;
            }
        }
        else if (sha.Length != 64 || !sha.All(Uri.IsHexDigit))
        {
            error = "The Worker update pin must contain a 64-character SHA-256 digest.";
            return false;
        }

        if (manifest.RollbackAfterMinutes is < MinRollbackAfterMinutes or > MaxRollbackAfterMinutes)
        {
            error = "The rollback timeout must be between 1 and 60 minutes.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool IsPlaceholderToken(string? value) =>
        !string.IsNullOrEmpty(value) &&
        (value.Contains("REPLACE_ME", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("REPLACE_WITH", StringComparison.OrdinalIgnoreCase));
}
