using WorkGroup.Domain.Common;
using WorkGroup.Domain.Groups;

namespace WorkGroup.Application.Persistence;

/// <summary>
/// 작업 그룹 컬렉션의 영속화 추상화. 구현은 Infrastructure(JSON 파일).
/// </summary>
public interface IGroupRepository
{
    /// <summary>저장된 모든 그룹을 불러온다. 파일이 없거나 손상되면 빈 목록을 반환한다(plan.md T6 Edge Cases).</summary>
    Task<IReadOnlyList<AppGroup>> LoadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>그룹을 추가하거나(없으면) 갱신한다(있으면). 전체 컬렉션을 원자적으로 저장한다.</summary>
    Task<Result> SaveAsync(AppGroup group, CancellationToken cancellationToken = default);

    /// <summary>식별자로 그룹을 삭제한다. 없으면 성공으로 간주(멱등).</summary>
    Task<Result> DeleteAsync(GroupId id, CancellationToken cancellationToken = default);
}
