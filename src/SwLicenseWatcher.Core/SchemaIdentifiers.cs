using System.Security.Cryptography;
using System.Text;

namespace SwLicenseWatcher.Core;

internal static class SchemaIdentifiers
{
    public static string Build(string prefix, int maxLength, params string[] parts)
    {
        var identifier = string.Join('_', [prefix, .. parts]);
        if (identifier.Length <= maxLength)
        {
            return identifier;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identifier)))[..16];
        var keep = Math.Max(1, maxLength - hash.Length - 1);
        return $"{identifier[..keep]}_{hash}";
    }
}

public static class SchemaScriptBuilders
{
    public static ISchemaScriptBuilder Create(StorageProvider provider) => provider switch
    {
        StorageProvider.MySql => new MySqlSchemaScriptBuilder(),
        StorageProvider.PostgreSql => new PostgreSqlSchemaScriptBuilder(),
        _ => new SqlServerSchemaScriptBuilder()
    };
}
