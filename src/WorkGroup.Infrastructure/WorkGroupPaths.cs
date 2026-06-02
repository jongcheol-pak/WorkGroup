using WorkGroup.Infrastructure.Shortcuts;

namespace WorkGroup.Infrastructure;

/// <summary>
/// 앱이 사용하는 파일 경로를 한곳에 모은다(plan.md D7/D8).
/// 셸이 접근하는 .lnk/.ico와 설정(groups.json)을 모두 MSIX 가상화 대상이 아닌
/// <c>%USERPROFILE%\WorkGroup</c> 아래에 둔다.
/// </summary>
public static class WorkGroupPaths
{
    /// <summary>MSIX 실행 별칭 이름(매니페스트의 AppExecutionAlias와 일치해야 함).</summary>
    public const string AliasExeName = "WorkGroup.exe";

    /// <summary>데이터 루트 (%USERPROFILE%\WorkGroup) — 비가상화.</summary>
    public static string RootDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "WorkGroup");

    /// <summary>그룹별 .lnk 저장 디렉터리.</summary>
    public static string ShortcutsDirectory => Path.Combine(RootDirectory, "Shortcuts");

    /// <summary>그룹별 .ico 저장 디렉터리.</summary>
    public static string IconsDirectory => Path.Combine(RootDirectory, "Icons");

    /// <summary>groups.json이 위치할 디렉터리.</summary>
    public static string ConfigDirectory => RootDirectory;

    /// <summary>실행 별칭의 표준 경로(.lnk 타깃).</summary>
    public static string AliasExePath => ShortcutService.DefaultAliasPath(AliasExeName);
}
