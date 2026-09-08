using WorkGroup.Application.Folders;
using WorkGroup.Domain.Common;
using WorkGroup.Domain.Folders;

namespace WorkGroup.App.Tests.Fakes;

/// <summary>
/// <see cref="IFolderShortcutRepository"/>의 테스트 대역. <see cref="FakeGroupAppService"/>와 같은 방식으로
/// 돌려줄 목록·재정렬 결과를 바깥에서 정하고 넘어온 순서를 붙잡는다.
/// </summary>
internal sealed class FakeFolderShortcutRepository : IFolderShortcutRepository
{
    public List<FolderShortcut> Shortcuts { get; } = new();

    /// <summary>다음 <see cref="ReorderAsync"/>가 돌려줄 결과.</summary>
    public Result ReorderResult { get; set; } = Result.Ok();

    /// <summary>마지막 <see cref="ReorderAsync"/>에 넘어온 순서(호출이 없었으면 null).</summary>
    public IReadOnlyList<int>? LastReorder { get; private set; }

    /// <summary><see cref="ReorderAsync"/> 호출 횟수.</summary>
    public int ReorderCallCount { get; private set; }

    /// <summary>이름·경로로 항목을 하나 추가한다(테스트 준비용 — Id는 순번으로 붙인다).</summary>
    public FakeFolderShortcutRepository With(string name, string path)
    {
        Shortcuts.Add(FolderShortcut.Create(Shortcuts.Count + 1, name, path).Value);
        return this;
    }

    public Task<IReadOnlyList<FolderShortcut>> LoadAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<FolderShortcut>>(Shortcuts.ToList());

    public Task<Result<FolderShortcut>> AddAsync(string name, string path, CancellationToken cancellationToken = default)
    {
        var created = FolderShortcut.Create(Shortcuts.Count + 1, name, path);
        if (created.IsSuccess)
            Shortcuts.Add(created.Value);
        return Task.FromResult(created);
    }

    public Task<Result> UpdateAsync(int id, string name, string path, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Ok());

    public Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        Shortcuts.RemoveAll(s => s.Id == id);
        return Task.FromResult(Result.Ok());
    }

    public Task<Result> ClearAllAsync(CancellationToken cancellationToken = default)
    {
        Shortcuts.Clear();
        return Task.FromResult(Result.Ok());
    }

    public Task<Result> ReorderAsync(IReadOnlyList<int> orderedIds, CancellationToken cancellationToken = default)
    {
        ReorderCallCount++;
        LastReorder = orderedIds.ToList();
        return Task.FromResult(ReorderResult);
    }
}
