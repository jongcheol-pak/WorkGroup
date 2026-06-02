using WorkGroup.Domain.Groups;
using WorkGroup.Infrastructure;

namespace WorkGroup.App.Services;

/// <summary>
/// 그룹 목록의 그룹 아이콘 표시 보조.
/// 표시 우선순위: 그룹 폴더의 {groupId}.png/.ico → 실패 시 IconSource 기반 폴백(색/멤버 앱).
/// </summary>
public static class GroupIconLoader
{
    /// <summary>그룹의 .ico 파일 경로(작업 표시줄 핀용. 운영 시 %USERPROFILE%\WorkGroup\Groups\{id}\Icons\{id}.ico).</summary>
    public static string GetIconPath(GroupId id) =>
        Path.Combine(WorkGroupPaths.GroupIconsDirectory(id.Value), $"{id.Value}.ico");

    /// <summary>그룹의 목록 표시용 PNG 경로({id}.png). .ico와 함께 그룹 폴더의 Icons에 생성된다.</summary>
    public static string GetPngPath(GroupId id) =>
        Path.Combine(WorkGroupPaths.GroupIconsDirectory(id.Value), $"{id.Value}.png");

    /// <summary>내장 색상 아이콘 식별자의 대략적 표시색(폴백용). 실제 .ico는 IconService가 생성한다.</summary>
    public static Windows.UI.Color ColorForBuiltIn(string iconId) => iconId switch
    {
        "red" => new Windows.UI.Color { A = 255, R = 0xE8, G = 0x11, B = 0x23 },
        "green" => new Windows.UI.Color { A = 255, R = 0x10, G = 0x7C, B = 0x10 },
        "orange" => new Windows.UI.Color { A = 255, R = 0xF7, G = 0x63, B = 0x0C },
        "purple" => new Windows.UI.Color { A = 255, R = 0x5C, G = 0x2D, B = 0x91 },
        _ => new Windows.UI.Color { A = 255, R = 0x51, G = 0x2B, B = 0xD4 }
    };
}
