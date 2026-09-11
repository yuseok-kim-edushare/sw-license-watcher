using System.Security.Principal;

namespace SwLicenseWatcher.Setup.Core;

public interface IAdministratorPrivilege
{
    bool IsElevated { get; }
}

public sealed class UnrestrictedAdministratorPrivilege : IAdministratorPrivilege
{
    public static UnrestrictedAdministratorPrivilege Instance { get; } = new();

    public bool IsElevated => true;
}

public sealed class WindowsAdministratorPrivilege : IAdministratorPrivilege
{
    public const string RequiredMessage =
        "관리자 권한이 필요합니다. Program Files와 Windows 서비스를 설치하거나 제거하려면 관리자로 다시 실행하세요.";

    public static WindowsAdministratorPrivilege Current { get; } = new();

    public bool IsElevated
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
