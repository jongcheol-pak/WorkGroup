using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkGroup.Application.Folders;
using WorkGroup.Application.Localization;
using WorkGroup.Domain.Common;
using WorkGroup.Domain.Folders;

namespace WorkGroup.Infrastructure.Persistence;

/// <summary>
/// 폴더 바로가기 컬렉션을 단일 JSON 파일(folders.json)에 저장하는 리포지토리.
/// 저장 파일 경로는 생성자로 주입받아 테스트 가능하게 한다. JsonGroupRepository와 동일한
/// 원자적 쓰기·손상 백업·동시성 직렬화 정책을 따른다.
/// </summary>
public sealed class JsonFolderShortcutRepository : IFolderShortcutRepository
{
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly ILogger<JsonFolderShortcutRepository> _logger;
    private readonly ILocalizer _localizer;
    // 같은 인스턴스의 동시 저장을 직렬화한다(단일 앱 인스턴스 전제).
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonFolderShortcutRepository(string filePath, ILogger<JsonFolderShortcutRepository>? logger = null, ILocalizer? localizer = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("저장 파일 경로는 비어 있을 수 없습니다.", nameof(filePath));

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _filePath = filePath;
        _logger = logger ?? NullLogger<JsonFolderShortcutRepository>.Instance;
        _localizer = localizer ?? NullLocalizer.Instance;
    }

    public async Task<IReadOnlyList<FolderShortcut>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task<Result<FolderShortcut>> AddAsync(string name, string path, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = (await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false)).ToList();
            if (IsDuplicatePath(items, path, excludeId: null))
                return Result<FolderShortcut>.Fail(_localizer.Get("Infra_Folder_Duplicate"));

            var nextId = items.Count == 0 ? 1 : items.Max(f => f.Id) + 1;
            var created = FolderShortcut.Create(nextId, name, path);
            if (created.IsFailure)
                return created;

            items.Add(created.Value);
            var write = await WriteUnlockedAsync(items, cancellationToken).ConfigureAwait(false);
            return write.IsSuccess ? created : Result<FolderShortcut>.Fail(write.Error!);
        }
        finally { _gate.Release(); }
    }

    public async Task<Result> UpdateAsync(int id, string name, string path, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = (await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false)).ToList();
            var index = items.FindIndex(f => f.Id == id);
            if (index < 0)
                return Result.Fail(_localizer.Get("Infra_Folder_NotFound"));

            if (IsDuplicatePath(items, path, excludeId: id))
                return Result.Fail(_localizer.Get("Infra_Folder_Duplicate"));

            var updated = FolderShortcut.Create(id, name, path);
            if (updated.IsFailure)
                return Result.Fail(updated.Error!);

            items[index] = updated.Value;
            return await WriteUnlockedAsync(items, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = (await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false)).ToList();
            items.RemoveAll(f => f.Id == id); // 없으면 멱등 성공
            return await WriteUnlockedAsync(items, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    // 경로 중복 검사(대소문자 무시). excludeId가 있으면 자기 자신은 제외(수정 시).
    private static bool IsDuplicatePath(IEnumerable<FolderShortcut> items, string? path, int? excludeId)
    {
        var target = path?.Trim();
        if (string.IsNullOrEmpty(target))
            return false;

        return items.Any(f =>
            (excludeId is null || f.Id != excludeId) &&
            string.Equals(f.Path, target, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<FolderShortcut>> LoadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return Array.Empty<FolderShortcut>();

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var dto = await JsonSerializer.DeserializeAsync<FoldersFileDto>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (dto?.Folders is null)
                return Array.Empty<FolderShortcut>();

            // 이름/경로 검증을 통과한 항목만 복원한다(손상 항목은 건너뜀).
            var list = new List<FolderShortcut>();
            foreach (var f in dto.Folders)
            {
                var created = FolderShortcut.Create(f.Id, f.Name, f.Path);
                if (created.IsSuccess)
                    list.Add(created.Value);
            }
            return list;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // 손상된 파일은 백업으로 옮기고 빈 컬렉션으로 복구한다.
            BackupCorruptFile(ex);
            return Array.Empty<FolderShortcut>();
        }
    }

    private async Task<Result> WriteUnlockedAsync(IReadOnlyList<FolderShortcut> items, CancellationToken cancellationToken)
    {
        var dto = new FoldersFileDto(CurrentSchemaVersion, items.Select(f => new FolderDto(f.Id, f.Name, f.Path)).ToList());
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
            _logger.LogError(ex, "폴더 저장 실패: {Path}", _filePath);
            TryDeleteTemp(tempPath);
            return Result.Fail(_localizer.Get("Infra_Folder_SaveFailed"));
        }
    }

    private void BackupCorruptFile(Exception ex)
    {
        try
        {
            var backupPath = _filePath + ".corrupt.bak";
            File.Copy(_filePath, backupPath, overwrite: true);
            _logger.LogWarning(ex, "손상된 folders.json을 백업했습니다: {Backup}", backupPath);
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

    // 직렬화 전용 DTO(스키마 버전 포함).
    private sealed record FoldersFileDto(int SchemaVersion, List<FolderDto> Folders);
    private sealed record FolderDto(int Id, string Name, string Path);
}
