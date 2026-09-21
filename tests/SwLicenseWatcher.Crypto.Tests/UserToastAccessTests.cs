using SwLicenseWatcher.Core;
using SwLicenseWatcher.Crypto;

namespace SwLicenseWatcher.Crypto.Tests;

public class UserToastAccessTests
{
    [Fact]
    public void Sign_then_accept_a_fresh_payload()
    {
        var (publicKey, privateKey) = MldsaDeviceCrypto.GenerateKeyPair();
        var now = new DateTimeOffset(2026, 9, 21, 5, 50, 0, TimeSpan.Zero);
        var signed = UserToastAccess.Sign(privateKey, new AgentUserMessageCommand(12, " 점검 ", " 10분 후 "), now);

        Assert.Equal(DeviceIdentityAlgorithms.Mldsa87, signed.Alg);
        Assert.True(UserToastAccess.TryAccept(signed, publicKey, now, out var command));
        Assert.Equal(12, command!.Id);
        Assert.Equal("점검", command.Title);
        Assert.Equal("10분 후", command.Body);
    }

    [Fact]
    public void TryAccept_rejects_legacy_unsigned_command_shape()
    {
        var (publicKey, _) = MldsaDeviceCrypto.GenerateKeyPair();
        var payload = new SignedUserToastPayload(4, "T", "B", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "", "");

        Assert.False(UserToastAccess.TryAccept(payload, publicKey, DateTimeOffset.UtcNow, out var command));
        Assert.Null(command);
    }

    [Fact]
    public void TryAccept_rejects_a_payload_signed_by_another_key()
    {
        var (publicKey, _) = MldsaDeviceCrypto.GenerateKeyPair();
        var (_, otherPrivate) = MldsaDeviceCrypto.GenerateKeyPair();
        var now = DateTimeOffset.UtcNow;
        var signed = UserToastAccess.Sign(otherPrivate, new AgentUserMessageCommand(1, "T", "B"), now);

        Assert.False(UserToastAccess.TryAccept(signed, publicKey, now, out _));
    }

    [Fact]
    public void TryAccept_rejects_expired_and_future_payloads()
    {
        var (publicKey, privateKey) = MldsaDeviceCrypto.GenerateKeyPair();
        var issued = new DateTimeOffset(2026, 9, 21, 5, 50, 0, TimeSpan.Zero);
        var signed = UserToastAccess.Sign(privateKey, new AgentUserMessageCommand(1, "T", "B"), issued);

        Assert.False(UserToastAccess.TryAccept(signed, publicKey, issued + TimeSpan.FromMinutes(3), out _));
        Assert.False(UserToastAccess.TryAccept(signed, publicKey, issued - TimeSpan.FromMinutes(1), out _));
        Assert.True(UserToastAccess.TryAccept(signed, publicKey, issued + TimeSpan.FromSeconds(20), out _));
    }

    [Fact]
    public void TryLoadPublicKey_reads_base64_and_rejects_missing_files()
    {
        var directory = Path.Combine(Path.GetTempPath(), "slw-toast-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var (publicKey, _) = MldsaDeviceCrypto.GenerateKeyPair();
            var path = Path.Combine(directory, UserToastProofs.PublicKeyFileName);
            File.WriteAllText(path, MldsaDeviceCrypto.ToBase64(publicKey));

            Assert.True(UserToastAccess.TryLoadPublicKey(path, out var loaded));
            Assert.Equal(publicKey, loaded);
            Assert.False(UserToastAccess.TryLoadPublicKey(path + ".missing", out _));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
