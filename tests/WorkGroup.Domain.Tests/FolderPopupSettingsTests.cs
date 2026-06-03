using WorkGroup.Domain.Folders;
using Xunit;

namespace WorkGroup.Domain.Tests;

/// <summary>FolderPopupSettings 클램프/기본값 검증.</summary>
public class FolderPopupSettingsTests
{
    [Fact]
    public void Default는_열1_깊이2_숨김false()
    {
        var settings = FolderPopupSettings.Default;

        Assert.Equal(1, settings.ColumnCount);
        Assert.Equal(2, settings.SubfolderDepth);
        Assert.False(settings.ShowHiddenItems);
    }

    [Fact]
    public void Create_정상_범위값은_유지된다()
    {
        var settings = FolderPopupSettings.Create(3, 4, true);

        Assert.Equal(3, settings.ColumnCount);
        Assert.Equal(4, settings.SubfolderDepth);
        Assert.True(settings.ShowHiddenItems);
    }

    [Fact]
    public void Create_하한_미만은_1로_클램프()
    {
        var settings = FolderPopupSettings.Create(0, 0, false);

        Assert.Equal(1, settings.ColumnCount);
        Assert.Equal(1, settings.SubfolderDepth);
    }

    [Fact]
    public void Create_상한_초과는_5로_클램프()
    {
        var settings = FolderPopupSettings.Create(9, 99, false);

        Assert.Equal(5, settings.ColumnCount);
        Assert.Equal(5, settings.SubfolderDepth);
    }
}
