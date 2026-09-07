namespace SwLicenseWatcher.Setup.Core;

public static class CompanySettingsValidator
{
    public const int MinimumTokenLength = 32;

    public static bool TryValidate(CompanySettings? settings, out string error)
    {
        error = string.Empty;
        if (settings is null)
        {
            error = "company.json is missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.ServerBaseUrl))
        {
            error = "ServerBaseUrl is required.";
            return false;
        }

        if (!Uri.TryCreate(settings.ServerBaseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            error = "ServerBaseUrl must be an absolute URL.";
            return false;
        }

        var https = uri.Scheme == Uri.UriSchemeHttps;
        var httpLoopback = uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
        if (!https && !httpLoopback)
        {
            error = "ServerBaseUrl must use HTTPS (HTTP is allowed only for loopback diagnostics).";
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.AgentToken) || settings.AgentToken.Trim().Length < MinimumTokenLength)
        {
            error = $"AgentToken must contain at least {MinimumTokenLength} characters.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.Version))
        {
            error = "Version is required.";
            return false;
        }

        return true;
    }

    public static CompanySettings Normalize(CompanySettings settings)
    {
        return new CompanySettings
        {
            ServerBaseUrl = settings.ServerBaseUrl.Trim(),
            AgentToken = settings.AgentToken.Trim(),
            Version = settings.Version.Trim()
        };
    }
}
