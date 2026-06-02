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
    private const string IconExtension = ".ico";
    private const string PngExtension = ".png";

    private readonly IIconService _iconService;
    private readonly IShortcutService _shortcutService;
    private readonly IGroupRepository _repository;
    private readonly string _iconsDirectory;
    private readonly ILogger<GroupAppService> _logger;

    public GroupAppService(
        IIconService iconService,
        IShortcutService shortcutService,
        IGroupRepository repository,
        string iconsDirectory,
        ILogger<GroupAppService>? logger = null)
    {
        _iconService = iconService ?? throw new ArgumentNullException(nameof(iconService));
        _shortcutService = shortcutService ?? throw new ArgumentNullException(nameof(shortcutService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        if (string.IsNullOrWhiteSpace(iconsDirectory))
            throw new ArgumentException("아이콘 디렉터리가 비어 있습니다.", nameof(iconsDirectory));
        _iconsDirectory = iconsDirectory;
        _logger = logger ?? NullLogger<GroupAppService>.Instance;
    }

    public Task<IReadOnlyList<AppGroup>> GetAllAsync(CancellationToken cancellationToken = default)
        => _repository.LoadAllAsync(cancellationToken);

    public async Task<Result> SaveAsync(AppGroup group, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        // (1) 아이콘 생성 — 실패 시 아무것도 저장하지 않는다.
        var iconResult = await _iconService
            .CreateGroupIconAsync(group.Id, group.Icon, group.Apps, _iconsDirectory, cancellationToken)
            .ConfigureAwait(false);
        if (iconResult.IsFailure)
            return Result.Fail(iconResult.Error!);

        // (2) .lnk 생성/갱신 — 실패 시 방금 만든 아이콘 정리.
        var shortcutResult = _shortcutService.CreateOrUpdate(group, iconResult.Value);
        if (shortcutResult.IsFailure)
        {
            TryDeleteIcon(group.Id);
            return Result.Fail(shortcutResult.Error!);
        }

        // (3) 영속화 — 성공해야 그룹이 "존재". 실패 시 .lnk·아이콘 정리.
        var saveResult = await _repository.SaveAsync(group, cancellationToken).ConfigureAwait(false);
        if (saveResult.IsFailure)
        {
            _shortcutService.Delete(group);
            TryDeleteIcon(group.Id);
            return saveResult;
        }

        return Result.Ok();
    }

    public async Task<Result> DeleteAsync(GroupId id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        var groups = await _repository.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        var group = groups.FirstOrDefault(g => g.Id == id);
        // 저장소에 그룹이 없으면 .lnk는 표시명 기반이라 경로를 추론할 수 없어 건너뛴다.
        // 이 경우 남을 수 있는 고아 .lnk는 CleanupOrphansAsync가 정리한다.
        if (group is not null)
            _shortcutService.Delete(group);

        TryDeleteIcon(id);
        return await _repository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task CleanupOrphansAsync(CancellationToken cancellationToken = default)
    {
        var groups = await _repository.LoadAllAsync(cancellationToken).ConfigureAwait(false);

        // .lnk 고아: ShortcutService가 자신의 디렉터리/명명 규칙으로 정리.
        _shortcutService.CleanupOrphans(groups);

        // .ico 고아: {groupId}.ico 규칙으로 유효 그룹에 없는 파일 제거.
        try
        {
            if (Directory.Exists(_iconsDirectory))
            {
                var validIds = groups
                    .Select(g => g.Id.Value)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // {groupId}.ico / {groupId}.png 모두 유효 그룹 기준으로 정리.
                foreach (var ext in new[] { IconExtension, PngExtension })
                    foreach (var file in Directory.EnumerateFiles(_iconsDirectory, "*" + ext))
                    {
                        if (!validIds.Contains(Path.GetFileNameWithoutExtension(file)))
                            TryDeleteFile(file);
                    }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "고아 아이콘 정리 실패");
        }
    }

    private void TryDeleteIcon(GroupId id)
    {
        // .ico(작업 표시줄)와 목록 표시용 .png를 함께 정리한다.
        TryDeleteFile(Path.Combine(_iconsDirectory, id.Value + IconExtension));
        TryDeleteFile(Path.Combine(_iconsDirectory, id.Value + PngExtension));
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "파일 정리 실패: {Path}", path);
        }
    }
}
