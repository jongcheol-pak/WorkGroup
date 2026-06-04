using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkGroup.Application.Localization;
using WorkGroup.Application.Persistence;
using WorkGroup.Domain.Common;
using WorkGroup.Domain.Groups;

namespace WorkGroup.Infrastructure.Persistence;

/// <summary>
/// 그룹 컬렉션을 단일 JSON 파일(groups.json)에 저장하는 리포지토리.
/// 저장 디렉터리는 생성자로 주입받아 테스트 가능하게 한다(plan.md T6, D7/D8).
/// </summary>
public sealed class JsonGroupRepository : IGroupRepository
{
    private const int CurrentSchemaVersion = 1;
    private const string FileName = "groups.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly ILogger<JsonGroupRepository> _logger;
    private readonly ILocalizer _localizer;
    // 같은 인스턴스의 동시 저장을 직렬화한다(단일 앱 인스턴스 전제 — D11).
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonGroupRepository(string storageDirectory, ILogger<JsonGroupRepository>? logger = null, ILocalizer? localizer = null)
    {
        if (string.IsNullOrWhiteSpace(storageDirectory))
            throw new ArgumentException("저장 디렉터리는 비어 있을 수 없습니다.", nameof(storageDirectory));

        Directory.CreateDirectory(storageDirectory);
        _filePath = Path.Combine(storageDirectory, FileName);
        _logger = logger ?? NullLogger<JsonGroupRepository>.Instance;
        _localizer = localizer ?? NullLocalizer.Instance;
    }

    public async Task<IReadOnlyList<AppGroup>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result> SaveAsync(AppGroup group, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var groups = (await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false)).ToList();
            var index = groups.FindIndex(g => g.Id == group.Id);
            if (index >= 0)
                groups[index] = group; // 갱신
            else
                groups.Add(group);     // 추가

            return await WriteUnlockedAsync(groups, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result> DeleteAsync(GroupId id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var groups = (await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false)).ToList();
            // 없으면 멱등 성공
            groups.RemoveAll(g => g.Id == id);
            return await WriteUnlockedAsync(groups, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<AppGroup>> LoadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return Array.Empty<AppGroup>();

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var dto = await JsonSerializer.DeserializeAsync<GroupsFileDto>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (dto?.Groups is null)
                return Array.Empty<AppGroup>();

            return dto.Groups.Select(MapToDomain).ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // 손상된 파일은 백업으로 옮기고 빈 컬렉션으로 복구한다(plan.md T6 Edge Cases).
            BackupCorruptFile(ex);
            return Array.Empty<AppGroup>();
        }
    }

    private async Task<Result> WriteUnlockedAsync(IReadOnlyList<AppGroup> groups, CancellationToken cancellationToken)
    {
        var dto = new GroupsFileDto(CurrentSchemaVersion, groups.Select(MapToDto).ToList());
        // 원자적 쓰기: 임시 파일에 쓴 뒤 교체(부분 쓰기로 인한 손상 방지).
        var tempPath = _filePath + ".tmp";
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, dto, JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, _filePath, overwrite: true);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "그룹 저장 실패: {Path}", _filePath);
            TryDeleteTemp(tempPath);
            return Result.Fail(_localizer.Get("Infra_Group_SaveFailed"));
        }
    }

    private void BackupCorruptFile(Exception ex)
    {
        try
        {
            var backupPath = _filePath + ".corrupt.bak";
            File.Copy(_filePath, backupPath, overwrite: true);
            _logger.LogWarning(ex, "손상된 groups.json을 백업했습니다: {Backup}", backupPath);
        }
        catch (Exception backupEx) when (backupEx is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(backupEx, "손상 파일 백업에 실패했습니다: {Path}", _filePath);
        }
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 임시 파일 정리 실패는 무시(다음 저장 시 덮어쓴다).
        }
    }

    // ----- 도메인 ↔ DTO 매핑 -----

    private static AppGroup MapToDomain(GroupDto dto)
    {
        var icon = new IconSource(Enum.Parse<IconSourceKind>(dto.Icon.Kind), dto.Icon.Value);
        var apps = dto.Apps.Select(a =>
            new AppEntry(a.DisplayName, a.LaunchTarget, Enum.Parse<AppKind>(a.Kind), a.IconLocation));
        // 레거시 파일(필드 없음)은 ShowPopupHeader가 null → 헤더 표시(true)로 마이그레이션.
        return AppGroup.Restore(new GroupId(dto.Id), dto.Name, icon, apps, dto.ShowPopupHeader ?? true);
    }

    private static GroupDto MapToDto(AppGroup group) => new(
        group.Id.Value,
        group.Name,
        new IconDto(group.Icon.Kind.ToString(), group.Icon.Value),
        group.Apps.Select(a => new AppDto(a.DisplayName, a.LaunchTarget, a.Kind.ToString(), a.IconLocation)).ToList(),
        group.ShowPopupHeader);

    // 직렬화 전용 DTO(스키마 버전 포함 — D7).
    private sealed record GroupsFileDto(int SchemaVersion, List<GroupDto> Groups);
    // ShowPopupHeader는 nullable — 기존 groups.json(필드 없음)도 로드 가능(하위호환). 키명은 ShowPopupHeader 고정.
    private sealed record GroupDto(string Id, string Name, IconDto Icon, List<AppDto> Apps, bool? ShowPopupHeader);
    private sealed record IconDto(string Kind, string Value);
    private sealed record AppDto(string DisplayName, string LaunchTarget, string Kind, string? IconLocation);
}
