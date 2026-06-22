using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkGroup.Application.Shortcuts;
using WorkGroup.Domain.Common;
using WorkGroup.Domain.Groups;
using WorkGroup.Infrastructure.Activation;

namespace WorkGroup.Infrastructure.Shortcuts;

/// <summary>
/// 작업 표시줄 핀(.lnk)의 깨진 실행 별칭 참조를 복구한다. 핀은 별칭(WorkGroup.exe)을 타깃으로 하는데,
/// 별칭은 0바이트 reparse point라 MSIX 업데이트마다 재생성되어 핀의 셸 링크 캐시가 stale 상태가 된다
/// (클릭 시 "이 항목을 열 수 없음"). 우리 핀을 load → 별칭/인자/아이콘 재설정 → Save 하면
/// 링크 정보가 새로 고쳐져 복구된다. COM 정의(<see cref="IShellLinkW"/> 등)는 ShortcutWriter와 공유한다.
/// 식별 기준: 인자의 <c>--group {id}</c>가 유효 그룹이고, 타깃이 별칭(파일명 일치)인 핀만.
/// </summary>
public sealed class TaskbarPinMaintainer : ITaskbarPinMaintainer
{
    private readonly string _taskbarPinDirectory;
    private readonly string _aliasExePath;
    private readonly string _groupsDirectory;
    private readonly ILogger<TaskbarPinMaintainer> _logger;

    public TaskbarPinMaintainer(
        string taskbarPinDirectory,
        string aliasExePath,
        string groupsDirectory,
        ILogger<TaskbarPinMaintainer>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(taskbarPinDirectory))
            throw new ArgumentException("작업 표시줄 핀 디렉터리가 비어 있습니다.", nameof(taskbarPinDirectory));
        if (string.IsNullOrWhiteSpace(aliasExePath))
            throw new ArgumentException("별칭 실행 경로가 비어 있습니다.", nameof(aliasExePath));
        if (string.IsNullOrWhiteSpace(groupsDirectory))
            throw new ArgumentException("그룹 디렉터리가 비어 있습니다.", nameof(groupsDirectory));

        _taskbarPinDirectory = taskbarPinDirectory;
        _aliasExePath = aliasExePath;
        _groupsDirectory = groupsDirectory;
        _logger = logger ?? NullLogger<TaskbarPinMaintainer>.Instance;
    }

    /// <summary>Windows 작업 표시줄 핀의 표준 저장 폴더(%APPDATA%\...\User Pinned\TaskBar).</summary>
    public static string DefaultTaskbarPinDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");

    public Result RepairPins(IReadOnlyCollection<AppGroup> validGroups)
    {
        ArgumentNullException.ThrowIfNull(validGroups);

        // 핀이 하나도 없으면 폴더 자체가 없을 수 있다 — 무동작.
        if (!Directory.Exists(_taskbarPinDirectory))
            return Result.Ok();

        // 유효 그룹 식별자 집합(대소문자 무시). 핀 인자의 id가 여기 있어야 우리 핀으로 본다.
        var validIds = new HashSet<string>(
            validGroups.Select(g => g.Id.Value), StringComparer.OrdinalIgnoreCase);

        var repaired = 0;
        try
        {
            foreach (var lnk in Directory.EnumerateFiles(_taskbarPinDirectory, "*.lnk"))
            {
                try
                {
                    if (TryRepairPin(lnk, validIds))
                        repaired++;
                }
                catch (Exception ex)
                {
                    // 항목 단위 실패는 해당 핀만 건너뛴다(다른 핀 복구는 계속).
                    _logger.LogDebug(ex, "작업 표시줄 핀 복구 건너뜀: {Path}", lnk);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "작업 표시줄 핀 폴더 열거 실패");
            return Result.Fail("작업 표시줄 핀을 복구하지 못했습니다.");
        }

        if (repaired > 0)
            _logger.LogInformation("작업 표시줄 핀 {Count}개 복구", repaired);
        return Result.Ok();
    }

    // 핀 하나를 검사해 우리 것이면 별칭/인자/아이콘을 다시 써 복구한다. 복구했으면 true.
    private bool TryRepairPin(string lnkPath, HashSet<string> validIds)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(lnkPath, 0); // STGM_READ

            var groupId = GroupArgs.ParseCommandLine(GetArguments(link));
            if (string.IsNullOrEmpty(groupId) || !validIds.Contains(groupId))
                return false; // --group {유효 id}가 아니면 우리 핀이 아니다.

            // 타깃 방어 확인: 읽을 수 있으면 별칭(파일명)이어야 한다(다른 앱 핀 오변경 방지).
            // 별칭은 0바이트 reparse라 raw 경로가 빈 값일 수 있는데, 그때는 id 일치만으로 우리 핀으로 본다.
            var target = GetRawPath(link);
            var aliasName = Path.GetFileName(_aliasExePath);
            if (!string.IsNullOrEmpty(target) &&
                !target.EndsWith(aliasName, StringComparison.OrdinalIgnoreCase))
                return false;

            // 별칭/인자를 정규값으로 다시 쓴다(재저장이 stale 셸 링크 정보를 새로 고친다).
            link.SetPath(_aliasExePath);
            link.SetArguments(GroupArgs.BuildCommandLineArguments(groupId));

            var icoPath = Path.Combine(_groupsDirectory, groupId, "Icons", groupId + ".ico");
            if (File.Exists(icoPath))
                link.SetIconLocation(icoPath, 0);

            ((IPersistFile)link).Save(lnkPath, true);
            NotifyShellUpdated(lnkPath);
            return true;
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    private static string GetArguments(IShellLinkW link)
    {
        var sb = new StringBuilder(1024);
        link.GetArguments(sb, sb.Capacity);
        return sb.ToString();
    }

    // 링크에 저장된 raw 타깃 경로(해석 없이). 별칭처럼 0바이트 reparse면 빈 문자열일 수 있다.
    private static string GetRawPath(IShellLinkW link)
    {
        var sb = new StringBuilder(260);
        link.GetPath(sb, sb.Capacity, IntPtr.Zero, SLGP_RAWPATH);
        return sb.ToString();
    }

    // 셸에 .lnk 변경을 알려 아이콘 캐시/작업 표시줄 렌더를 갱신한다(best-effort).
    private void NotifyShellUpdated(string lnkPath)
    {
        var p = IntPtr.Zero;
        try
        {
            p = Marshal.StringToCoTaskMemUni(lnkPath);
            SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW | SHCNF_FLUSH, p, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "셸 변경 알림 실패: {Path}", lnkPath);
        }
        finally
        {
            if (p != IntPtr.Zero)
                Marshal.FreeCoTaskMem(p);
        }
    }

    // IShellLink::GetPath 플래그 — 해석/환경변수 확장 없이 저장된 raw 경로를 얻는다.
    private const uint SLGP_RAWPATH = 0x4;
    // SHChangeNotify 상수. wEventId는 Win32 LONG(signed)이므로 int로 맞춘다.
    private const int SHCNE_UPDATEITEM = 0x00002000;
    private const uint SHCNF_PATHW = 0x0005;
    private const uint SHCNF_FLUSH = 0x1000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
