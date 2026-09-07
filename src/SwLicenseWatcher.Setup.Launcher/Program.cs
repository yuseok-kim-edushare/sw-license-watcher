using System.Diagnostics;
using System.Runtime.InteropServices;
using SwLicenseWatcher.Setup.Core;

var sourceExe = Environment.ProcessPath;
if (string.IsNullOrWhiteSpace(sourceExe) || !File.Exists(sourceExe))
{
    NativeMessageBox.Show("이 설치 파일의 경로를 확인할 수 없습니다.");
    return 1;
}

string payloadDirectory;
try
{
    payloadDirectory = PayloadExtractor.ExtractFromExecutable(sourceExe);
}
catch (Exception ex)
{
    NativeMessageBox.Show(
        "회사 설치 페이로드를 읽지 못했습니다.\nPackager로 만든 설치본인지 확인하세요.\n\n" + ex.Message);
    return 2;
}

var setupUi = PayloadLayout.GetSetupUiPath(payloadDirectory);
if (!File.Exists(setupUi))
{
    NativeMessageBox.Show("설치 화면 실행 파일이 페이로드에 없습니다.");
    return 3;
}

var start = new ProcessStartInfo
{
    FileName = setupUi,
    WorkingDirectory = Path.GetDirectoryName(setupUi) ?? payloadDirectory,
    UseShellExecute = false
};
foreach (var arg in args)
{
    start.ArgumentList.Add(arg);
}

start.ArgumentList.Add("--source-exe=" + sourceExe);
start.ArgumentList.Add("--payload-dir=" + payloadDirectory);

try
{
    using var process = Process.Start(start);
    if (process is null)
    {
        NativeMessageBox.Show("설치 화면을 시작하지 못했습니다.");
        return 4;
    }

    process.WaitForExit();
    return process.ExitCode;
}
catch (Exception ex)
{
    NativeMessageBox.Show("설치 화면을 실행하지 못했습니다.\n\n" + ex.Message);
    return 4;
}

internal static partial class NativeMessageBox
{
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint hWnd, string text, string caption, uint type);

    public static void Show(string text)
    {
        MessageBoxW(0, text, "SW License Watcher", 0x00000010);
    }
}
