using System.Runtime.CompilerServices;
using SwLicenseWatcher.Setup.Core;

namespace SwLicenseWatcher.Setup.Tests;

public class WindowsElevationTests
{
    [Fact]
    public void Already_elevated_process_continues_without_relaunch()
    {
        var starter = new RecordingStarter();

        var relaunched = WindowsElevation.TryRelaunchIfNotElevated(
            ["/uninstall"],
            UnrestrictedAdministratorPrivilege.Instance,
            starter,
            @"C:\missing\setup.exe",
            out var exitCode);

        Assert.False(relaunched);
        Assert.Equal(0, exitCode);
        Assert.Empty(starter.Calls);
    }

    [Fact]
    public void Unelevated_process_relaunches_and_returns_child_exit_code()
    {
        using var directory = new TempDirectory();
        var exe = Path.Combine(directory.Path, "SwLicenseWatcher-Setup.exe");
        File.WriteAllText(exe, "stub");
        var starter = new RecordingStarter { ExitCode = 7 };

        var relaunched = WindowsElevation.TryRelaunchIfNotElevated(
            ["/uninstall", "--payload-dir=C:\\payload"],
            new DeniedPrivilege(),
            starter,
            exe,
            out var exitCode);

        Assert.True(relaunched);
        Assert.Equal(7, exitCode);
        Assert.Equal([exe + " /uninstall --payload-dir=C:\\payload"], starter.Calls);
    }

    [Fact]
    public void Unelevated_process_without_an_exe_path_explains_the_failure()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            WindowsElevation.TryRelaunchIfNotElevated(
                ["/uninstall"],
                new DeniedPrivilege(),
                new RecordingStarter(),
                processPath: null,
                out _));

        Assert.Equal(
            "실행 파일 경로를 확인할 수 없어 관리자 권한으로 다시 시작하지 못했습니다.",
            error.Message);
    }

    [Fact]
    public void Setup_and_launcher_require_administrator_in_their_win32_manifests()
    {
        var root = RepoRoot();
        foreach (var relative in new[]
                 {
                     Path.Combine("src", "SwLicenseWatcher.Setup", "app.manifest"),
                     Path.Combine("src", "SwLicenseWatcher.Setup.Launcher", "app.manifest")
                 })
        {
            var manifest = File.ReadAllText(Path.Combine(root, relative));
            Assert.Contains("requireAdministrator", manifest, StringComparison.Ordinal);
            Assert.DoesNotContain("asInvoker", manifest, StringComparison.Ordinal);
        }

        foreach (var relative in new[]
                 {
                     Path.Combine("src", "SwLicenseWatcher.Setup", "SwLicenseWatcher.Setup.csproj"),
                     Path.Combine("src", "SwLicenseWatcher.Setup.Launcher", "SwLicenseWatcher.Setup.Launcher.csproj")
                 })
        {
            var project = File.ReadAllText(Path.Combine(root, relative));
            Assert.Contains("<ApplicationManifest>app.manifest</ApplicationManifest>", project, StringComparison.Ordinal);
        }
    }

    private static string RepoRoot([CallerFilePath] string? file = null)
    {
        var tests = Path.GetDirectoryName(file)
            ?? throw new InvalidOperationException("Test file path is missing.");
        return Path.GetFullPath(Path.Combine(tests, "..", ".."));
    }

    private sealed class DeniedPrivilege : IAdministratorPrivilege
    {
        public bool IsElevated => false;
    }

    private sealed class RecordingStarter : IElevatedProcessStarter
    {
        public int ExitCode { get; init; }

        public List<string> Calls { get; } = [];

        public int StartAndWait(string exePath, IReadOnlyList<string> arguments)
        {
            Calls.Add(exePath + " " + string.Join(' ', arguments));
            return ExitCode;
        }
    }
}
