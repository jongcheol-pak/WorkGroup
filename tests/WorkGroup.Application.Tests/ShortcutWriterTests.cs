using WorkGroup.Infrastructure.Shortcuts;
using Xunit;

namespace WorkGroup.Application.Tests;

/// <summary>ShortcutWriter가 실제 .lnk 파일을 생성하는지 검증(COM IShellLink).</summary>
public sealed class ShortcutWriterTests : IDisposable
{
    private readonly string _dir;

    public ShortcutWriterTests()
        => _dir = Path.Combine(Path.GetTempPath(), "WorkGroupLnkTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void Create_lnk_파일을_생성한다()
    {
        var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
        var lnk = Path.Combine(_dir, "test.lnk");

        new ShortcutWriter().Create(lnk, target, arguments: "--group test", iconPath: target, description: "테스트");

        Assert.True(File.Exists(lnk));
        Assert.True(new FileInfo(lnk).Length > 0);
    }

    [Fact]
    public void Create_빈_타깃이면_예외()
    {
        var lnk = Path.Combine(_dir, "x.lnk");
        Assert.Throws<ArgumentException>(() => new ShortcutWriter().Create(lnk, ""));
    }
}
