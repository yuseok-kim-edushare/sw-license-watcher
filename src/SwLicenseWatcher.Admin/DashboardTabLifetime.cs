namespace SwLicenseWatcher.Admin;

internal sealed class DashboardTabLifetime
{
    private readonly HashSet<string> _visited = ["devices"];

    internal string Active { get; private set; } = "devices";

    internal bool IsActive(string tab) =>
        string.Equals(Active, tab, StringComparison.Ordinal);

    internal bool IsVisited(string tab) => _visited.Contains(tab);

    internal bool Select(string tab)
    {
        if (IsActive(tab))
        {
            return false;
        }

        Active = tab;
        _visited.Add(tab);
        return true;
    }

    internal void Reset()
    {
        _visited.Clear();
        _visited.Add(Active);
    }
}
