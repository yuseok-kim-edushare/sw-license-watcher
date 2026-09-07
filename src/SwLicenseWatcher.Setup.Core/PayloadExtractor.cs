using System.Security.Cryptography;

namespace SwLicenseWatcher.Setup.Core;

public static class PayloadExtractor
{
    public static string ExtractFromExecutable(string exePath, string? tempRoot = null)
    {
        var zip = AttachedPayload.ReadZip(exePath);
        return ExtractZip(zip, tempRoot);
    }

    public static string ExtractZip(byte[] zip, string? tempRoot = null)
    {
        ArgumentNullException.ThrowIfNull(zip);
        var hash = Convert.ToHexString(SHA256.HashData(zip))[..8].ToLowerInvariant();
        var root = string.IsNullOrWhiteSpace(tempRoot)
            ? Path.Combine(Path.GetTempPath(), "SwLicenseWatcher-setup")
            : tempRoot;
        var destination = Path.Combine(root, hash);
        var marker = Path.Combine(destination, ".extracted");
        if (File.Exists(marker) && File.Exists(PayloadLayout.GetSetupUiPath(destination)))
        {
            return destination;
        }

        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }

        ZipExtractor.ExtractToDirectory(zip, destination);
        PayloadZipBuilder.ValidateLayout(destination);
        File.WriteAllText(marker, hash);
        return destination;
    }
}
