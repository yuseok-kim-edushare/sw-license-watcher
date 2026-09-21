using System.Text.Json;
using Microsoft.Toolkit.Uwp.Notifications;
using SwLicenseWatcher.Core;
using SwLicenseWatcher.Crypto;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            return 2;
        }

        try
        {
            if (!UserToastAccess.TryLoadPublicKey(UserToastProofs.DefaultPublicKeyPath, out var publicKey))
            {
                return 4;
            }

            var json = File.ReadAllText(args[0]);
            var payload = JsonSerializer.Deserialize(json, InventoryJsonSerializerContext.Default.SignedUserToastPayload);
            if (!UserToastAccess.TryAccept(payload, publicKey, DateTimeOffset.UtcNow, out var command) || command is null)
            {
                return 3;
            }

            new ToastContentBuilder()
                .AddText(command.Title)
                .AddText(command.Body)
                .Show();
            Thread.Sleep(500);
            return 0;
        }
        catch
        {
            return 1;
        }
    }
}
