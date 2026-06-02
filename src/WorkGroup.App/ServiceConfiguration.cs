using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkGroup.Application.Groups;
using WorkGroup.Application.Icons;
using WorkGroup.Application.Inventory;
using WorkGroup.Application.Launch;
using WorkGroup.Application.Persistence;
using WorkGroup.Application.Shortcuts;
using WorkGroup.Infrastructure;
using WorkGroup.Infrastructure.Icons;
using WorkGroup.Infrastructure.Inventory;
using WorkGroup.Infrastructure.Launch;
using WorkGroup.Infrastructure.Persistence;
using WorkGroup.Infrastructure.Shortcuts;

namespace WorkGroup.App;

/// <summary>
/// DI 컨테이너 구성(plan.md T9 — 조립 단계). 인프라 구현을 비가상화 경로(WorkGroupPaths)로 연결한다.
/// </summary>
public static class ServiceConfiguration
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // 인프라 — 경로/별칭을 WorkGroupPaths로 주입(D7/D8).
        services.AddSingleton<IGroupRepository>(sp =>
            new JsonGroupRepository(
                WorkGroupPaths.ConfigDirectory,
                sp.GetRequiredService<ILogger<JsonGroupRepository>>()));

        services.AddSingleton<IAppInventory, InstalledAppInventory>();
        services.AddSingleton<IIconService, IconService>();
        services.AddSingleton<IAppLauncher, AppLauncher>();

        services.AddSingleton<IShortcutService>(sp =>
            new ShortcutService(
                WorkGroupPaths.ShortcutsDirectory,
                WorkGroupPaths.AliasExePath,
                logger: sp.GetRequiredService<ILogger<ShortcutService>>()));

        // 애플리케이션 서비스.
        services.AddSingleton<IGroupAppService>(sp =>
            new GroupAppService(
                sp.GetRequiredService<IIconService>(),
                sp.GetRequiredService<IShortcutService>(),
                sp.GetRequiredService<IGroupRepository>(),
                WorkGroupPaths.IconsDirectory,
                sp.GetRequiredService<ILogger<GroupAppService>>()));

        // 자동 시작 토글(plan.md T12).
        services.AddSingleton<Services.StartupService>();

        // 앱 테마 적용·영속(plan.md T2/DU5).
        services.AddSingleton<Services.ThemeService>();

        // ViewModel (plan.md T2~T7 — NavigationView 셸 페이지/다이얼로그).
        services.AddTransient<ViewModels.SettingsViewModel>();
        services.AddTransient<ViewModels.AboutViewModel>();
        services.AddTransient<ViewModels.GroupEditViewModel>();
        services.AddTransient<ViewModels.WorkGroupsViewModel>();

        return services.BuildServiceProvider();
    }
}
