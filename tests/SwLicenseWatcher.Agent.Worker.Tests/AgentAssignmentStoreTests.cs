using SwLicenseWatcher.Agent.Worker;

namespace SwLicenseWatcher.Agent.Worker.Tests;

public class AgentAssignmentStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "slw-assign-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("""{"AssignedHostName":"마케팅-01"}""", true, "마케팅-01")]
    [InlineData("""{"assignedHostName":"pc-01"}""", true, "pc-01")]
    [InlineData("""{"AssignedHostName":null,"DeviceCode":"PC-1"}""", true, null)]
    [InlineData("""{"DeviceCode":"PC-1"}""", false, null)]
    [InlineData("", false, null)]
    public void TryReadAssignedHostName_reads_pascal_or_camel_case(
        string json,
        bool specified,
        string? expected)
    {
        var read = AgentAssignmentStore.TryReadAssignedHostName(json, out var assignmentSpecified, out var assigned);
        Assert.Equal(specified, read);
        Assert.Equal(specified, assignmentSpecified);
        Assert.Equal(expected, assigned);
    }

    [Fact]
    public async Task ApplyAsync_persists_and_resolves_host_name()
    {
        var path = Path.Combine(_directory, "assigned-host-name.json");
        var store = new AgentAssignmentStore(path);

        Assert.Equal("DESKTOP-1", store.ResolveHostName("DESKTOP-1"));
        await store.ApplyAsync(true, " 마케팅-01 ", CancellationToken.None);
        Assert.Equal("마케팅-01", store.ResolveHostName("DESKTOP-1"));
        Assert.True(File.Exists(path));

        var reloaded = new AgentAssignmentStore(path);
        Assert.Equal("마케팅-01", reloaded.ResolveHostName("DESKTOP-1"));

        await store.ApplyAsync(true, null, CancellationToken.None);
        Assert.Equal("DESKTOP-1", store.ResolveHostName("DESKTOP-1"));
    }

    [Fact]
    public async Task ApplyAsync_ignores_unspecified_assignment()
    {
        var path = Path.Combine(_directory, "assigned-host-name.json");
        var store = new AgentAssignmentStore(path);
        await store.ApplyAsync(true, "pc-01", CancellationToken.None);

        await store.ApplyAsync(false, null, CancellationToken.None);
        Assert.Equal("pc-01", store.AssignedHostName);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}
