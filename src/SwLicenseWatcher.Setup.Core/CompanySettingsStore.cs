using System.Text.Json;

namespace SwLicenseWatcher.Setup.Core;

public static class CompanySettingsStore
{
    public static CompanySettings Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("company.json was not found.", path);
        }

        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize(json, SetupJsonContext.Default.CompanySettings);
        if (!CompanySettingsValidator.TryValidate(settings, out var error))
        {
            throw new InvalidDataException(error);
        }

        return CompanySettingsValidator.Normalize(settings!);
    }

    public static string Serialize(CompanySettings settings)
    {
        if (!CompanySettingsValidator.TryValidate(settings, out var error))
        {
            throw new ArgumentException(error);
        }

        return JsonSerializer.Serialize(CompanySettingsValidator.Normalize(settings), SetupJsonContext.Default.CompanySettings);
    }

    public static void Save(string path, CompanySettings settings)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, Serialize(settings) + Environment.NewLine);
    }
}
