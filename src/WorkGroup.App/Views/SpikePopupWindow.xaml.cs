using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WorkGroup.Infrastructure.Interop;

namespace WorkGroup.App.Views;

/// <summary>
/// T2 spike 팝업. 활성화 시 캡처한 커서/모니터 좌표로 작업 표시줄 변 위에 표시된다(plan.md D4).
/// 항상 위·테두리 없음·포커스 손실 시 자동 닫힘.
/// </summary>
public sealed partial class SpikePopupWindow : Window
{
    private const int PopupWidth = 320;
    private const int PopupHeight = 420;

    public SpikePopupWindow(string groupId)
    {
        InitializeComponent();
        GroupIdText.Text = $"수신한 그룹 id: {groupId}";

        ConfigurePresenter();
        PositionNearTaskbar();

        // 포커스를 잃으면 닫는다(런처 팝업 동작).
        Activated += OnActivated;
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
