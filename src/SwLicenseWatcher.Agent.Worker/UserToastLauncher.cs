using System.Text.Json;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Agent.Worker;

public sealed class UserToastLauncher(
    ILogger<UserToastLauncher> logger,
    IToastHelperProcessStarter processStarter,
    IOptions<LocalStateStoreOptions> localState)
{
    public const string HelperFileName = "SwLicenseWatcher.Agent.Toast.exe";

    public Task<bool> ShowAsync(AgentUserMessageCommand message, CancellationToken cancellationToken) =>
        ShowAsync(message, AppContext.BaseDirectory, cancellationToken);

    public async Task<bool> ShowAsync(
        AgentUserMessageCommand message,
        string helperDirectory,
        CancellationToken cancellationToken)
    {
        var helper = ResolveHelperPath(helperDirectory);
        if (helper is null)
        {
            logger.LogWarning("Toast helper {Helper} was not found beside the Worker.", HelperFileName);
            return false;
        }

        var directory = Path.Combine(
            Path.GetDirectoryName(localState.Value.QueueDirectory) ?? @"C:\ProgramData\SwLicenseWatcher\state",
            "toasts");
        Directory.CreateDirectory(directory);
        var payloadPath = Path.Combine(directory, $"{message.Id}.json");
        try
        {
            var json = JsonSerializer.Serialize(message, InventoryJsonSerializerContext.Default.AgentUserMessageCommand);
            await File.WriteAllTextAsync(payloadPath, json, cancellationToken);
            var exit = processStarter.StartAndWait(helper, payloadPath, TimeSpan.FromSeconds(15));
            if (exit == 0)
            {
                return true;
            }

            logger.LogWarning(
                "Toast helper exited {ExitCode} for user message {Id}.",
                exit,
                message.Id);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to write toast payload for user message {Id}.", message.Id);
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(payloadPath))
                {
                    File.Delete(payloadPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Unable to delete toast payload {PayloadPath}.", payloadPath);
            }
        }
    }

    public static string? ResolveHelperPath(string? baseDirectory = null)
    {
        var root = baseDirectory ?? AppContext.BaseDirectory;
        var nested = Path.Combine(root, "toast", HelperFileName);
        if (File.Exists(nested))
        {
            return nested;
        }

        var sibling = Path.Combine(root, HelperFileName);
        return File.Exists(sibling) ? sibling : null;
    }
}
