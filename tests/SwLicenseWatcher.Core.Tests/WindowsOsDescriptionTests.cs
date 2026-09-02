using System.Runtime.InteropServices;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Core.Tests;

public class WindowsOsDescriptionTests
{
    private static readonly Version WindowsNt = new(10, 0);

    [Fact]
    public void Compose_rewrites_windows_10_product_name_when_build_is_22000_or_higher()
    {
        var description = WindowsOsDescription.Compose(
            "Windows 10 Pro",
            "24H2",
            "2009",
            "26100",
            4351,
            WindowsNt,
            Architecture.X64,
            "fallback");

        Assert.Equal("Windows 11 Pro 24H2 (10.0.26100.4351) x64", description);
    }

    [Fact]
    public void Compose_keeps_windows_10_product_name_when_build_is_below_22000()
    {
        var description = WindowsOsDescription.Compose(
            "Windows 10 Pro",
            "22H2",
            "2009",
            "19045",
            4780,
            WindowsNt,
            Architecture.X64,
            "fallback");

        Assert.Equal("Windows 10 Pro 22H2 (10.0.19045.4780) x64", description);
    }

    [Fact]
    public void Compose_uses_release_id_when_display_version_is_missing()
    {
        var description = WindowsOsDescription.Compose(
            "Windows 10 Pro",
            "  ",
            "2009",
            "19041",
            264,
            WindowsNt,
            Architecture.X64,
            "fallback");

        Assert.Equal("Windows 10 Pro 2009 (10.0.19041.264) x64", description);
    }

    [Fact]
    public void Compose_returns_the_fallback_when_all_registry_values_are_missing()
    {
        var description = WindowsOsDescription.Compose(
            null,
            null,
            " ",
            null,
            null,
            null,
            Architecture.X64,
            "Microsoft Windows NT 10.0.26100.0");

        Assert.Equal("Microsoft Windows NT 10.0.26100.0", description);
    }

    [Fact]
    public void Compose_stays_within_the_snapshot_validator_operating_system_limit()
    {
        var longProductName = new string('W', 200);
        var description = WindowsOsDescription.Compose(
            longProductName,
            "24H2",
            "2009",
            "26100",
            4351,
            WindowsNt,
            Architecture.X64,
            new string('f', 200));

        Assert.True(description.Length <= WindowsOsDescription.MaxLength);
        Assert.True(description.Length <= 128);

        var almostAtLimit = WindowsOsDescription.Compose(
            new string('A', 125),
            null,
            null,
            null,
            null,
            null,
            Architecture.X64,
            "fallback");
        Assert.Equal(new string('A', 125), almostAtLimit);
        Assert.True(almostAtLimit.Length <= 128);
    }
}
