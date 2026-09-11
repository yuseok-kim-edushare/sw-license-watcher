using System.Security.Cryptography;
using System.Text.Json;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Setup.Core;

public sealed record StoredLocalDeviceIdentity(
    string? DeviceId,
    string PublicKey,
    string PrivateKey,
    string? Certificate);

public sealed class InstalledDeviceIdentityStore
{
    private readonly string _filePath;
    private readonly ILocalStateProtector _protector;

    public InstalledDeviceIdentityStore(string? stateRoot = null, ILocalStateProtector? protector = null)
    {
        _filePath = SetupPaths.DeviceIdentityPath(stateRoot ?? SetupPaths.DefaultStateRoot);
        _protector = protector ?? new DpapiLocalStateProtector(new LocalStateStoreOptions());
    }

    public StoredLocalDeviceIdentity Read()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new StoredLocalDeviceIdentity(null, "", "", null);
            }

            var json = _protector.Unprotect(File.ReadAllText(_filePath));
            var stored = JsonSerializer.Deserialize(json, InventoryJsonSerializerContext.Default.StoredDeviceIdentity);
            if (stored is null)
            {
                return new StoredLocalDeviceIdentity(null, "", "", null);
            }

            return new StoredLocalDeviceIdentity(
                stored.DeviceId,
                stored.PublicKey,
                stored.PrivateKey,
                stored.Certificate);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or CryptographicException or FormatException)
        {
            return new StoredLocalDeviceIdentity(null, "", "", null);
        }
    }

    public void ApplyServerKeys(UpgradeAuthorizationResponse keys)
    {
        if (string.IsNullOrWhiteSpace(keys.DeviceId) && string.IsNullOrWhiteSpace(keys.DeviceCertificate))
        {
            return;
        }

        var current = Read();
        if (string.IsNullOrWhiteSpace(current.PrivateKey) || string.IsNullOrWhiteSpace(current.PublicKey))
        {
            return;
        }

        if (string.Equals(current.DeviceId, keys.DeviceId, StringComparison.Ordinal) &&
            string.Equals(current.Certificate, keys.DeviceCertificate, StringComparison.Ordinal))
        {
            return;
        }

        var updated = new StoredDeviceIdentity(
            string.IsNullOrWhiteSpace(keys.DeviceId) ? current.DeviceId : keys.DeviceId,
            current.PublicKey,
            current.PrivateKey,
            string.IsNullOrWhiteSpace(keys.DeviceCertificate) ? current.Certificate : keys.DeviceCertificate);
        var json = JsonSerializer.Serialize(updated, InventoryJsonSerializerContext.Default.StoredDeviceIdentity);
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = _filePath + ".tmp";
        File.WriteAllText(temporaryPath, _protector.Protect(json));
        File.Move(temporaryPath, _filePath, true);
    }
}
