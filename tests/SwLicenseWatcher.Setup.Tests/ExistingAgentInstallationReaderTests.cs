using SwLicenseWatcher.Setup.Core;

namespace SwLicenseWatcher.Setup.Tests;

public class ExistingAgentInstallationReaderTests
{
    [Fact]
    public void Missing_install_is_not_present()
    {
        using var directory = new TempDirectory();
        var machine = new EmptyMachine();
        var sut = new ExistingAgentInstallationReader(
            machine,
            Path.Combine(directory.Path, "install"),
            Path.Combine(directory.Path, "state"));

        var existing = sut.Read();

        Assert.False(existing.IsPresent);
        Assert.Null(existing.PreferredDeviceCode);
        Assert.False(existing.HasDeviceIdentity);
    }

    [Fact]
    public void Worker_files_alone_count_as_an_existing_install()
    {
        using var directory = new TempDirectory();
        var installRoot = Path.Combine(directory.Path, "install");
        var stateRoot = Path.Combine(directory.Path, "state");
        Directory.CreateDirectory(SetupPaths.WorkerDirectory(installRoot));
        File.WriteAllText(SetupPaths.WorkerExe(installRoot), "worker");
        File.WriteAllText(
            Path.Combine(SetupPaths.WorkerDirectory(installRoot), "appsettings.json"),
            """{"Agent":{"DeviceCode":"PC-77","DomainName":"FACTORY"}}""");
        Directory.CreateDirectory(Path.Combine(stateRoot, "state"));
        File.WriteAllText(SetupPaths.DeviceIdentityPath(stateRoot), "keys");

        var existing = new ExistingAgentInstallationReader(new EmptyMachine(), installRoot, stateRoot).Read();

        Assert.True(existing.IsPresent);
        Assert.True(existing.WorkerFilesPresent);
        Assert.False(existing.WorkerServicePresent);
        Assert.Equal("PC-77", existing.PreferredDeviceCode);
        Assert.Equal("FACTORY", existing.DomainName);
        Assert.True(existing.HasDeviceIdentity);
        Assert.Null(existing.InstalledVersion);
        Assert.False(existing.RequiresServerKeyAuthorization);
    }

    [Fact]
    public void Reads_installed_worker_version()
    {
        using var directory = new TempDirectory();
        var installRoot = Path.Combine(directory.Path, "install");
        Directory.CreateDirectory(SetupPaths.WorkerDirectory(installRoot));
        File.WriteAllText(SetupPaths.WorkerExe(installRoot), "worker");
        File.WriteAllText(
            Path.Combine(SetupPaths.WorkerDirectory(installRoot), PayloadLayout.VersionFileName),
            "0.1.0\n");

        var existing = new ExistingAgentInstallationReader(
            new EmptyMachine(),
            installRoot,
            Path.Combine(directory.Path, "state")).Read();

        Assert.Equal("0.1.0", existing.InstalledVersion);
        Assert.True(existing.RequiresServerKeyAuthorization);
    }

    private sealed class EmptyMachine : IAgentMachineIntegration
    {
        public bool ServiceExists(string name) => false;

        public void StopService(string name)
        {
        }

        public void InstallOrUpdateService(string name, string displayName, string description, string exePath)
        {
        }

        public void StartService(string name)
        {
        }

        public void DeleteService(string name)
        {
        }

        public void RegisterArp(string version, string setupExe)
        {
        }

        public void DeleteArp()
        {
        }
    }
}
