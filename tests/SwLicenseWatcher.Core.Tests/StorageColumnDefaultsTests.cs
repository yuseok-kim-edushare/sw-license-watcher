using System.Reflection;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Core.Tests;

public class StorageColumnDefaultsTests
{
    [Fact]
    public void Blank_column_names_on_every_table_fall_back_to_the_property_defaults()
    {
        var options = new SqlServerStorageOptions();
        Blank(options.PcTable);
        Blank(options.InstalledSoftwareTable);
        Blank(options.SoftwarePolicyTable);
        Blank(options.SoftwareViolationTable);
        Blank(options.StaleHeartbeatNotificationTable);
        Blank(options.UninstallRequestTable);
        Blank(options.SoftwareLicenseTable);
        Blank(options.WorkerUpdatePinTable);
        options.UserMessageTable = null!;

        options.ApplyUnspecifiedColumnDefaults();

        AssertSame(options.PcTable, new PcTableOptions());
        AssertSame(options.InstalledSoftwareTable, new InstalledSoftwareTableOptions());
        AssertSame(options.SoftwarePolicyTable, new SoftwarePolicyTableOptions());
        AssertSame(options.SoftwareViolationTable, new SoftwareViolationTableOptions());
        AssertSame(options.StaleHeartbeatNotificationTable, new StaleHeartbeatNotificationTableOptions());
        AssertSame(options.UninstallRequestTable, new UninstallRequestTableOptions());
        AssertSame(options.SoftwareLicenseTable, new SoftwareLicenseTableOptions());
        AssertSame(options.WorkerUpdatePinTable, new WorkerUpdatePinTableOptions());
        AssertSame(options.UserMessageTable, new UserMessageTableOptions());
        Assert.Equal("installed_at_utc", options.InstalledSoftwareTable.InstalledAtUtcColumn);
    }

    [Fact]
    public void Custom_column_names_are_kept_and_trimmed()
    {
        var options = new SqlServerStorageOptions
        {
            InstalledSoftwareTable = new InstalledSoftwareTableOptions
            {
                DisplayNameColumn = " sw_name ",
                InstalledAtUtcColumn = "install_date"
            }
        };

        options.ApplyUnspecifiedColumnDefaults();

        Assert.Equal("sw_name", options.InstalledSoftwareTable.DisplayNameColumn);
        Assert.Equal("install_date", options.InstalledSoftwareTable.InstalledAtUtcColumn);
        Assert.Equal("display_version", options.InstalledSoftwareTable.DisplayVersionColumn);
    }

    private static void Blank(object target)
    {
        foreach (var property in target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.PropertyType == typeof(string) && property.CanWrite)
            {
                property.SetValue(target, null);
            }
        }
    }

    private static void AssertSame(object actual, object expected)
    {
        foreach (var property in expected.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.PropertyType == typeof(string))
            {
                Assert.Equal(property.GetValue(expected), property.GetValue(actual));
            }
        }
    }
}
