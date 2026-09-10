using System.Text.Json;
using SwLicenseWatcher.Api;
using SwLicenseWatcher.Application;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api.Tests;

public class PhaseZeroCharacterizationTests
{
    [Fact]
    public void ApplyLicenseSources_uses_device_override_before_managed_policy_default()
    {
        var installed = new[]
        {
            Software("Contoso Editor"),
            Software("Contoso Viewer")
        };
        var assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CONTOSO EDITOR"] = LicenseSourceNames.Byo
        };
        var policies = new[]
        {
            Policy("Contoso *", SoftwarePolicyClassification.Managed, LicenseSourceNames.Company)
        };

        var result = InventoryDecisions.ApplyLicenseSources(installed, assignments, policies);

        Assert.Collection(
            result,
            editor =>
            {
                Assert.Null(editor.Classification);
                Assert.Equal("byo", editor.LicenseSource);
                Assert.Equal("byo", editor.LicenseSourceOverride);
            },
            viewer =>
            {
                Assert.Null(viewer.Classification);
                Assert.Equal("company", viewer.LicenseSource);
                Assert.Null(viewer.LicenseSourceOverride);
            });
    }

    [Fact]
    public void ApplyLicenseSources_preserves_stored_classification_and_does_not_apply_nonmanaged_defaults()
    {
        var installed = new[]
        {
            Software("Blocked Tool") with { Classification = "white" },
            Software("Unknown Tool") with { Classification = "unclassified" }
        };
        var policies = new[]
        {
            Policy("Blocked Tool", SoftwarePolicyClassification.Blacklist, LicenseSourceNames.Company)
        };

        var result = InventoryDecisions.ApplyLicenseSources(
            installed,
            new Dictionary<string, string>(),
            policies);

        Assert.Equal("white", result[0].Classification);
        Assert.Null(result[0].LicenseSource);
        Assert.Equal("unclassified", result[1].Classification);
        Assert.Null(result[1].LicenseSource);
    }

    [Fact]
    public void ApplySoftwareDeviceLicenseSources_normalizes_override_and_resolves_default()
    {
        var items = new List<SoftwareDevice>
        {
            Device("PC-01", "managed", " BYO "),
            Device("PC-02", "managed", null),
            Device("PC-03", "black", "invalid")
        };

        var result = InventoryDecisions.ApplySoftwareDeviceLicenseSources(
            "Contoso Editor",
            items,
            [Policy("Contoso Editor", SoftwarePolicyClassification.Managed, LicenseSourceNames.Company)]);

        Assert.Equal(("byo", "byo"), (result[0].LicenseSource, result[0].LicenseSourceOverride));
        Assert.Equal(("company", null), (result[1].LicenseSource, result[1].LicenseSourceOverride));
        Assert.Equal((null, null), (result[2].LicenseSource, result[2].LicenseSourceOverride));
        Assert.All(items, item => Assert.Null(item.LicenseSource));
    }

    [Fact]
    public void InventoryMemoryStore_counts_each_concurrent_recording()
    {
        var store = new InventoryMemoryStore();
        var snapshot = new InventoryIngestionRequest(
            new PcIdentity("PC-01", "host", "domain", "Windows", "1.0"),
            [],
            DateTimeOffset.UnixEpoch);
        var heartbeat = new AgentHeartbeat("PC-01", "host", "Worker", "1.0", DateTimeOffset.UnixEpoch, "ok");

        Parallel.For(0, 1_000, _ =>
        {
            store.RecordSnapshot(snapshot);
            store.RecordHeartbeat(heartbeat);
        });

        Assert.Equal(1_000, store.SnapshotCount);
        Assert.Equal(1_000, store.HeartbeatCount);
    }

    [Fact]
    public void HealthResponse_json_uses_pascal_case_and_omits_null_reason()
    {
        var value = new HealthResponse("Healthy", DateTimeOffset.UnixEpoch);

        var json = JsonSerializer.Serialize(value, ApiJsonSerializerContext.Default.HealthResponse);

        Assert.Equal("""{"Status":"Healthy","Utc":"1970-01-01T00:00:00+00:00"}""", json);
    }

    [Fact]
    public void DeviceDetail_json_preserves_nested_contract_and_omits_optional_inventory_fields()
    {
        var value = new DeviceDetail(
            "PC-01",
            "host",
            "domain",
            "Windows",
            "1.0",
            null,
            DateTimeOffset.UnixEpoch,
            [Software("Contoso Editor")]);

        var json = JsonSerializer.Serialize(value, ApiJsonSerializerContext.Default.DeviceDetail);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var software = root.GetProperty("InstalledSoftware")[0];

        Assert.Equal("PC-01", root.GetProperty("DeviceCode").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("LastHeartbeatUtc").ValueKind);
        Assert.Equal("Contoso Editor", software.GetProperty("Name").GetString());
        Assert.False(software.TryGetProperty("Classification", out _));
        Assert.False(software.TryGetProperty("LicenseSource", out _));
        Assert.False(software.TryGetProperty("LicenseSourceOverride", out _));
        Assert.False(root.TryGetProperty("deviceCode", out _));
    }

    [Fact]
    public void SoftwarePolicyEntry_json_writes_classification_as_wire_name()
    {
        var value = Policy("Contoso Editor", SoftwarePolicyClassification.Managed, LicenseSourceNames.Company);

        var json = JsonSerializer.Serialize(value, ApiJsonSerializerContext.Default.SoftwarePolicyEntry);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("managed", document.RootElement.GetProperty("Classification").GetString());
        Assert.Equal("company", document.RootElement.GetProperty("DefaultLicenseSource").GetString());
    }

    private static InstalledSoftwareEntry Software(string name) =>
        new(name, "1.0", "Contoso", null, "machine", "registry");

    private static SoftwareDevice Device(string code, string classification, string? sourceOverride) =>
        new(code, code, "domain", "Windows", "1.0", null, null, "1.0", "Contoso", classification, null, sourceOverride);

    private static SoftwarePolicyEntry Policy(
        string productName,
        SoftwarePolicyClassification classification,
        string? defaultLicenseSource) =>
        new(1, productName, null, null, classification, null, true, DateTimeOffset.UnixEpoch, defaultLicenseSource);
}
