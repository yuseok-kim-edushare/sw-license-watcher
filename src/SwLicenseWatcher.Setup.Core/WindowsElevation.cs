using System.ComponentModel;
using System.Diagnostics;

namespace SwLicenseWatcher.Setup.Core;

public interface IElevatedProcessStarter
{
    int StartAndWait(string exePath, IReadOnlyList<string> arguments);
}

public sealed class WindowsElevatedProcessStarter : IElevatedProcessStarter
{
    public static WindowsElevatedProcessStarter Instance { get; } = new();

    public int StartAndWait(string exePath, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("관리자 권한으로 다시 시작하지 못했습니다.");
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new UnauthorizedAccessException(
                "관리자 권한 요청이 취소되었습니다. Program Files와 Windows 서비스를 다루려면 허용이 필요합니다.",
                ex);
        }
    }
}

public static class WindowsElevation
{
    public static bool TryRelaunchIfNotElevated(
        IReadOnlyList<string> arguments,
        IAdministratorPrivilege privilege,
        IElevatedProcessStarter starter,
        string? processPath,
        out int exitCode)
    {
        if (privilege.IsElevated)
        {
            exitCode = 0;
            return false;
        }

        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
        {
            throw new InvalidOperationException(
                "실행 파일 경로를 확인할 수 없어 관리자 권한으로 다시 시작하지 못했습니다.");
        }

        exitCode = starter.StartAndWait(processPath, arguments);
        return true;
    }
}
