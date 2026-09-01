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
        Assert.Equal("'\tsheet", InventoryCsv.Escape("\tsheet"));
    }

    [Fact]
    public void File_writes_utf8_bom_and_crlf_rows()
    {
        var result = InventoryCsv.File("policies.csv", ["Id", "Name"], [["1", "=cmd"]]);
        var file = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult>(result);
        Assert.Equal("text/csv", file.ContentType);
        Assert.Equal("policies.csv", file.FileDownloadName);
        Assert.Equal(0xEF, file.FileContents.Span[0]);
        Assert.Equal(0xBB, file.FileContents.Span[1]);
        Assert.Equal(0xBF, file.FileContents.Span[2]);
        var text = System.Text.Encoding.UTF8.GetString(file.FileContents.Span[3..]);
        Assert.Equal("Id,Name\r\n1,'=cmd\r\n", text);
    }

    [Fact]
    public void SafeFileName_replaces_invalid_characters()
    {
        Assert.Equal("Chrome_64_", InventoryCsv.SafeFileName("Chrome/64*"));
        Assert.Equal("export", InventoryCsv.SafeFileName("   "));
    }
}
