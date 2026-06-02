using WorkGroup.Domain.Groups;
using WorkGroup.Infrastructure.Shortcuts;
using Xunit;

namespace WorkGroup.Application.Tests;

/// <summary>ShortcutService가 그룹별 .lnk를 생성/삭제하는지 검증.</summary>
public sealed class ShortcutServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _alias;

    public ShortcutServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "WorkGroupShortcutTests", Guid.NewGuid().ToString("N"));
        // 별칭 자리표시자로 실제 존재하는 exe 사용
        _alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private ShortcutService CreateSut() => new(_dir, _alias);

    [Fact]
    public void CreateOrUpdate_그룹_이름으로_lnk_생성()
    {
        var group = AppGroup.Create("업무 그룹").Value;
        var sut = CreateSut();

        var result = sut.CreateOrUpdate(group, _alias);

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.Combine(_dir, group.Id.Value, "업무 그룹.lnk"), result.Value);
        Assert.True(File.Exists(result.Value));
    }

    [Fact]
    public void CreateOrUpdate_파일명_금지문자_치환()
    {
        var group = AppGroup.Create("a/b:c").Value;
        var sut = CreateSut();

        var result = sut.CreateOrUpdate(group, _alias);

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(result.Value));
        Assert.DoesNotContain('/', Path.GetFileName(result.Value));
        Assert.DoesNotContain(':', Path.GetFileName(result.Value));
    }

    [Fact]
    public void Delete_생성한_lnk_제거()
    {
        var group = AppGroup.Create("삭제대상").Value;
        var sut = CreateSut();
        var path = sut.CreateOrUpdate(group, _alias).Value;
        Assert.True(File.Exists(path));

        var result = sut.Delete(group);

        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Delete_없는_lnk도_성공()
    {
        var group = AppGroup.Create("없음").Value;
        Assert.True(CreateSut().Delete(group).IsSuccess);
    }

    [Fact]
    public void 생성자_빈_인자_검증()
    {
        Assert.Throws<ArgumentException>(() => new ShortcutService("", _alias));
        Assert.Throws<ArgumentException>(() => new ShortcutService(_dir, ""));
    }

    [Fact]
    public void CreateOrUpdate_라이터_예외시_실패_반환()
    {
        // IShortcutWriter 주입으로 실패 경로 검증(주입 가능 설계).
        var sut = new ShortcutService(_dir, _alias, new ThrowingWriter());
        var result = sut.CreateOrUpdate(AppGroup.Create("그룹").Value, _alias);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void CleanupOrphans_그룹_폴더_내_잔여_lnk_제거()
    {
        var sut = CreateSut();
        var keep = AppGroup.Create("유지").Value;
        var keepPath = sut.CreateOrUpdate(keep, _alias).Value;

        // 같은 그룹 폴더에 이름 변경 잔여 .lnk를 직접 생성
        var folder = Path.GetDirectoryName(keepPath)!;
        var stale = Path.Combine(folder, "옛이름.lnk");
        File.WriteAllText(stale, "x");

        var result = sut.CleanupOrphans(new[] { keep });

        Assert.True(result.IsSuccess);
        Assert.False(File.Exists(stale));          // 잔여 제거
        Assert.True(File.Exists(keepPath));         // 현재 것 보존
    }

    private sealed class ThrowingWriter : IShortcutWriter
    {
        public void Create(string lnkPath, string targetPath, string? arguments = null, string? iconPath = null, string? description = null)
            => throw new IOException("강제 실패");
    }
}
