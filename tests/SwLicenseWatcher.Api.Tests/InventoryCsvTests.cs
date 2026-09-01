namespace SwLicenseWatcher.Api.Tests;

public class InventoryCsvTests
{
    [Fact]
    public void Escape_quotes_commas_and_newlines()
    {
        Assert.Equal("plain", InventoryCsv.Escape("plain"));
        Assert.Equal("\"a,b\"", InventoryCsv.Escape("a,b"));
        Assert.Equal("\"a\"\"b\"", InventoryCsv.Escape("a\"b"));
        Assert.Equal("\"a\r\nb\"", InventoryCsv.Escape("a\r\nb"));
    }

    [Fact]
    public void Escape_prefixes_formula_injection_characters()
    {
        Assert.Equal("'=1+1", InventoryCsv.Escape("=1+1"));
        Assert.Equal("'+cmd", InventoryCsv.Escape("+cmd"));
        Assert.Equal("'-1", InventoryCsv.Escape("-1"));
        Assert.Equal("'@sum", InventoryCsv.Escape("@sum"));
    }

    [Fact]
    public void SafeFileName_replaces_invalid_characters()
    {
        Assert.Equal("Chrome_64_", InventoryCsv.SafeFileName("Chrome/64*"));
        Assert.Equal("export", InventoryCsv.SafeFileName("   "));
    }
}
