using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal sealed class UpdatePackageSignException(string message) : Exception(message);

internal interface IUpdatePackageSigner
{
    bool SignClientPackages { get; }

    bool SignServerPackages { get; }

    bool ShouldSignClient { get; }

    bool ShouldSignServer { get; }

    Task SignBinariesAsync(string directory, CancellationToken cancellationToken);
}

internal static class UpdateBinaryFiles
{
    public static string[] Enumerate(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new UpdatePackageSignException("The package directory does not exist.");
        }

        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (files.Length == 0)
        {
            throw new UpdatePackageSignException("The package contains no EXE or DLL to sign.");
        }

        return files;
    }
}

internal static class ArtifactSigningMetadata
{
    public static string Format(string endpoint, string accountName, string certificateProfileName) =>
        "{\"Endpoint\":" + Quote(endpoint) +
        ",\"CodeSigningAccountName\":" + Quote(accountName) +
        ",\"CertificateProfileName\":" + Quote(certificateProfileName) +
        "}";

    private static string Quote(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}

internal sealed class SignToolRequest
{
    public required string SignToolPath { get; init; }

    public required IReadOnlyList<string> Files { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);

    public string? TimestampUrl { get; init; }

    public string? DlibPath { get; init; }

    public string? MetadataPath { get; init; }

    public string? CertificatePath { get; init; }

    public string? CertificatePassword { get; init; }
}

internal static class SignToolRunner
{
    public static async Task SignAsync(SignToolRequest request, CancellationToken cancellationToken)
    {
        if (request.Files.Count == 0)
        {
            throw new UpdatePackageSignException("The package contains no EXE or DLL to sign.");
        }

        if (!File.Exists(request.SignToolPath))
        {
            throw new UpdatePackageSignException($"signtool was not found at {request.SignToolPath}.");
        }

        var deadline = DateTime.UtcNow + request.Timeout;
        foreach (var file in request.Files)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                throw new UpdatePackageSignException("Signing the package timed out.");
            }

            await SignFileAsync(request, file, remaining, cancellationToken);
        }
    }

    private static async Task SignFileAsync(
        SignToolRequest request,
        string file,
        TimeSpan remaining,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = request.SignToolPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("sign");
        start.ArgumentList.Add("/fd");
        start.ArgumentList.Add("SHA256");
        if (!string.IsNullOrWhiteSpace(request.TimestampUrl))
        {
            start.ArgumentList.Add("/tr");
            start.ArgumentList.Add(request.TimestampUrl);
            start.ArgumentList.Add("/td");
            start.ArgumentList.Add("SHA256");
        }

        if (!string.IsNullOrWhiteSpace(request.CertificatePath))
        {
            start.ArgumentList.Add("/f");
            start.ArgumentList.Add(request.CertificatePath);
            start.ArgumentList.Add("/p");
            start.ArgumentList.Add(request.CertificatePassword ?? string.Empty);
        }
        else
        {
            start.ArgumentList.Add("/dlib");
            start.ArgumentList.Add(request.DlibPath ?? string.Empty);
            start.ArgumentList.Add("/dmdf");
            start.ArgumentList.Add(request.MetadataPath ?? string.Empty);
        }

        start.ArgumentList.Add(file);
        using var process = Process.Start(start) ??
            throw new UpdatePackageSignException("signtool could not be started.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(remaining);
        var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        string output;
        string error;
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            output = await outputTask;
            error = await errorTask;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new UpdatePackageSignException($"signtool timed out while signing {Path.GetFileName(file)}.");
        }

        if (process.ExitCode == 0)
        {
            return;
        }

        var detail = string.IsNullOrWhiteSpace(error) ? output : error;
        detail = detail.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        if (detail.Length > 400)
        {
            detail = detail[..400];
        }

        throw new UpdatePackageSignException(
            string.IsNullOrWhiteSpace(detail)
                ? $"signtool failed for {Path.GetFileName(file)} with exit code {process.ExitCode}."
                : $"signtool failed for {Path.GetFileName(file)}: {detail}");
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}

internal sealed class DisabledUpdatePackageSigner : IUpdatePackageSigner
{
    public static DisabledUpdatePackageSigner Instance { get; } = new();

    public bool SignClientPackages => false;

    public bool SignServerPackages => false;

    public bool ShouldSignClient => false;

    public bool ShouldSignServer => false;

    public Task SignBinariesAsync(string directory, CancellationToken cancellationToken) =>
        Task.FromException(new UpdatePackageSignException("Artifact Signing is not configured."));
}

internal sealed class ArtifactUpdatePackageSigner(
    IOptions<ArtifactSigningOptions> options,
    ILogger<ArtifactUpdatePackageSigner> logger) : IUpdatePackageSigner, IPackageFileSigner
{
    public bool SignClientPackages => options.Value.SignClientPackages;

    public bool SignServerPackages => options.Value.SignServerPackages;

    public bool ShouldSignClient => options.Value.ShouldSignClient;

    public bool ShouldSignServer => options.Value.ShouldSignServer;

    public async Task SignBinariesAsync(string directory, CancellationToken cancellationToken)
    {
        var configured = options.Value;
        if (!configured.IsConfigured)
        {
            throw new UpdatePackageSignException("Artifact Signing is not configured.");
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new UpdatePackageSignException("Artifact Signing requires a Windows API host.");
        }

        if (!Uri.TryCreate(configured.Endpoint.Trim(), UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new UpdatePackageSignException("Signing:Artifact:Endpoint must be an absolute HTTPS URI.");
        }

        if (!File.Exists(configured.DlibPath))
        {
            throw new UpdatePackageSignException($"Azure.CodeSigning.Dlib.dll was not found at {configured.DlibPath}.");
        }

        var files = UpdateBinaryFiles.Enumerate(directory);
        var metadataPath = Path.Combine(Path.GetTempPath(), "slw-signing-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(
                metadataPath,
                ArtifactSigningMetadata.Format(endpoint.AbsoluteUri, configured.AccountName.Trim(), configured.CertificateProfileName.Trim()),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            logger.LogInformation("Signing {Count} binaries with Azure Artifact Signing.", files.Length);
            await SignToolRunner.SignAsync(
                new SignToolRequest
                {
                    SignToolPath = configured.SignToolPath.Trim(),
                    Files = files,
                    Timeout = configured.EffectiveTimeout,
                    TimestampUrl = configured.EffectiveTimestampUrl,
                    DlibPath = configured.DlibPath.Trim(),
                    MetadataPath = metadataPath
                },
                cancellationToken);
            AuthenticodeTrust.RequireTrusted(files);
        }
        finally
        {
            if (File.Exists(metadataPath))
            {
                File.Delete(metadataPath);
            }
        }
    }
}

internal sealed class LocalCertificatePackageSigner(
    string signToolPath,
    string certificatePath,
    string certificatePassword,
    bool signClient = true,
    bool signServer = false) : IUpdatePackageSigner
{
    public bool SignClientPackages => signClient;

    public bool SignServerPackages => signServer;

    public bool ShouldSignClient => signClient;

    public bool ShouldSignServer => signServer;

    public async Task SignBinariesAsync(string directory, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new UpdatePackageSignException("Code signing requires Windows.");
        }

        var files = UpdateBinaryFiles.Enumerate(directory);
        await SignToolRunner.SignAsync(
            new SignToolRequest
            {
                SignToolPath = signToolPath,
                Files = files,
                Timeout = TimeSpan.FromMinutes(2),
                CertificatePath = certificatePath,
                CertificatePassword = certificatePassword
            },
            cancellationToken);
        foreach (var file in files)
        {
            if (!SignedFileCertificate.HasCertificate(file))
            {
                throw new UpdatePackageSignException($"Signing did not attach a certificate to {Path.GetFileName(file)}.");
            }
        }
    }
}

internal static class SignedFileCertificate
{
    public static bool HasCertificate(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var certificate = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return !string.IsNullOrWhiteSpace(certificate.GetCertHashString());
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
