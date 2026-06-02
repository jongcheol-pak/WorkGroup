using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WorkGroup.App.ViewModels;
using WorkGroup.Application.Groups;
using WorkGroup.Application.Launch;
using WorkGroup.Infrastructure.Interop;

namespace WorkGroup.App.Views;

/// <summary>
/// 핀된 그룹 아이콘 클릭 시 뜨는 팝업(plan.md T11). 멤버 앱을 아이콘 그리드로 보여주고,
/// 클릭 시 실행한다. 작업 표시줄 변에 배치(D4), 항상 위, 포커스 손실 시 닫힘.
/// </summary>
public sealed partial class GroupPopupWindow : Window
{
    private const int PopupWidth = 360;
    private const int PopupHeight = 440;

    private readonly IGroupAppService _groupService;
    private readonly IAppLauncher _launcher;

    public ObservableCollection<PopupAppItem> Items { get; } = new();

    public GroupPopupWindow(string groupId)
    {
        InitializeComponent();

        // 메인 창과 동일한 저장 테마를 팝업 루트에도 적용한다(plan.md M1 — 별 프로세스라 직접 읽어 적용).
        if (Content is FrameworkElement root)
            root.RequestedTheme = App.Services.GetRequiredService<WorkGroup.App.Services.ThemeService>().Read();

        _groupService = App.Services.GetRequiredService<IGroupAppService>();
        _launcher = App.Services.GetRequiredService<IAppLauncher>();

        ConfigurePresenter();
        PositionNearTaskbar();
        Activated += OnActivated;

        _ = LoadAsync(groupId);
    }

    private async Task LoadAsync(string groupId)
    {
        try
        {
            var groups = await _groupService.GetAllAsync();
            var group = groups.FirstOrDefault(g => g.Id.Value == groupId);
            if (group is null)
            {
                TitleText.Text = "그룹을 찾을 수 없습니다.";
                return;
            }

            TitleText.Text = group.Apps.Count == 0 ? $"{group.Name} (멤버 없음)" : group.Name;
            foreach (var app in group.Apps)
            {
                var item = new PopupAppItem(app);
                Items.Add(item);
                _ = item.LoadIconAsync();
            }
        }
        catch (Exception)
        {
            TitleText.Text = "그룹을 불러오지 못했습니다.";
        }
    }

    private void OnAppClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PopupAppItem item)
        {
            _launcher.Launch(item.App);
            Close();
        }
    }

    private void ConfigurePresenter()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }
    }

    private void PositionNearTaskbar()
    {
        var metrics = new ScreenMetricsProvider().Capture();
        AppWindow.Resize(new SizeInt32(PopupWidth, PopupHeight));
        var placement = TaskbarPopupPositioner.Compute(
            metrics.Monitor, metrics.Work, metrics.CursorX, metrics.CursorY, PopupWidth, PopupHeight);
        AppWindow.Move(new PointInt32(placement.X, placement.Y));
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated)
            Close();
    }
}
