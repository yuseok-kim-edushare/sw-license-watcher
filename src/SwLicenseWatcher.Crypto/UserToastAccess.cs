using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Crypto;

public static class UserToastAccess
{
    public static SignedUserToastPayload Sign(
        byte[] privateKey,
        AgentUserMessageCommand message,
        DateTimeOffset now)
    {
        if (message.Id <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(message), "The toast id must be positive.");
        }

        if (!UserMessageText.TryNormalize(message.Title, message.Body, out var title, out var body, out var error))
        {
            throw new ArgumentException(error, nameof(message));
        }

        var issuedAtUnix = now.ToUnixTimeSeconds();
        var signature = MldsaDeviceCrypto.Sign(
            privateKey,
            UserToastProofs.CanonicalBytes(message.Id, title, body, issuedAtUnix));
        return new SignedUserToastPayload(
            message.Id,
            title,
            body,
            issuedAtUnix,
            DeviceIdentityAlgorithms.Mldsa87,
            MldsaDeviceCrypto.ToBase64(signature));
    }

    public static bool TryAccept(
        SignedUserToastPayload? payload,
        byte[] publicKey,
        DateTimeOffset now,
        out AgentUserMessageCommand? command)
    {
        command = null;
        if (payload is null ||
            payload.Id <= 0 ||
            !string.Equals(payload.Alg, DeviceIdentityAlgorithms.Mldsa87, StringComparison.Ordinal) ||
            !UserMessageText.TryNormalize(payload.Title, payload.Body, out var title, out var body, out _) ||
            !MldsaDeviceCrypto.TryFromBase64(payload.Signature, out var signature))
        {
            return false;
        }

        DateTimeOffset issuedAt;
        try
        {
            issuedAt = DateTimeOffset.FromUnixTimeSeconds(payload.IssuedAtUnix);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        if (issuedAt > now + UserToastProofs.ClockSkew ||
            now - issuedAt > UserToastProofs.MaxAge)
        {
            return false;
        }

        if (!MldsaDeviceCrypto.Verify(
                publicKey,
                UserToastProofs.CanonicalBytes(payload.Id, title, body, payload.IssuedAtUnix),
                signature))
        {
            return false;
        }

        command = new AgentUserMessageCommand(payload.Id, title, body);
        return true;
    }

    public static bool TryLoadPublicKey(string path, out byte[] publicKey)
    {
        publicKey = [];
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            return MldsaDeviceCrypto.TryFromBase64(File.ReadAllText(path), out publicKey);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
