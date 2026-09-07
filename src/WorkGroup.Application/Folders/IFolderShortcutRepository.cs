using WorkGroup.Domain.Common;
using WorkGroup.Domain.Folders;

namespace WorkGroup.Application.Folders;

/// <summary>
/// 등록 폴더 바로가기 컬렉션의 영속화 추상화. 구현은 Infrastructure(JSON 파일).
/// </summary>
public interface IFolderShortcutRepository
{
    /// <summary>저장된 모든 폴더 바로가기를 불러온다. 파일이 없거나 손상되면 빈 목록을 반환한다.</summary>
    Task<IReadOnlyList<FolderShortcut>> LoadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>새 폴더를 추가한다(Id 자동 부여). 같은 경로가 이미 있으면 실패를 반환한다.</summary>
    Task<Result<FolderShortcut>> AddAsync(string name, string path, CancellationToken cancellationToken = default);

    /// <summary>식별자로 폴더의 이름/경로를 갱신한다. 없으면 실패, 경로 중복이면 실패를 반환한다.</summary>
    Task<Result> UpdateAsync(int id, string name, string path, CancellationToken cancellationToken = default);

    /// <summary>식별자로 폴더를 삭제한다. 없으면 성공으로 간주(멱등).</summary>
    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>등록된 모든 폴더를 삭제한다(설정 화면 초기화용, 멱등).</summary>
    Task<Result> ClearAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 저장된 폴더를 <paramref name="orderedIds"/> 순서로 재배열해 기록한다(트레이 팝업의 표시 순서 = 배열 순서).
    /// 목록에 없는 기존 폴더는 원래 상대 순서로 뒤에 남기고, 저장에 없는 id는 무시한다.
    /// </summary>
    Task<Result> ReorderAsync(IReadOnlyList<int> orderedIds, CancellationToken cancellationToken = default);
}
