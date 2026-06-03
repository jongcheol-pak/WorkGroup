using WorkGroup.Domain.Folders;
using Xunit;

namespace WorkGroup.Domain.Tests;

/// <summary>FolderShortcut 도메인 불변식 검증.</summary>
public class FolderShortcutTests
{
    [Fact]
    public void Create_정상_이름과_경로면_성공()
    {
        var result = FolderShortcut.Create(1, "Module", @"D:\Module");

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Id);
        Assert.Equal("Module", result.Value.Name);
        Assert.Equal(@"D:\Module", result.Value.Path);
    }

    [Fact]
    public void Create_빈_이름이면_실패()
    {
        var result = FolderShortcut.Create(1, "   ", @"D:\Module");

        Assert.True(result.IsFailure);
        Assert.Equal("폴더 이름은 필수입니다.", result.Error);
    }

    [Fact]
    public void Create_빈_경로면_실패()
    {
        var result = FolderShortcut.Create(1, "Module", "  ");

        Assert.True(result.IsFailure);
        Assert.Equal("폴더 경로는 필수입니다.", result.Error);
    }

    [Fact]
    public void Create_이름과_경로_앞뒤_공백은_제거된다()
    {
        var shortcut = FolderShortcut.Create(2, "  Source  ", @"  D:\Source  ").Value;

        Assert.Equal("Source", shortcut.Name);
        Assert.Equal(@"D:\Source", shortcut.Path);
    }
}
