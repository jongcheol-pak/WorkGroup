using WorkGroup.Domain.Common;
using WorkGroup.Domain.Groups;

namespace WorkGroup.Application.Shortcuts;

/// <summary>
/// 그룹을 작업 표시줄에 핀하기 위한 .lnk 바로가기 생성/삭제 추상화(plan.md T7).
/// .lnk는 실행 별칭을 타깃으로, 인자로 그룹 식별자를 전달한다(클릭 시 해당 그룹 팝업).
/// </summary>
public interface IShortcutService
{
    /// <summary>그룹용 .lnk를 생성하거나 갱신하고 그 경로를 반환한다.</summary>
    Result<string> CreateOrUpdate(AppGroup group, string iconPath);

    /// <summary>그룹용 .lnk를 삭제한다(없으면 성공으로 간주).</summary>
    Result Delete(AppGroup group);

    /// <summary>유효 그룹 목록에 없는 .lnk(고아)를 정리한다(plan.md T8 일관성 정책).</summary>
    Result CleanupOrphans(IReadOnlyCollection<AppGroup> validGroups);
}
