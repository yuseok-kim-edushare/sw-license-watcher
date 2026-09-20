using System.Text.Json;
using Microsoft.Toolkit.Uwp.Notifications;
using SwLicenseWatcher.Core;

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
            var json = File.ReadAllText(args[0]);
            var payload = JsonSerializer.Deserialize(json, InventoryJsonSerializerContext.Default.AgentUserMessageCommand);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Title) || string.IsNullOrWhiteSpace(payload.Body))
            {
                return 3;
            }

            new ToastContentBuilder()
                .AddText(payload.Title)
                .AddText(payload.Body)
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
