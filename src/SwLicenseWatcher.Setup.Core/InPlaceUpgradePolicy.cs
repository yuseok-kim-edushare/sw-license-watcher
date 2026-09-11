namespace SwLicenseWatcher.Setup.Core;

public static class InPlaceUpgradePolicy
{
    public static readonly Version KeyAuthorizationSince = new(0, 1, 0);

    public static bool RequiresServerKeyAuthorization(string? installedVersion)
    {
        return TryParse(installedVersion, out var version) && version >= new Version(0, 1, 0, 0);
    }

    public static bool TryParse(string? installedVersion, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(installedVersion))
        {
            return false;
        }

        var core = installedVersion.Trim().Split('-', 2)[0].Split('+', 2)[0];
        if (!Version.TryParse(core, out var parsed) || parsed is null)
        {
            return false;
        }

        version = new Version(
            parsed.Major,
            parsed.Minor,
            Math.Max(parsed.Build, 0),
            Math.Max(parsed.Revision, 0));
        return true;
    }
}
