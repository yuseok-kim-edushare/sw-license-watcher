using System.Security.Cryptography;
using System.Text.Json;
using SwLicenseWatcher.Core;
using SwLicenseWatcher.Crypto;

namespace SwLicenseWatcher.Agent.Worker;

public sealed class AgentDeviceIdentityStore(string filePath, ILocalStateProtector protector)
{
    private readonly object _gate = new();
    private StoredDeviceIdentity _identity = Read(filePath, protector);

    public StoredDeviceIdentity Current
    {
        get
        {
            lock (_gate)
            {
                return _identity;
            }
        }
    }

    public StoredDeviceIdentity Ensure()
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(_identity.PublicKey) && !string.IsNullOrWhiteSpace(_identity.PrivateKey))
            {
                return _identity;
            }

            var (publicKey, privateKey) = MldsaDeviceCrypto.GenerateKeyPair();
            _identity = new StoredDeviceIdentity(
                null,
                MldsaDeviceCrypto.ToBase64(publicKey),
                MldsaDeviceCrypto.ToBase64(privateKey),
                null);
        }

        Write();
        return Current;
    }

    public void ApplyCertificate(string? deviceId, string? certificate)
    {
        if (string.IsNullOrWhiteSpace(deviceId) && string.IsNullOrWhiteSpace(certificate))
        {
            return;
        }

        lock (_gate)
        {
            if (string.Equals(_identity.DeviceId, deviceId, StringComparison.Ordinal) &&
                string.Equals(_identity.Certificate, certificate, StringComparison.Ordinal))
            {
                return;
            }

            _identity = _identity with
            {
                DeviceId = string.IsNullOrWhiteSpace(deviceId) ? _identity.DeviceId : deviceId,
                Certificate = string.IsNullOrWhiteSpace(certificate) ? _identity.Certificate : certificate
            };
        }

        Write();
    }

    private void Write()
    {
        StoredDeviceIdentity identity;
        lock (_gate)
        {
            identity = _identity;
        }

        var json = JsonSerializer.Serialize(identity, InventoryJsonSerializerContext.Default.StoredDeviceIdentity);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = filePath + ".tmp";
        File.WriteAllText(temporaryPath, protector.Protect(json));
        File.Move(temporaryPath, filePath, true);
    }

    private static StoredDeviceIdentity Read(string path, ILocalStateProtector protector)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new StoredDeviceIdentity(null, "", "", null);
            }

            var json = protector.Unprotect(File.ReadAllText(path));
            return JsonSerializer.Deserialize(json, InventoryJsonSerializerContext.Default.StoredDeviceIdentity)
                ?? new StoredDeviceIdentity(null, "", "", null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or CryptographicException or FormatException)
        {
            return new StoredDeviceIdentity(null, "", "", null);
        }
    }
}
