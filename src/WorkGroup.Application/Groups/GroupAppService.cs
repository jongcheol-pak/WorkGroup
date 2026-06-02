using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkGroup.Application.Icons;
using WorkGroup.Application.Persistence;
using WorkGroup.Application.Shortcuts;
using WorkGroup.Domain.Common;
using WorkGroup.Domain.Groups;

namespace WorkGroup.Application.Groups;

/// <summary>
/// 그룹 저장/삭제를 아이콘·바로가기·영속화에 걸쳐 오케스트레이션한다(plan.md T8).
/// 일관성 정책(M3): (1)아이콘 → (2).lnk → (3)groups.json 순서로 만들고,
/// JSON 저장이 성공해야만 그룹이 존재한다. 중간 실패 시 이미 만든 산출물을 역순으로 정리한다.
/// </summary>
public sealed class GroupAppService : IGroupAppService
{
    private readonly IIconService _iconService;
    private readonly IShortcutService _shortcutService;
    private readonly IGroupRepository _repository;
    private readonly string _groupsDirectory;
    private readonly ILogger<GroupAppService> _logger;

    public GroupAppService(
        IIconService iconService,
        IShortcutService shortcutService,
        IGroupRepository repository,
        string groupsDirectory,
        ILogger<GroupAppService>? logger = null)
    {
        _iconService = iconService ?? throw new ArgumentNullException(nameof(iconService));
        _shortcutService = shortcutService ?? throw new ArgumentNullException(nameof(shortcutService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        if (string.IsNullOrWhiteSpace(groupsDirectory))
            throw new ArgumentException("그룹 디렉터리가 비어 있습니다.", nameof(groupsDirectory));
        _groupsDirectory = groupsDirectory;
        _logger = logger ?? NullLogger<GroupAppService>.Instance;
    }

    /// <summary>그룹별 폴더(Groups\{groupId}). 아이콘·.lnk를 이 폴더에 모은다.</summary>
    private string GroupFolder(GroupId id) => Path.Combine(_groupsDirectory, id.Value);

    /// <summary>그룹 아이콘(.ico/.png) 디렉터리(Groups\{groupId}\Icons).</summary>
    private string IconsFolder(GroupId id) => Path.Combine(GroupFolder(id), "Icons");

    public Task<IReadOnlyList<AppGroup>> GetAllAsync(CancellationToken cancellationToken = default)
        => _repository.LoadAllAsync(cancellationToken);

    public async Task<Result> SaveAsync(AppGroup group, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        // (1) 아이콘 생성(그룹 폴더의 Icons 하위) — 실패 시 아무것도 저장하지 않는다.
        var iconResult = await _iconService
            .CreateGroupIconAsync(group.Id, group.Icon, group.Apps, IconsFolder(group.Id), cancellationToken)
            .ConfigureAwait(false);
        if (iconResult.IsFailure)
            return Result.Fail(iconResult.Error!);

        // (2) .lnk 생성/갱신(그룹 폴더 직하) — 실패 시 방금 만든 그룹 폴더 정리.
        var shortcutResult = _shortcutService.CreateOrUpdate(group, iconResult.Value);
        if (shortcutResult.IsFailure)
        {
            TryDeleteGroupFolder(group.Id);
            return Result.Fail(shortcutResult.Error!);
        }

        // (3) 영속화 — 성공해야 그룹이 "존재". 실패 시 .lnk·그룹 폴더 정리.
        var saveResult = await _repository.SaveAsync(group, cancellationToken).ConfigureAwait(false);
        if (saveResult.IsFailure)
        {
            _shortcutService.Delete(group);
            TryDeleteGroupFolder(group.Id);
            return saveResult;
        }

        return Result.Ok();
    }

    public async Task<Result> DeleteAsync(GroupId id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        var groups = await _repository.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        var group = groups.FirstOrDefault(g => g.Id == id);
        // .lnk 삭제는 계약상 ShortcutService에 위임(그룹이 있을 때). 그룹 폴더 자체는 아래에서 통째로 제거한다.
        if (group is not null)
            _shortcutService.Delete(group);

        // 그룹 폴더(아이콘+.lnk 포함)를 통째로 삭제한다.
        TryDeleteGroupFolder(id);
        return await _repository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task CleanupOrphansAsync(CancellationToken cancellationToken = default)
    {
        var groups = await _repository.LoadAllAsync(cancellationToken).ConfigureAwait(false);

        // 유효 그룹 폴더 내부의 잔여 .lnk(이름 변경 등)는 ShortcutService에 위임.
        _shortcutService.CleanupOrphans(groups);

        // 고아 그룹 폴더(유효 id가 아닌 Groups\{name}) 통째로 제거(아이콘+.lnk 동시).
        try
        {
            if (Directory.Exists(_groupsDirectory))
            {
                var validIds = groups
                    .Select(g => g.Id.Value)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var dir in Directory.EnumerateDirectories(_groupsDirectory))
                {
                    var name = Path.GetFileName(dir);
                    // 그룹 id 형식(GUID "N", 32자 hex) 폴더만 대상으로 한다 → 사용자/타 앱이 둔 임의 폴더 오삭제 방지.
                    if (IsGroupIdFolder(name) && !validIds.Contains(name))
                        TryDeleteDirectory(dir);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "고아 그룹 폴더 정리 실패");
        }
    }

    /// <summary>폴더명이 그룹 id(GUID "N": 32자 hex)인지 판정한다(고아 정리 안전 가드).</summary>
    private static bool IsGroupIdFolder(string name)
        => name.Length == 32 && name.All(Uri.IsHexDigit);

    private void TryDeleteGroupFolder(GroupId id) => TryDeleteDirectory(GroupFolder(id));

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "폴더 정리 실패: {Path}", path);
        }
    }
}
