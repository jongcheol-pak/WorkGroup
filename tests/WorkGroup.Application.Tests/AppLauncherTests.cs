using WorkGroup.Domain.Groups;
using WorkGroup.Infrastructure.Launch;
using Xunit;

namespace WorkGroup.Application.Tests;

/// <summary>AppLauncher.BuildSpec 실행 스펙 계산 검증(순수).</summary>
public class AppLauncherTests
{
    [Fact]
    public void BuildSpec_Win32는_셸_실행()
    {
        var app = new AppEntry("메모장", @"C:\Win\notepad.exe", AppKind.Win32);
        var spec = AppLauncher.BuildSpec(app);

        Assert.Equal(@"C:\Win\notepad.exe", spec.FileName);
        Assert.Equal(string.Empty, spec.Arguments);
        Assert.True(spec.UseShellExecute);
    }

    [Fact]
    public void BuildSpec_Win32_lnk도_셸_실행()
    {
        var app = new AppEntry("앱", @"C:\Start\app.lnk", AppKind.Win32, @"C:\Start\app.lnk");
        var spec = AppLauncher.BuildSpec(app);
        Assert.EndsWith("app.lnk", spec.FileName);
    }

    [Fact]
    public void BuildSpec_Packaged는_AppsFolder_AUMID()
    {
        var app = new AppEntry("계산기", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", AppKind.Packaged);
        var spec = AppLauncher.BuildSpec(app);

        Assert.Equal("explorer.exe", spec.FileName);
        Assert.Equal(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", spec.Arguments);
        Assert.True(spec.UseShellExecute);
    }
}
