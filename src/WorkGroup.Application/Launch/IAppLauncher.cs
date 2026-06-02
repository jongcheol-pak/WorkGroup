using WorkGroup.Domain.Common;
using WorkGroup.Domain.Groups;

namespace WorkGroup.Application.Launch;

/// <summary>그룹 팝업에서 선택한 앱을 실행한다(plan.md T11).</summary>
public interface IAppLauncher
{
    /// <summary>앱을 실행한다(Win32: 셸 실행, Packaged: AUMID 활성화).</summary>
    Result Launch(AppEntry app);
}
