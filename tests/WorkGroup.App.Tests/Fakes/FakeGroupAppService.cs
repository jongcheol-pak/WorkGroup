using WorkGroup.Application.Groups;
using WorkGroup.Domain.Common;
using WorkGroup.Domain.Groups;

namespace WorkGroup.App.Tests.Fakes;

/// <summary>
/// <see cref="IGroupAppService"/>의 테스트 대역. 돌려줄 목록과 <see cref="ReorderAsync"/>의 결과를
/// 바깥에서 정하고, 그 호출에 실제로 넘어온 순서를 <see cref="LastReorder"/>로 붙잡아 둔다
/// — 순서가 저장까지 갔는지는 인자를 봐야만 알 수 있다.
/// </summary>
internal sealed class FakeGroupAppService : IGroupAppService
{
    public IReadOnlyList<AppGroup> Groups { get; set; } = Array.Empty<AppGroup>();

    /// <summary>다음 <see cref="ReorderAsync"/>가 돌려줄 결과.</summary>
    public Result ReorderResult { get; set; } = Result.Ok();

    /// <summary>마지막 <see cref="ReorderAsync"/>에 넘어온 순서(호출이 없었으면 null).</summary>
    public IReadOnlyList<GroupId>? LastReorder { get; private set; }

    /// <summary><see cref="ReorderAsync"/> 호출 횟수.</summary>
    public int ReorderCallCount { get; private set; }

    public Task<IReadOnlyList<AppGroup>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Groups);

    public Task<Result> SaveAsync(AppGroup group, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Ok());

    public Task<Result> DeleteAsync(GroupId id, CancellationToken cancellationToken = default)
    {
        Groups = Groups.Where(g => g.Id.Value != id.Value).ToList();
        return Task.FromResult(Result.Ok());
    }

    public Task<Result> ReorderAsync(IReadOnlyList<GroupId> orderedIds, CancellationToken cancellationToken = default)
    {
        ReorderCallCount++;
        LastReorder = orderedIds.ToList();
        return Task.FromResult(ReorderResult);
    }

    public Task<Result> ClearAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Ok());

    public Task CleanupOrphansAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
