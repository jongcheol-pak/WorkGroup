using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkGroup.Application.Shortcuts;
using WorkGroup.Domain.Common;
using WorkGroup.Domain.Groups;
using WorkGroup.Infrastructure.Activation;

namespace WorkGroup.Infrastructure.Shortcuts;

/// <summary>
/// 그룹별 .lnk를 그룹 폴더(<c>%USERPROFILE%\WorkGroup\Groups\{groupId}\</c>)에 생성한다.
/// 타깃은 실행 별칭, 인자는 <c>--group {groupId}</c>, 파일명은 표시용 그룹 이름이다.
/// 그룹 폴더 루트(Groups)와 별칭 경로는 주입받아 테스트 가능하게 한다.
/// </summary>
public sealed class ShortcutService : IShortcutService
{
    private readonly string _groupsDirectory;
    private readonly string _aliasExePath;
    private readonly IShortcutWriter _writer;
    private readonly ILogger<ShortcutService> _logger;

    public ShortcutService(
        string groupsDirectory,
        string aliasExePath,
        IShortcutWriter? writer = null,
        ILogger<ShortcutService>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(groupsDirectory))
            throw new ArgumentException("그룹 디렉터리가 비어 있습니다.", nameof(groupsDirectory));
        if (string.IsNullOrWhiteSpace(aliasExePath))
            throw new ArgumentException("별칭 실행 경로가 비어 있습니다.", nameof(aliasExePath));

        _groupsDirectory = groupsDirectory;
        _aliasExePath = aliasExePath;
        _writer = writer ?? new ShortcutWriter();
        _logger = logger ?? NullLogger<ShortcutService>.Instance;
    }

    /// <summary>%LOCALAPPDATA%\Microsoft\WindowsApps\{aliasExeName} — MSIX 실행 별칭의 표준 경로.</summary>
    public static string DefaultAliasPath(string aliasExeName) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "WindowsApps", aliasExeName);

    public Result<string> CreateOrUpdate(AppGroup group, string iconPath)
    {
        ArgumentNullException.ThrowIfNull(group);

        try
        {
            var folder = GroupFolder(group);
            Directory.CreateDirectory(folder);
            var lnkPath = ShortcutPathFor(group);

            // 이름 변경 등으로 남은 다른 .lnk를 정리한다(그룹 폴더당 .lnk 하나 유지).
            DeleteOtherShortcuts(folder, lnkPath);

            _writer.Create(
                lnkPath,
                _aliasExePath,
                GroupArgs.BuildCommandLineArguments(group.Id.Value),
                iconPath,
                $"{group.Name} - 작업 관리");

            return Result<string>.Ok(lnkPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "그룹 바로가기 생성 실패: {Group}", group.Name);
            return Result<string>.Fail("바로가기를 생성하지 못했습니다.");
        }
    }

    public Result Delete(AppGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        try
        {
            var lnkPath = ShortcutPathFor(group);
            if (File.Exists(lnkPath))
                File.Delete(lnkPath);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "그룹 바로가기 삭제 실패: {Group}", group.Name);
            return Result.Fail("바로가기를 삭제하지 못했습니다.");
        }
    }

    public Result CleanupOrphans(IReadOnlyCollection<AppGroup> validGroups)
    {
        ArgumentNullException.ThrowIfNull(validGroups);

        try
        {
            // 각 유효 그룹 폴더에서 현재 이름의 .lnk만 남기고 나머지(이름 변경 잔여)를 제거한다.
            // 그룹 폴더 자체의 고아(삭제된 그룹)는 상위(GroupAppService)가 폴더째 삭제한다.
            foreach (var group in validGroups)
            {
                var folder = GroupFolder(group);
                if (!Directory.Exists(folder))
                    continue;

                var keep = ShortcutPathFor(group);
                foreach (var lnk in Directory.EnumerateFiles(folder, "*.lnk"))
                {
                    if (string.Equals(lnk, keep, StringComparison.OrdinalIgnoreCase))
                        continue;
                    try
                    {
                        File.Delete(lnk);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        _logger.LogWarning(ex, "잔여 바로가기 삭제 실패: {Path}", lnk);
                    }
                }
            }

            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "고아 바로가기 정리 실패");
            return Result.Fail("고아 바로가기 정리에 실패했습니다.");
        }
    }

    public string GetShortcutPath(AppGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return ShortcutPathFor(group);
    }

    /// <summary>그룹별 폴더(Groups\{groupId}).</summary>
    private string GroupFolder(AppGroup group)
        => Path.Combine(_groupsDirectory, group.Id.Value);

    private string ShortcutPathFor(AppGroup group)
        => Path.Combine(GroupFolder(group), SanitizeFileName(group.Name) + ".lnk");

    /// <summary>그룹 폴더 안에서 keepPath를 제외한 .lnk를 제거한다(best-effort).</summary>
    private void DeleteOtherShortcuts(string folder, string keepPath)
    {
        foreach (var lnk in Directory.EnumerateFiles(folder, "*.lnk"))
        {
            if (string.Equals(lnk, keepPath, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                File.Delete(lnk);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "기존 바로가기 삭제 실패: {Path}", lnk);
            }
        }
    }

    /// <summary>파일명에 쓸 수 없는 문자를 '_'로 치환한다.</summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
        return string.IsNullOrEmpty(sanitized) ? "group" : sanitized;
    }
}
