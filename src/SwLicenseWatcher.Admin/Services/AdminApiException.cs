namespace SwLicenseWatcher.Admin.Services;

public sealed class AdminApiException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public bool IsUnauthorized => StatusCode == 401;
}
