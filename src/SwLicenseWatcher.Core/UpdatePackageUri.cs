namespace SwLicenseWatcher.Core;

public static class UpdatePackageUri
{
    public const string AbsoluteHttpsOrLoopbackHttpRule =
        "The Worker update package URL must be an absolute HTTPS URI (HTTP is allowed only for loopback).";

    public static bool IsAllowed(Uri uri) =>
        uri.IsAbsoluteUri &&
        (uri.Scheme == Uri.UriSchemeHttps ||
         (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

    public static bool TryCreateAllowed(string? value, out Uri uri)
    {
        uri = null!;
        return Uri.TryCreate(value, UriKind.Absolute, out uri!) && IsAllowed(uri);
    }
}
