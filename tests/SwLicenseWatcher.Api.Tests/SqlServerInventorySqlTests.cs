using SwLicenseWatcher.Api;

namespace SwLicenseWatcher.Api.Tests;

public class SqlServerInventorySqlTests
{
    [Fact]
    public void Name_quotes_and_joins_identifiers()
    {
        Assert.Equal("[inventory].[pc_entity]", SqlServerInventoryRepository.Name("inventory", "pc_entity"));
    }

    [Fact]
    public void Name_escapes_closing_brackets_inside_identifiers()
    {
        Assert.Equal("[a]]b]", SqlServerInventoryRepository.Name("a]b"));
        Assert.Equal("[schema]]].[ta]]ble]", SqlServerInventoryRepository.Name("schema]", "ta]ble"));
    }

    [Fact]
    public void Truncate_shortens_values_that_exceed_the_limit()
    {
        Assert.Equal("hel", SqlServerInventoryRepository.Truncate("hello", 3));
    }

    [Fact]
    public void Truncate_preserves_null_and_short_values()
    {
        Assert.Null(SqlServerInventoryRepository.Truncate(null, 3));
        Assert.Equal("hi", SqlServerInventoryRepository.Truncate("hi", 3));
        Assert.Equal("hey", SqlServerInventoryRepository.Truncate("hey", 3));
    }

    [Fact]
    public void ToContainsPattern_returns_null_for_blank_search()
    {
        Assert.Null(SqlServerInventoryRepository.ToContainsPattern(null));
        Assert.Null(SqlServerInventoryRepository.ToContainsPattern(" "));
    }

    [Fact]
    public void ToContainsPattern_wraps_and_escapes_like_wildcards()
    {
        Assert.Equal("%widget%", SqlServerInventoryRepository.ToContainsPattern("widget"));
        Assert.Equal("%a[[]b]%", SqlServerInventoryRepository.ToContainsPattern("a[b]"));
        Assert.Equal("%100[%][_]%", SqlServerInventoryRepository.ToContainsPattern("100%_"));
        Assert.Equal("%trimmed%", SqlServerInventoryRepository.ToContainsPattern("  trimmed  "));
    }
}
