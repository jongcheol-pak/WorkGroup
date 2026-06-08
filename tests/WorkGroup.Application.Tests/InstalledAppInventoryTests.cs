using WorkGroup.Domain.Groups;
using WorkGroup.Infrastructure.Inventory;
using Xunit;

namespace WorkGroup.Application.Tests;

/// <summary>InstalledAppInventory의 순수 로직(MergeApps, CreateManualEntry) 단위 테스트.</summary>
public class InstalledAppInventoryUnitTests
{
    private static AppEntry Packaged(string name) => new(name, $"{name}_8wekyb3d8bbwe!App", AppKind.Packaged);
    private static AppEntry Win32(string name) => new(name, $@"C:\Start\{name}.lnk", AppKind.Win32, $@"C:\Start\{name}.lnk");

    [Fact]
    public void MergeApps_표시명_중복은_제거되고_패키지가_우선된다()
    {
        var packaged = new[] { Packaged("Edge") };
        var win32 = new[] { Win32("Edge"), Win32("메모장") };

        var merged = InstalledAppInventory.MergeApps(packaged, win32);

        Assert.Equal(2, merged.Count);
        var edge = merged.Single(a => a.DisplayName == "Edge");
        Assert.Equal(AppKind.Packaged, edge.Kind); // 패키지 우선
    }

    [Fact]
    public void MergeApps_대소문자만_다른_표시명도_중복으로_본다()
    {
        var merged = InstalledAppInventory.MergeApps(
            new[] { Packaged("Slack") },
            new[] { Win32("slack") });

        Assert.Single(merged);
    }

    [Fact]
    public void MergeApps_이름순_정렬()
    {
        var merged = InstalledAppInventory.MergeApps(
            Array.Empty<AppEntry>(),
            new[] { Win32("Zoom"), Win32("Apple"), Win32("Microsoft") });

        Assert.Equal(new[] { "Apple", "Microsoft", "Zoom" }, merged.Select(a => a.DisplayName).ToArray());
    }

    [Fact]
    public void MergeApps_빈_소스는_빈_결과()
    {
        var merged = InstalledAppInventory.MergeApps(Array.Empty<AppEntry>(), Array.Empty<AppEntry>());
        Assert.Empty(merged);
    }

    [Fact]
    public void CreateManualEntry_정상_exe()
    {
        var sut = new InstalledAppInventory();
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");

        var result = sut.CreateManualEntry(exe);

        Assert.True(result.IsSuccess);
        Assert.Equal("notepad", result.Value.DisplayName);
        Assert.Equal(AppKind.Win32, result.Value.Kind);
        Assert.Equal(exe, result.Value.LaunchTarget);
    }

    [Fact]
    public void CreateManualEntry_없는_파일은_실패()
    {
        var sut = new InstalledAppInventory();
        Assert.True(sut.CreateManualEntry(@"C:\none\missing.exe").IsFailure);
    }

    [Fact]
    public void CreateManualEntry_지원하지_않는_확장자는_실패()
    {
        var sut = new InstalledAppInventory();
        var txt = Path.Combine(Path.GetTempPath(), $"wg_{Guid.NewGuid():N}.txt");
        File.WriteAllText(txt, "x");
        try
        {
            Assert.True(sut.CreateManualEntry(txt).IsFailure);
        }
        finally
        {
            File.Delete(txt);
        }
    }

    [Theory]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", true)] // PFN!AppId = 패키지
    [InlineData(@"C:\Program Files\app\app.exe", false)]               // 실제 .exe 경로 = Win32(.lnk 소스 담당)
    [InlineData("Microsoft.Office.WINWORD.EXE.15", false)]             // 점 구분 명시적 AUMID, '!' 없음
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsPackagedAumid_PFN_구분자가_있는_AUMID만_true(string? path, bool expected)
    {
        Assert.Equal(expected, InstalledAppInventory.IsPackagedAumid(path));
    }
}

/// <summary>실제 머신에서 인벤토리를 수집하는 통합 테스트(환경 의존 — Integration 트레이트).</summary>
[Trait("Category", "Integration")]
public class InstalledAppInventoryIntegrationTests
{
    [Fact]
    public async Task GetInstalledApps_앱을_반환하고_모든_항목이_유효하다()
    {
        var sut = new InstalledAppInventory();

        var apps = await sut.GetInstalledAppsAsync();

        Assert.NotEmpty(apps);
        Assert.All(apps, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(a.LaunchTarget));
        });
    }

    [Fact]
    public async Task GetInstalledApps_표시명_중복이_없다()
    {
        var sut = new InstalledAppInventory();

        var apps = await sut.GetInstalledAppsAsync();

        var distinct = apps.Select(a => a.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Assert.Equal(apps.Count, distinct);
    }

    [Fact]
    public async Task GetInstalledApps_패키지_앱이_하나_이상_포함된다()
    {
        var sut = new InstalledAppInventory();

        var apps = await sut.GetInstalledAppsAsync();

        // shell:AppsFolder 패키지 추출이 조용히 0개가 되면(판별 과엄격/COM 실패) 실패하도록 — 개발 머신엔 Store 패키지 앱 상존.
        Assert.Contains(apps, a => a.Kind == AppKind.Packaged);
    }
}
