using WorkGroup.Application.Folders;
using WorkGroup.Infrastructure.Folders;
using Xunit;

namespace WorkGroup.Application.Tests;

/// <summary>DirectoryBrowser 열거/숨김 필터/정렬/상태 검증. 임시 폴더로 격리한다.</summary>
public sealed class DirectoryBrowserTests : IDisposable
{
    private readonly string _dir;

    public DirectoryBrowserTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "WorkGroupBrowseTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { /* 테스트 정리 실패 무시 */ }
    }

    [Fact]
    public void Browse_없는_경로면_NotFound()
    {
        var result = new DirectoryBrowser().Browse(Path.Combine(_dir, "nope"), showHidden: false);
        Assert.Equal(DirectoryBrowseStatus.NotFound, result.Status);
    }

    [Fact]
    public void Browse_빈_폴더면_Empty()
    {
        var result = new DirectoryBrowser().Browse(_dir, showHidden: false);
        Assert.Equal(DirectoryBrowseStatus.Empty, result.Status);
    }

    [Fact]
    public void Browse_파일과_폴더를_분류하고_이름순_정렬()
    {
        File.WriteAllText(Path.Combine(_dir, "b.txt"), "x");
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "x");
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));

        var result = new DirectoryBrowser().Browse(_dir, showHidden: false);

        Assert.Equal(DirectoryBrowseStatus.Ok, result.Status);
        Assert.Equal(new[] { "a.txt", "b.txt" }, result.Files.Select(f => f.Name).ToArray());
        Assert.Equal(new[] { "sub" }, result.Folders.Select(f => f.Name).ToArray());
        Assert.True(result.Folders[0].IsDirectory);
        Assert.False(result.Files[0].IsDirectory);
    }

    [Fact]
    public void Browse_숨김_파일은_showHidden에_따라_필터()
    {
        File.WriteAllText(Path.Combine(_dir, "visible.txt"), "x");
        var hidden = Path.Combine(_dir, "hidden.txt");
        File.WriteAllText(hidden, "x");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var browser = new DirectoryBrowser();

        var without = browser.Browse(_dir, showHidden: false);
        Assert.Single(without.Files);
        Assert.Equal("visible.txt", without.Files[0].Name);

        var with = browser.Browse(_dir, showHidden: true);
        Assert.Equal(2, with.Files.Count);
    }
}
