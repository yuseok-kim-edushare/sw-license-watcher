using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace SwLicenseWatcher.Agent.Worker;

public interface IToastHelperProcessStarter
{
    int? StartAndWait(string exePath, string payloadPath, TimeSpan timeout);
}

public sealed class UserSessionToastProcessStarter : IToastHelperProcessStarter
{
    public int? StartAndWait(string exePath, string payloadPath, TimeSpan timeout)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        if (!WindowsServiceHelpers.IsWindowsService() && !WindowsIdentity.GetCurrent().IsSystem)
        {
            return StartAsCurrentUser(exePath, payloadPath, timeout);
        }

        return StartInActiveSessions(exePath, payloadPath, timeout);
    }

    private static int? StartAsCurrentUser(string exePath, string payloadPath, TimeSpan timeout)
    {
        var start = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.SystemDirectory
        };
        start.ArgumentList.Add(payloadPath);
        using var process = Process.Start(start);
        if (process is null)
        {
            return null;
        }

        if (!process.WaitForExit(timeout))
        {
            try
            {
                process.Kill(true);
            }
            catch (InvalidOperationException)
            {
            }

            return null;
        }

        return process.ExitCode;
    }

    private static int? StartInActiveSessions(string exePath, string payloadPath, TimeSpan timeout)
    {
        if (!NativeMethods.WTSEnumerateSessions(0, 0, 1, out var sessionList, out var count) || sessionList == 0)
        {
            return null;
        }

        var launched = false;
        var lastExit = (int?)null;
        try
        {
            var size = Marshal.SizeOf<NativeMethods.WtsSessionInfo>();
            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<NativeMethods.WtsSessionInfo>(sessionList + (i * size));
                if (info.State != NativeMethods.WtsActive || info.SessionId == 0)
                {
                    continue;
                }

                var exit = StartAsUser(info.SessionId, exePath, payloadPath, timeout);
                if (exit is not null)
                {
                    launched = true;
                    lastExit = exit;
                }
            }
        }
        finally
        {
            NativeMethods.WTSFreeMemory(sessionList);
        }

        return launched ? lastExit : null;
    }

    private static int? StartAsUser(uint sessionId, string exePath, string payloadPath, TimeSpan timeout)
    {
        var userToken = nint.Zero;
        var primaryToken = nint.Zero;
        var environment = nint.Zero;
        var processInfo = new NativeMethods.ProcessInformation();
        try
        {
            if (!NativeMethods.WTSQueryUserToken(sessionId, out userToken))
            {
                return null;
            }

            if (!NativeMethods.DuplicateTokenEx(
                    userToken,
                    NativeMethods.GenericAll,
                    0,
                    NativeMethods.SecurityImpersonation,
                    NativeMethods.TokenPrimary,
                    out primaryToken))
            {
                return null;
            }

            NativeMethods.CreateEnvironmentBlock(out environment, primaryToken, false);
            var startup = new NativeMethods.StartupInfo
            {
                cb = Marshal.SizeOf<NativeMethods.StartupInfo>(),
                lpDesktop = @"winsta0\default"
            };
            var commandLine = new string($"\"{exePath}\" \"{payloadPath}\"".ToCharArray());
            var created = NativeMethods.CreateProcessAsUser(
                primaryToken,
                exePath,
                commandLine,
                0,
                0,
                false,
                NativeMethods.CreateUnicodeEnvironment | NativeMethods.CreateNoWindow,
                environment,
                Path.GetDirectoryName(exePath) ?? Environment.SystemDirectory,
                ref startup,
                out processInfo);
            if (!created)
            {
                return null;
            }

            var waited = NativeMethods.WaitForSingleObject(
                processInfo.hProcess,
                (uint)Math.Clamp(timeout.TotalMilliseconds, 1, uint.MaxValue - 1));
            if (waited != NativeMethods.WaitObject0)
            {
                return null;
            }

            return NativeMethods.GetExitCodeProcess(processInfo.hProcess, out var exitCode)
                ? (int)exitCode
                : null;
        }
        finally
        {
            if (processInfo.hThread != 0)
            {
                NativeMethods.CloseHandle(processInfo.hThread);
            }

            if (processInfo.hProcess != 0)
            {
                NativeMethods.CloseHandle(processInfo.hProcess);
            }

            if (environment != 0)
            {
                NativeMethods.DestroyEnvironmentBlock(environment);
            }

            if (primaryToken != 0)
            {
                NativeMethods.CloseHandle(primaryToken);
            }

            if (userToken != 0)
            {
                NativeMethods.CloseHandle(userToken);
            }
        }
    }
}
