using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SwLicenseWatcher.Api.Tests;

internal static class TestCodeSigning
{
    public static string? FindSignTool()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var roots = new[]
        {
            Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
            Environment.GetEnvironmentVariable("ProgramFiles")
        };
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var bin = Path.Combine(root, "Windows Kits", "10", "bin");
            if (!Directory.Exists(bin))
            {
                continue;
            }

            var found = Directory.EnumerateFiles(bin, "signtool.exe", SearchOption.AllDirectories)
                .Where(path => path.Contains($"{Path.DirectorySeparatorChar}x64{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    public static string WritePfx(string directory, string password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=SwLicenseWatcher Test Code Signing",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: false));
        var usage = new OidCollection { new Oid("1.3.6.1.5.5.7.3.3") };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usage, critical: false));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var path = Path.Combine(directory, "test-codesign.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        return path;
    }

    public static byte[] ZipAssembly(string entryName)
    {
        var source = typeof(TestCodeSigning).Assembly.Location;
        using var stream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var input = File.OpenRead(source);
            using var output = entry.Open();
            input.CopyTo(output);
        }

        return stream.ToArray();
    }
}

public class PackageSigningWorkerTests
{
    [Fact]
    public async Task Sign_request_runs_on_the_background_loop()
    {
        var inner = new RecordingFileSigner();
        var worker = new PackageSigningWorker(
            Microsoft.Extensions.Options.Options.Create(new SwLicenseWatcher.Core.ArtifactSigningOptions()),
            inner,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PackageSigningWorker>.Instance);
        using var stop = new CancellationTokenSource();
        await worker.StartAsync(stop.Token);
        try
        {
            var directory = Path.GetTempPath();
            await worker.SignBinariesAsync(directory, CancellationToken.None);
            Assert.Equal(directory, inner.Directory);
        }
        finally
        {
            await stop.CancelAsync();
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private sealed class RecordingFileSigner : IPackageFileSigner
    {
        public string? Directory { get; private set; }

        public Task SignBinariesAsync(string directory, CancellationToken cancellationToken)
        {
            Directory = directory;
            return Task.CompletedTask;
        }
    }
}

public class ArtifactSigningMetadataTests
{
    [Fact]
    public void Metadata_names_the_endpoint_account_and_profile()
    {
        var json = ArtifactSigningMetadata.Format(
            "https://eus.codesigning.azure.net/",
            "contoso",
            "contoso-profile");

        Assert.Contains("\"Endpoint\":\"https://eus.codesigning.azure.net/\"", json, StringComparison.Ordinal);
        Assert.Contains("\"CodeSigningAccountName\":\"contoso\"", json, StringComparison.Ordinal);
        Assert.Contains("\"CertificateProfileName\":\"contoso-profile\"", json, StringComparison.Ordinal);
    }
}
