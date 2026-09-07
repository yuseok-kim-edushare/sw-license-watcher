namespace SwLicenseWatcher.Setup.Core;

public sealed class SetupArguments
{
    public bool Uninstall { get; init; }

    public string? SourceExePath { get; init; }

    public string? PayloadDirectory { get; init; }

    public static SetupArguments Parse(IReadOnlyList<string> args)
    {
        var uninstall = false;
        string? sourceExe = null;
        string? payloadDirectory = null;

        foreach (var arg in args)
        {
            if (string.Equals(arg, "/uninstall", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--uninstall", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-uninstall", StringComparison.OrdinalIgnoreCase))
            {
                uninstall = true;
                continue;
            }

            if (TryReadValue(arg, "--source-exe=", out var source))
            {
                sourceExe = source;
                continue;
            }

            if (TryReadValue(arg, "--payload-dir=", out var payload))
            {
                payloadDirectory = payload;
            }
        }

        return new SetupArguments
        {
            Uninstall = uninstall,
            SourceExePath = sourceExe,
            PayloadDirectory = payloadDirectory
        };
    }

    private static bool TryReadValue(string arg, string prefix, out string value)
    {
        if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = arg[prefix.Length..].Trim().Trim('"');
            return value.Length > 0;
        }

        value = string.Empty;
        return false;
    }
}
