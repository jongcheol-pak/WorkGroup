using System.Runtime.InteropServices;
using System.Text;
using WorkGroup.Domain.Groups;
using WorkGroup.Infrastructure.Shortcuts;
using Xunit;

namespace WorkGroup.Application.Tests;

/// <summary>
/// TaskbarPinMaintainer가 우리 핀만 식별해 재저장(복구)하고 다른 핀은 건드리지 않는지 검증.
/// 임시 폴더 + 실제 IShellLink COM으로 .lnk를 만들고 결과를 다시 읽어 단언한다.
/// </summary>
public sealed class TaskbarPinMaintainerTests : IDisposable
{
    private readonly string _root;
    private readonly string _taskbarDir;
    private readonly string _groupsDir;
    private readonly string _alias;

    public TaskbarPinMaintainerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "WorkGroupPinMaintainerTests", Guid.NewGuid().ToString("N"));
        _taskbarDir = Path.Combine(_root, "TaskBar");
        _groupsDir = Path.Combine(_root, "Groups");
        Directory.CreateDirectory(_taskbarDir);
        Directory.CreateDirectory(_groupsDir);
        // 별칭 자리표시자로 실제 존재하는 exe 사용(ShortcutServiceTests와 동일 패턴).
        _alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private TaskbarPinMaintainer CreateSut() => new(_taskbarDir, _alias, _groupsDir);

    [Fact]
    public void RepairPins_우리_핀_아이콘_복구()
    {
        var group = AppGroup.Create("작업").Value;
        var id = group.Id.Value;

        // 그룹의 현재 .ico 준비(존재만 필요).
        var icoPath = Path.Combine(_groupsDir, id, "Icons", id + ".ico");
        Directory.CreateDirectory(Path.GetDirectoryName(icoPath)!);
        File.WriteAllBytes(icoPath, [0, 0, 1, 0]);

        // 옛 아이콘으로 핀 .lnk 생성(별칭 타깃 + --group id).
        var lnkPath = Path.Combine(_taskbarDir, "작업.lnk");
        var oldIco = Path.Combine(_groupsDir, "old.ico");
        new ShortcutWriter().Create(lnkPath, _alias, "--group " + id, oldIco, "작업");

        var result = CreateSut().RepairPins([group]);

        Assert.True(result.IsSuccess);
        Assert.Equal(_alias, ReadTarget(lnkPath), ignoreCase: true); // 타깃=별칭으로 재설정됨
        Assert.Equal(icoPath, ReadIconLocation(lnkPath));    // 현재 .ico로 갱신됨
        Assert.Equal("--group " + id, ReadArguments(lnkPath)); // 인자 보존(정규화)
    }

    [Fact]
    public void RepairPins_다른_앱_핀_불변()
    {
        var group = AppGroup.Create("작업").Value;

        // --group 인자가 없는 핀(우리 것이 아님).
        var lnkPath = Path.Combine(_taskbarDir, "기타.lnk");
        new ShortcutWriter().Create(lnkPath, _alias, null, null, "기타");
        var before = File.ReadAllBytes(lnkPath);

        var result = CreateSut().RepairPins([group]);

        Assert.True(result.IsSuccess);
        Assert.Equal(before, File.ReadAllBytes(lnkPath)); // 바이트 불변
    }

    [Fact]
    public void RepairPins_미등록_그룹_핀_불변()
    {
        var group = AppGroup.Create("작업").Value;

        // 유효 그룹 목록에 없는 id를 가진 핀.
        var unknownId = Guid.NewGuid().ToString("N");
        var lnkPath = Path.Combine(_taskbarDir, "미등록.lnk");
        new ShortcutWriter().Create(lnkPath, _alias, "--group " + unknownId, null, "미등록");
        var before = File.ReadAllBytes(lnkPath);

        var result = CreateSut().RepairPins([group]);

        Assert.True(result.IsSuccess);
        Assert.Equal(before, File.ReadAllBytes(lnkPath));
    }

    [Fact]
    public void RepairPins_핀_폴더_없으면_성공()
    {
        var group = AppGroup.Create("작업").Value;
        var sut = new TaskbarPinMaintainer(Path.Combine(_root, "NotExist"), _alias, _groupsDir);

        Assert.True(sut.RepairPins([group]).IsSuccess);
    }

    [Fact]
    public void 생성자_빈_인자_검증()
    {
        Assert.Throws<ArgumentException>(() => new TaskbarPinMaintainer("", _alias, _groupsDir));
        Assert.Throws<ArgumentException>(() => new TaskbarPinMaintainer(_taskbarDir, "", _groupsDir));
        Assert.Throws<ArgumentException>(() => new TaskbarPinMaintainer(_taskbarDir, _alias, ""));
    }

    // --- .lnk를 다시 읽어 단언하기 위한 IShellLink COM 헬퍼(InternalsVisibleTo로 internal 타입 접근) ---

    private static string ReadIconLocation(string lnkPath)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(lnkPath, 0);
            var sb = new StringBuilder(260);
            link.GetIconLocation(sb, sb.Capacity, out _);
            return sb.ToString();
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    private static string ReadArguments(string lnkPath)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(lnkPath, 0);
            var sb = new StringBuilder(1024);
            link.GetArguments(sb, sb.Capacity);
            return sb.ToString();
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    private static string ReadTarget(string lnkPath)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(lnkPath, 0);
            var sb = new StringBuilder(260);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0x4); // SLGP_RAWPATH
            return sb.ToString();
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }
}
