namespace SwLicenseWatcher.Admin.Tests;

public class DashboardTabLifetimeTests
{
    [Fact]
    public void Tabs_become_visited_lazily_and_stay_alive_across_switches()
    {
        var tabs = new DashboardTabLifetime();

        Assert.True(tabs.IsVisited("devices"));
        Assert.False(tabs.IsVisited("software"));

        Assert.True(tabs.Select("software"));
        Assert.True(tabs.IsVisited("devices"));
        Assert.True(tabs.IsVisited("software"));

        Assert.True(tabs.Select("devices"));
        Assert.True(tabs.IsVisited("software"));
        Assert.False(tabs.Select("devices"));
    }

    [Fact]
    public void Reset_retains_only_active_tab_for_next_authenticated_session()
    {
        var tabs = new DashboardTabLifetime();
        tabs.Select("software");
        tabs.Select("policies");

        tabs.Reset();

        Assert.True(tabs.IsVisited("policies"));
        Assert.False(tabs.IsVisited("devices"));
        Assert.False(tabs.IsVisited("software"));
    }
}
