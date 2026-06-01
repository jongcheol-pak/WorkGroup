using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkGroup.Application.Shortcuts;
using WorkGroup.Domain.Common;
using WorkGroup.Domain.Groups;
using WorkGroup.Infrastructure.Activation;

namespace WorkGroup.Infrastructure.Shortcuts;

/// <summary>
/// 그룹별 .lnk를 비가상화 경로(%USERPROFILE%\WorkGroup\Shortcuts 등)에 생성한다(plan.md T7, D8).
/// 타깃은 실행 별칭, 인자는 <c>--group {groupId}</c>, 파일명은 표시용 그룹 이름이다.
/// 저장 디렉터리와 별칭 경로는 주입받아 테스트 가능하게 한다.
/// </summary>
public sealed class ShortcutService : IShortcutService
{
    private readonly string _shortcutsDirectory;
    private readonly string _aliasExePath;
    private readonly IShortcutWriter _writer;
    private readonly ILogger<ShortcutService> _logger;

    public ShortcutService(
        string shortcutsDirectory,
        string aliasExePath,
        IShortcutWriter? writer = null,
        ILogger<ShortcutService>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(shortcutsDirectory))
            throw new ArgumentException("바로가기 디렉터리가 비어 있습니다.", nameof(shortcutsDirectory));
        if (string.IsNullOrWhiteSpace(aliasExePath))
            throw new ArgumentException("별칭 실행 경로가 비어 있습니다.", nameof(aliasExePath));

        _shortcutsDirectory = shortcutsDirectory;
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
            Directory.CreateDirectory(_shortcutsDirectory);
            var lnkPath = ShortcutPathFor(group);

            _writer.Create(
                lnkPath,
                _aliasExePath,
                GroupArgs.BuildCommandLineArguments(group.Id.Value),
                iconPath,
                $"{group.Name} - WorkGroup");

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
            if (!Directory.Exists(_shortcutsDirectory))
                return Result.Ok();

            var valid = validGroups
                .Select(g => SanitizeFileName(g.Name) + ".lnk")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var lnk in Directory.EnumerateFiles(_shortcutsDirectory, "*.lnk"))
            {
                if (valid.Contains(Path.GetFileName(lnk)))
                    continue;
                try
                {
                    File.Delete(lnk);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "고아 바로가기 삭제 실패: {Path}", lnk);
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

    private string ShortcutPathFor(AppGroup group)
        => Path.Combine(_shortcutsDirectory, SanitizeFileName(group.Name) + ".lnk");

    /// <summary>파일명에 쓸 수 없는 문자를 '_'로 치환한다.</summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
        return string.IsNullOrEmpty(sanitized) ? "group" : sanitized;
    }
}
