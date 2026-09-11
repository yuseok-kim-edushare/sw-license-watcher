using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Crypto;

public sealed class FileDeviceCertificateAuthority : IDeviceCertificateAuthority
{
    private readonly byte[] _publicKey;
    private readonly byte[] _privateKey;

    public FileDeviceCertificateAuthority(string path, ILogger<FileDeviceCertificateAuthority>? logger = null)
    {
        var stored = LoadOrCreate(path, logger);
        if (!MldsaDeviceCrypto.TryFromBase64(stored.PublicKey, out _publicKey) ||
            !MldsaDeviceCrypto.TryFromBase64(stored.PrivateKey, out _privateKey))
        {
            throw new InvalidOperationException("The ML-DSA-87 device CA key file is malformed.");
        }
    }

    public DeviceCertificateDocument Issue(string deviceId, string publicKeyBase64) =>
        MldsaDeviceCrypto.Issue(_privateKey, deviceId, publicKeyBase64);

    public bool TryVerify(DeviceCertificateDocument certificate, out string error) =>
        MldsaDeviceCrypto.TryVerifyCertificate(_publicKey, certificate, out error);

    private static StoredCaKey LoadOrCreate(string path, ILogger? logger)
    {
        if (File.Exists(path))
        {
            var existing = JsonSerializer.Deserialize(File.ReadAllText(path), DeviceCryptoJsonContext.Default.StoredCaKey);
            if (existing is null ||
                !string.Equals(existing.Alg, DeviceIdentityAlgorithms.Mldsa87, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Device CA at '{path}' is not an {DeviceIdentityAlgorithms.Mldsa87} key.");
            }

            return existing;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var (publicKey, privateKey) = MldsaDeviceCrypto.GenerateKeyPair();
        var created = new StoredCaKey(
            DeviceIdentityAlgorithms.Mldsa87,
            MldsaDeviceCrypto.ToBase64(publicKey),
            MldsaDeviceCrypto.ToBase64(privateKey));
        var json = JsonSerializer.Serialize(created, DeviceCryptoJsonContext.Default.StoredCaKey);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, path, false);
        logger?.LogInformation(
            "Created {Algorithm} device CA at {Path}. Signing is slower than ML-DSA-44/65 by design.",
            DeviceIdentityAlgorithms.Mldsa87,
            path);
        return created;
    }
}

internal sealed record StoredCaKey(string Alg, string PublicKey, string PrivateKey);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(StoredCaKey))]
internal sealed partial class DeviceCryptoJsonContext : JsonSerializerContext;
