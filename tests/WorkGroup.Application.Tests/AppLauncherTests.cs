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

    // Packaged 앱은 runas 승격이 불가하므로 Process.Start에 도달하기 전에 실패를 반환한다(부작용 없음).
    // Win32 케이스는 실제 UAC/프로세스 기동을 유발하므로 단위 테스트 대상에서 제외한다.
    [Fact]
    public void LaunchAsAdmin_Packaged는_실패_반환()
    {
        var app = new AppEntry("계산기", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", AppKind.Packaged);
        var result = new AppLauncher().LaunchAsAdmin(app);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void BuildAdminStartInfo_powershell_중개로_RunAs_승격()
    {
        var info = AppLauncher.BuildAdminStartInfo(@"C:\Apps\foo.exe");

        // 직접 runas가 아니라 powershell.exe를 중개로 Start-Process -Verb RunAs를 호출한다(패키지 컨텍스트 분리).
        Assert.Equal("powershell.exe", info.FileName);
        Assert.Contains("Start-Process", info.Arguments);
        Assert.Contains("-Verb RunAs", info.Arguments);
        Assert.Contains(@"C:\Apps\foo.exe", info.Arguments);
        Assert.False(info.UseShellExecute);
    }

    [Fact]
    public void BuildAdminStartInfo_경로_작은따옴표_이스케이프()
    {
        // PowerShell 문자열의 작은따옴표는 두 번으로 이스케이프해야 한다.
        var info = AppLauncher.BuildAdminStartInfo(@"C:\a'b\foo.exe");
        Assert.Contains(@"C:\a''b\foo.exe", info.Arguments);
    }
}
