using SwLicenseWatcher.Admin.Services;

namespace SwLicenseWatcher.Admin.Tests;

public class AdminRequestUriTests
{
    [Fact]
    public void Search_keeps_encoded_characters_that_iis_rejects_raw()
    {
        var uri = AdminApiClient.CreateRequestUri(
            new Uri("https://inventory.example/admin/"),
            "/api/policies?search=%2ATorrent%2A&skip=0");

        Assert.Equal("https://inventory.example/api/policies?search=%2ATorrent%2A&skip=0", uri.OriginalString);
        Assert.DoesNotContain("*", uri.OriginalString, StringComparison.Ordinal);
        Assert.Contains("%2A", uri.OriginalString, StringComparison.Ordinal);
        var root = new Uri("https://inventory.example/");
        Assert.Contains("%2B", AdminApiClient.CreateRequestUri(root, "/api/inventory/software?search=%2B").OriginalString, StringComparison.Ordinal);
        Assert.Contains("%23", AdminApiClient.CreateRequestUri(root, "/api/policies?search=%23").OriginalString, StringComparison.Ordinal);
        Assert.Contains("%25", AdminApiClient.CreateRequestUri(root, "/api/policies?search=%25").OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public void Software_name_stays_in_the_query()
    {
        const string name = "C++/A\\B%C#";
        var path = "/api/inventory/software/devices?name=" + Uri.EscapeDataString(name);
        var uri = AdminApiClient.CreateRequestUri(new Uri("https://inventory.example/"), path);

        Assert.StartsWith("https://inventory.example/api/inventory/software/devices?", uri.OriginalString, StringComparison.Ordinal);
        Assert.Contains("name=" + Uri.EscapeDataString(name), uri.OriginalString, StringComparison.Ordinal);
        Assert.DoesNotContain("/C++", uri.OriginalString, StringComparison.Ordinal);
        Assert.DoesNotContain("%2F", uri.AbsolutePath, StringComparison.Ordinal);
        Assert.DoesNotContain("%5C", uri.AbsolutePath, StringComparison.Ordinal);
    }
}
