using WorkGroup.Domain.Common;
using WorkGroup.Domain.Groups;

namespace WorkGroup.Application.Groups;

/// <summary>
/// 그룹 관리 use case 오케스트레이션(plan.md T8).
/// 저장은 아이콘 → .lnk → JSON 순서로 수행하고, JSON 저장이 성공해야 그룹이 "존재"로 간주된다.
/// </summary>
public interface IGroupAppService
{
    /// <summary>저장된 모든 그룹을 불러온다.</summary>
    Task<IReadOnlyList<AppGroup>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>그룹을 생성하거나 갱신한다(아이콘·.lnk·JSON 일괄, 실패 시 부분 산출물 정리).</summary>
    Task<Result> SaveAsync(AppGroup group, CancellationToken cancellationToken = default);

    /// <summary>그룹과 그 .lnk·.ico를 삭제한다(멱등).</summary>
    Task<Result> DeleteAsync(GroupId id, CancellationToken cancellationToken = default);

    /// <summary>저장된 모든 그룹을 .lnk·.ico 산출물과 함께 삭제한다(설정 화면 초기화용, 멱등).</summary>
    Task<Result> ClearAllAsync(CancellationToken cancellationToken = default);

    /// <summary>저장된 그룹과 일치하지 않는 고아 .lnk·.ico를 정리한다(앱 시작 시 호출 권장 — plan.md T8).</summary>
    Task CleanupOrphansAsync(CancellationToken cancellationToken = default);
}
