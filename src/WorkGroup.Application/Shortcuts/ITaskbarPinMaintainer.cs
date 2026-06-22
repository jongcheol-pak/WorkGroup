using WorkGroup.Domain.Common;
using WorkGroup.Domain.Groups;

namespace WorkGroup.Application.Shortcuts;

/// <summary>
/// 작업 표시줄에 핀된 그룹 바로가기(.lnk)의 깨진 실행 별칭 참조를 복구하는 추상화.
/// 핀은 실행 별칭(0바이트 reparse point)을 타깃으로 하는데, MSIX 업데이트마다 별칭이
/// 재생성되면 핀에 캐시된 셸 링크 정보가 stale 상태가 되어 클릭 시 "열 수 없음"이 뜬다.
/// 앱 시작 시 우리 핀을 다시 저장(Save)해 링크 정보를 새로 고쳐 복구한다.
/// </summary>
public interface ITaskbarPinMaintainer
{
    /// <summary>
    /// 작업 표시줄 핀 중 WorkGroup 소유 .lnk를 찾아 별칭/인자/아이콘을 다시 써 stale 참조를 복구한다.
    /// best-effort — 다른 앱의 핀이나 식별되지 않는 핀은 읽기만 하고 변경하지 않는다.
    /// </summary>
    /// <param name="validGroups">현재 유효한 그룹 목록. 이 목록의 식별자를 인자로 가진 핀만 복구 대상이다.</param>
    Result RepairPins(IReadOnlyCollection<AppGroup> validGroups);
}
