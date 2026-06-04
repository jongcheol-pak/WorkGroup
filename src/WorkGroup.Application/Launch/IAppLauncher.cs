using WorkGroup.Domain.Common;
using WorkGroup.Domain.Groups;

namespace WorkGroup.Application.Launch;

/// <summary>그룹 팝업에서 선택한 앱을 실행한다(plan.md T11).</summary>
public interface IAppLauncher
{
    /// <summary>앱을 실행한다(Win32: 셸 실행, Packaged: AUMID 활성화).</summary>
    Result Launch(AppEntry app);

    /// <summary>
    /// 앱을 관리자 권한으로 실행한다(Win32만 — UAC 승격).
    /// Packaged 앱은 OS 제약상 관리자 권한 실행이 불가하므로 실패를 반환한다.
    /// </summary>
    Result LaunchAsAdmin(AppEntry app);
}
