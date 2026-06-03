using WorkGroup.Domain.Groups;
using Xunit;

namespace WorkGroup.Domain.Tests;

/// <summary>AppGroup 도메인 불변식 검증.</summary>
public class AppGroupTests
{
    private static AppEntry SampleApp(string name = "Word", string target = @"C:\Office\winword.exe")
        => new(name, target, AppKind.Win32);

    [Fact]
    public void Create_빈_이름이면_실패()
    {
        var result = AppGroup.Create("   ");

        Assert.True(result.IsFailure);
        Assert.Equal("그룹 이름은 필수입니다.", result.Error);
    }

    [Fact]
    public void Create_정상_이름이면_성공하고_멤버는_0개()
    {
        var result = AppGroup.Create("업무");

        Assert.True(result.IsSuccess);
        Assert.Equal("업무", result.Value.Name);
        Assert.Empty(result.Value.Apps);
        Assert.Equal(IconSourceKind.BuiltIn, result.Value.Icon.Kind);
    }

    [Fact]
    public void Create_이름_앞뒤_공백은_제거된다()
    {
        var group = AppGroup.Create("  업무 그룹  ").Value;
        Assert.Equal("업무 그룹", group.Name);
    }

    [Fact]
    public void AddApp_정상_추가()
    {
        var group = AppGroup.Create("업무").Value;

        var result = group.AddApp(SampleApp());

        Assert.True(result.IsSuccess);
        Assert.Single(group.Apps);
    }

    [Fact]
    public void AddApp_같은_실행대상_중복은_실패하고_추가되지_않는다()
    {
        var group = AppGroup.Create("업무").Value;
        group.AddApp(SampleApp());

        // 대소문자만 다른 동일 경로
        var result = group.AddApp(SampleApp(name: "Word 복제", target: @"C:\OFFICE\WINWORD.EXE"));

        Assert.True(result.IsFailure);
        Assert.Single(group.Apps);
    }

    [Fact]
    public void RemoveApp_존재하는_앱_제거()
    {
        var group = AppGroup.Create("업무").Value;
        group.AddApp(SampleApp());

        var result = group.RemoveApp(@"C:\Office\winword.exe");

        Assert.True(result.IsSuccess);
        Assert.Empty(group.Apps);
    }

    [Fact]
    public void RemoveApp_없는_앱_제거는_실패()
    {
        var group = AppGroup.Create("업무").Value;

        var result = group.RemoveApp(@"C:\None\none.exe");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Rename_빈_이름이면_실패()
    {
        var group = AppGroup.Create("업무").Value;
        Assert.True(group.Rename("").IsFailure);
        Assert.Equal("업무", group.Name);
    }

    [Fact]
    public void SetIcon_변경된다()
    {
        var group = AppGroup.Create("업무").Value;

        group.SetIcon(IconSource.FromCustomImage(@"C:\img\icon.ico"));

        Assert.Equal(IconSourceKind.CustomImage, group.Icon.Kind);
        Assert.Equal(@"C:\img\icon.ico", group.Icon.Value);
    }

    [Fact]
    public void Restore_중복_멤버는_제외하고_복원()
    {
        var id = GroupId.New();
        var apps = new[]
        {
            SampleApp(),
            SampleApp(name: "중복", target: @"c:\office\winword.exe")
        };

        var group = AppGroup.Restore(id, "업무", IconSource.DefaultBuiltIn, apps);

        Assert.Equal(id, group.Id);
        Assert.Single(group.Apps);
    }

    [Fact]
    public void AppEntry_빈_실행대상이면_예외()
    {
        Assert.Throws<ArgumentException>(() => new AppEntry("이름", "  ", AppKind.Win32));
    }

    [Fact]
    public void Create_ShowPopupHeader_기본값은_true()
    {
        var group = AppGroup.Create("업무").Value;
        Assert.True(group.ShowPopupHeader);
    }

    [Fact]
    public void Create_ShowPopupHeader_false_지정()
    {
        var group = AppGroup.Create("업무", showPopupHeader: false).Value;
        Assert.False(group.ShowPopupHeader);
    }

    [Fact]
    public void Restore_ShowPopupHeader_기본은_true_명시하면_반영()
    {
        var id = GroupId.New();

        var on = AppGroup.Restore(id, "업무", IconSource.DefaultBuiltIn, Array.Empty<AppEntry>());
        Assert.True(on.ShowPopupHeader);

        var off = AppGroup.Restore(id, "업무", IconSource.DefaultBuiltIn, Array.Empty<AppEntry>(), showPopupHeader: false);
        Assert.False(off.ShowPopupHeader);
    }

    [Fact]
    public void SetShowPopupHeader_변경된다()
    {
        var group = AppGroup.Create("업무").Value;
        group.SetShowPopupHeader(false);
        Assert.False(group.ShowPopupHeader);
    }
}
