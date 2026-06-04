using System;
using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using WorkGroup.App.Services;
using WorkGroup.App.ViewModels;
using WorkGroup.Application.Groups;
using WorkGroup.Application.Launch;
using WorkGroup.Infrastructure;
using WorkGroup.Infrastructure.Activation;
using WorkGroup.Infrastructure.Interop;

namespace WorkGroup.App.Views;

/// <summary>
/// 핀된 그룹 아이콘 클릭 시 뜨는 팝업(plan.md T11). 멤버 앱을 아이콘 그리드로 보여주고,
/// 클릭 시 실행한다. 작업 표시줄 변에 배치(D4), 항상 위, 포커스 손실 시 닫힘.
/// </summary>
public sealed partial class GroupPopupWindow : Window
{
    // 콘텐츠 측정 전 초기 배치/최소 너비. 로드 후 아이콘 1행 너비에 맞춰 늘리거나 폭 상한으로 줄인다.
    private const int InitialPopupWidth = 360;
    // 1행 너비가 작업영역을 넘지 않도록 좌우로 확보하는 여백 합(px). 초과분은 가로 스크롤.
    private const int WorkAreaMargin = 24;
    // 콘텐츠 측정 전 초기 배치에 쓰는 기본 높이. 로드 후 아이콘 그리드 크기에 맞춰 줄인다.
    private const int InitialPopupHeight = 200;
    // 측정/콘텐츠 확정 전까지 창을 숨겨두는 화면 밖 좌표(여기서 리사이즈가 끝나 점프·깜빡임이 보이지 않음).
    private const int OffScreen = -32000;
    // 세로 1열 콘텐츠 폭 좌우에 더하는 루트 Grid 가로 패딩 합(Padding="12,12,12,0" → 좌 12 + 우 12).
    private const int VerticalContentSidePadding = 24;
    // 세로 스크롤바가 생길 때 콘텐츠 폭에 더해 잘림을 막는 근사 스크롤바 폭(px).
    private const int VerticalScrollBarWidth = 16;

    private readonly IGroupAppService _groupService;
    private readonly IAppLauncher _launcher;
    // 핀 클릭 시점의 커서/모니터 좌표(표시를 미뤄도 클릭 위치 기준으로 배치하도록 생성자에서 1회 캡처).
    private readonly ScreenMetricsProvider.Metrics _metrics;
    // "그룹 수정" 메뉴에서 메인 앱에 편집 활성화 인자를 넘길 때 사용하는 그룹 식별자.
    private readonly string _groupId;

    // 현재 적용된 창 너비(px). 아이콘 1행 콘텐츠 너비에 맞춰 동적으로 결정한다(폭 상한까지).
    private int _popupWidth = InitialPopupWidth;
    // 마지막으로 적용한 창 너비/높이(px). 동일 값이면 재조정을 건너뛰어 SizeChanged 무한 루프를 막는다.
    private int _lastAppliedWidth = -1;
    private int _lastAppliedHeight = -1;
    // 콘텐츠 확정 후 작업 표시줄 정위치로 한 번 이동해 표시했는지. 그 전까지는 화면 밖에 머문다.
    private bool _positioned;
    // 작업 표시줄이 좌/우 변에 붙었는지. 참이면 아이콘을 세로 1열로 배치하고 측정도 세로 기준으로 한다.
    private readonly bool _isVertical;

    // 앱 항목(PopupAppItem)과 마지막 "+" 추가버튼 항목(PopupAddButtonItem)이 섞이므로 object 컬렉션.
    public ObservableCollection<object> Items { get; } = new();

    public GroupPopupWindow(string groupId)
    {
        InitializeComponent();

        _groupId = groupId;

        // 메인 창과 동일한 재질(Mica)을 팝업에도 적용해 흰 외곽선 없는 표준 창 프레임으로 만든다.
        SystemBackdrop = new MicaBackdrop();

        // 콘텐츠를 타이틀바 영역까지 확장해 상단 빈 캡션 여백을 없앤다(타이틀바는 ConfigurePresenter에서 숨김).
        ExtendsContentIntoTitleBar = true;

        // 메인 창과 동일한 저장 테마를 팝업 루트에도 적용한다(plan.md M1 — 별 프로세스라 직접 읽어 적용).
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = App.Services.GetRequiredService<WorkGroup.App.Services.ThemeService>().Read();
            // 아이템 컨테이너가 실제로 생성·배치되면 콘텐츠 크기가 바뀌므로, 그 시점에 창 높이를 다시 맞춘다.
            root.SizeChanged += (_, _) => AdjustToContent();
        }

        _groupService = App.Services.GetRequiredService<IGroupAppService>();
        _launcher = App.Services.GetRequiredService<IAppLauncher>();

        ConfigurePresenter();
        // 핀 클릭 위치를 즉시 캡처(표시는 콘텐츠 확정 후로 미뤄도 이 좌표 기준으로 배치).
        _metrics = new ScreenMetricsProvider().Capture();

        // 작업 표시줄 변을 판정해 좌/우면 세로 배치로 전환한다. 아이템 로드(LoadAsync) 전에
        // ItemsPanel을 1회 교체해 ItemsHost 재생성·깜빡임을 피한다.
        var edge = TaskbarPopupPositioner.DetectEdge(_metrics.Monitor, _metrics.Work);
        _isVertical = edge is TaskbarEdge.Left or TaskbarEdge.Right;
        if (Content is FrameworkElement contentRoot)
            AppsGrid.ItemsPanel = (ItemsPanelTemplate)contentRoot.Resources[
                _isVertical ? "VerticalItemsPanel" : "HorizontalItemsPanel"];

        // 측정이 끝날 때까지 화면 밖에 둔다 → 초기 리사이즈/깜빡임이 사용자에게 보이지 않음.
        AppWindow.Resize(new SizeInt32(InitialPopupWidth, InitialPopupHeight));
        AppWindow.Move(new PointInt32(OffScreen, OffScreen));
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
                TitleText.Visibility = Visibility.Visible; // 에러 메시지는 헤더 설정과 무관하게 항상 표시
                TitleText.Text = LocalizationService.Current?.Get("GroupPopup_NotFound") ?? string.Empty;
                return;
            }

            // 그룹 설정에 따라 이름 헤더 표시/숨김(숨김 시 텍스트 설정 불필요).
            TitleText.Visibility = group.ShowPopupHeader ? Visibility.Visible : Visibility.Collapsed;
            if (group.ShowPopupHeader)
                TitleText.Text = group.Apps.Count == 0
                    ? LocalizationService.Current?.Get("GroupPopup_MemberlessFormat", group.Name) ?? group.Name
                    : group.Name;

            foreach (var app in group.Apps)
            {
                var item = new PopupAppItem(app);
                Items.Add(item);
                // 아이콘 로드는 기다리지 않고 백그라운드로 시작한다(표시를 막지 않음 → 핀 클릭 즉시 팝업이 뜬다).
                // 항목 박스가 고정 48px라 아이콘 로드 여부와 무관하게 레이아웃이 확정되므로 먼저 표시해도 리사이즈 점프가 없고,
                // 각 아이콘은 OneWay 바인딩으로 로드 완료 시 자기 박스 안에 채워진다.
                _ = item.LoadIconAsync();
            }

            // 정상 그룹이면(멤버 0개여도) 목록 끝에 "그룹 편집" 추가버튼을 둔다(NotFound/예외 경로에는 추가 안 함).
            Items.Add(new PopupAddButtonItem());
        }
        catch (Exception)
        {
            TitleText.Visibility = Visibility.Visible; // 에러 메시지는 항상 표시
            TitleText.Text = LocalizationService.Current?.Get("GroupPopup_LoadFailed") ?? string.Empty;
        }
        finally
        {
            // 콘텐츠 확정 후 한 프레임 뒤에 실제 높이로 측정하고 작업 표시줄 정위치로 이동해 표시한다.
            DispatcherQueue.TryEnqueue(RevealAtTaskbar);
        }
    }

    /// <summary>
    /// 아이콘 줄 콘텐츠의 실제 크기를 측정해 창 크기를 맞춘다(빈 여백 제거).
    /// 작업 표시줄 변에 따라 가로 1행/세로 1열로 분기 측정하고, 공통으로 표준 프레임(chrome) 보정 후 적용한다.
    /// </summary>
    private void AdjustToContent()
    {
        if (Content is not FrameworkElement root)
            return;

        double scale = root.XamlRoot?.RasterizationScale ?? 1.0;

        // 변 방향에 맞춰 콘텐츠 자연 크기를 측정한다(가로=너비 우선, 세로=높이 우선).
        var (finalWidth, contentHeight) = _isVertical
            ? MeasureVerticalContent(root, scale)
            : MeasureHorizontalContent(root, scale);

        // 표준 프레임(테두리/캡션 등 비클라이언트)만큼 더해 클라이언트 영역이 콘텐츠를 다 담게 한다(잘림 방지).
        // 높이뿐 아니라 너비에도 보정해야 client 폭이 테두리만큼 줄어 콘텐츠가 잘리고 스크롤이 생기는 것을 막는다.
        int chrome = AppWindow.Size.Height - AppWindow.ClientSize.Height;
        if (chrome < 0)
            chrome = 0;
        int hChrome = AppWindow.Size.Width - AppWindow.ClientSize.Width;
        if (hChrome < 0)
            hChrome = 0;

        int total = contentHeight + chrome;
        int windowWidth = finalWidth + hChrome;
        // SizeChanged가 Resize로 재발생해도 같은 크기면 무시해 무한 루프를 막는다.
        if (windowWidth == _lastAppliedWidth && total == _lastAppliedHeight)
            return;
        _lastAppliedWidth = windowWidth;
        _lastAppliedHeight = total;
        // 배치/이동도 실제 창 너비(테두리 포함)를 기준으로 해야 정렬이 어긋나지 않는다(우측 변 우측 정렬에 필수).
        _popupWidth = windowWidth;

        AppWindow.Resize(new SizeInt32(windowWidth, total));
        // 표시(Reveal) 전에는 화면 밖에서 크기만 맞추고, 이미 표시 중이면 정위치도 따라 갱신한다.
        if (_positioned)
            MoveToTaskbar(total);
    }

    /// <summary>
    /// 가로 1행(상/하 작업 표시줄): 아이콘 1행의 자연 너비를 작업영역 폭 상한으로 클램프(초과분 가로 스크롤),
    /// 그 너비에서 높이를 재측정한다. 반환은 (콘텐츠 너비, 콘텐츠 높이) 픽셀.
    /// </summary>
    private (int Width, int Height) MeasureHorizontalContent(FrameworkElement root, double scale)
    {
        // 가로 스크롤이 켜져 있으면 내부 ScrollViewer가 "스크롤 가능 방향은 주어진 만큼만 차지"하는 성질 때문에
        // 무한 너비 측정 시 콘텐츠 자연 너비를 부정확하게 보고한다 → 측정 동안은 가로 스크롤을 꺼서 정확히 측정한다.
        ScrollViewer.SetHorizontalScrollMode(AppsGrid, ScrollMode.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(AppsGrid, ScrollBarVisibility.Disabled);
        root.UpdateLayout();

        // 1) 너비·높이 모두 무제한으로 측정해 아이콘 1행이 필요로 하는 자연 너비를 구한다.
        root.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        int desiredWidth = (int)Math.Ceiling(root.DesiredSize.Width * scale);
        if (desiredWidth <= 0)
            desiredWidth = InitialPopupWidth;

        // 2) 작업영역 폭(좌우 여백 확보)을 상한으로 클램프 — 초과분은 가로 스크롤로 처리한다.
        int maxWidth = Math.Max(InitialPopupWidth, _metrics.Work.Width - WorkAreaMargin);
        int finalWidth = Math.Min(desiredWidth, maxWidth);

        // 콘텐츠가 상한을 넘을 때만 가로 스크롤을 켠다(그 외엔 창이 콘텐츠에 정확히 맞아 스크롤이 불필요·미표시).
        if (desiredWidth > maxWidth)
        {
            ScrollViewer.SetHorizontalScrollMode(AppsGrid, ScrollMode.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(AppsGrid, ScrollBarVisibility.Auto);
        }

        // 3) 확정 너비로 높이를 재측정한다(가로 스크롤바가 생기면 그만큼 높이에 반영해 잘림을 막는다).
        root.Measure(new Windows.Foundation.Size(finalWidth / scale, double.PositiveInfinity));
        int contentHeight = (int)Math.Ceiling(root.DesiredSize.Height * scale);
        if (contentHeight <= 0)
            contentHeight = InitialPopupHeight;

        return (finalWidth, contentHeight);
    }

    /// <summary>
    /// 세로 1열(좌/우 작업 표시줄): 아이콘 1열의 자연 높이를 작업영역 높이 상한으로 클램프(초과분 세로 스크롤),
    /// 그 높이에서 너비를 재측정하고 너비도 작업영역 폭 상한으로 클램프한다. 반환은 (콘텐츠 너비, 콘텐츠 높이) 픽셀.
    /// </summary>
    private (int Width, int Height) MeasureVerticalContent(FrameworkElement root, double scale)
    {
        // 측정 동안은 양축 스크롤을 모두 꺼서 자연 길이를 정확히 측정한다(켜져 있으면 그 축 자연 길이를 부정확 보고).
        ScrollViewer.SetVerticalScrollMode(AppsGrid, ScrollMode.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(AppsGrid, ScrollBarVisibility.Disabled);
        ScrollViewer.SetHorizontalScrollMode(AppsGrid, ScrollMode.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(AppsGrid, ScrollBarVisibility.Disabled);
        root.UpdateLayout();

        int maxWidth = Math.Max(InitialPopupWidth, _metrics.Work.Width - WorkAreaMargin);

        // GridView는 measure 시 주어진 가용 폭을 그대로 DesiredSize로 반환하고(HorizontalAlignment=Left는
        // arrange만 바꿔 창 크기엔 영향 없음), 무한 폭으로 줘도 cross축 너비를 부정확 보고한다. 따라서 세로 1열
        // 콘텐츠 폭은 GridView를 거치지 않고 내부 패널(ItemsPanelRoot)과 헤더를 직접 측정해 구한다.
        int contentWidth = 0;
        if (AppsGrid.ItemsPanelRoot is { } panel)
        {
            panel.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            contentWidth = (int)Math.Ceiling(panel.DesiredSize.Width * scale);
        }
        if (TitleText.Visibility == Visibility.Visible)
        {
            TitleText.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            contentWidth = Math.Max(contentWidth, (int)Math.Ceiling(TitleText.DesiredSize.Width * scale));
        }
        // 루트 Grid 좌우 패딩을 더해 콘텐츠가 잘리지 않게 한다(아이템 컨테이너 생성 전이면 0 → 초기값 폴백).
        contentWidth = contentWidth > 0 ? contentWidth + VerticalContentSidePadding : InitialPopupWidth;
        int finalWidth = Math.Min(contentWidth, maxWidth);

        // 확정 폭으로 높이를 측정한다(세로는 높이가 배치축이라 GridView 측정이 정확).
        root.Measure(new Windows.Foundation.Size(finalWidth / scale, double.PositiveInfinity));
        int desiredHeight = (int)Math.Ceiling(root.DesiredSize.Height * scale);
        if (desiredHeight <= 0)
            desiredHeight = InitialPopupHeight;

        // 작업영역 높이(상하 여백 확보)를 상한으로 클램프 — 초과분은 세로 스크롤로 처리한다.
        int maxHeight = Math.Max(InitialPopupHeight, _metrics.Work.Height - WorkAreaMargin);
        int finalHeight = Math.Min(desiredHeight, maxHeight);

        if (desiredHeight > maxHeight)
        {
            ScrollViewer.SetVerticalScrollMode(AppsGrid, ScrollMode.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(AppsGrid, ScrollBarVisibility.Auto);
            // 세로 스크롤바가 생기면 그 폭만큼 콘텐츠 폭에 더해 아이콘이 가려지지 않게 한다(상한 내 클램프).
            finalWidth = Math.Min(finalWidth + VerticalScrollBarWidth, maxWidth);
        }

        return (finalWidth, finalHeight);
    }

    private void OnAppClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PopupAppItem item)
        {
            _launcher.Launch(item.App);
            Close();
        }
        else if (e.ClickedItem is PopupAddButtonItem)
        {
            // 목록 끝 "+" 버튼: 우클릭 "그룹 수정"과 동일하게 메인 창 편집 다이얼로그를 연다.
            EditGroup();
        }
    }

    // 우클릭 메뉴 "열기": 일반 실행 후 팝업 닫기(좌클릭과 동일 동작).
    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PopupAppItem item)
        {
            _launcher.Launch(item.App);
            Close();
        }
    }

    // 우클릭 메뉴 "관리자 권한으로 실행": Win32만 승격 실행(Packaged 항목은 메뉴에서 비활성).
    private void OnRunAsAdminClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PopupAppItem item)
        {
            _launcher.LaunchAsAdmin(item.App);
            Close();
        }
    }

    // 우클릭 메뉴 "그룹 수정": EditGroup으로 위임(목록 끝 "+" 버튼과 동일 동작).
    private void OnEditGroupClick(object sender, RoutedEventArgs e) => EditGroup();

    // 작은 팝업 창은 편집 다이얼로그를 담을 수 없으므로, 메인 앱을 "--edit-group {id}"로 실행
    // (single-instance가 기존 메인 인스턴스로 합침)해 메인 창에서 편집한다. 실행 후 팝업을 닫는다.
    private void EditGroup()
    {
        try
        {
            // .lnk 타깃과 동일한 실행 별칭 풀패스로 호출(검증된 실행 경로).
            var info = new System.Diagnostics.ProcessStartInfo(WorkGroupPaths.AliasExePath)
            {
                Arguments = GroupArgs.BuildEditCommandLineArguments(_groupId),
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(info);
        }
        catch (Exception)
        {
            // 별칭 실행 실패(파일 없음/권한 등)는 사용자 개입 없이 무시한다(팝업은 곧 닫히고 로거 없음).
        }
        Close();
    }

    // 아이콘 위에 마우스가 올라오면 살짝 확대, 벗어나면 원래 크기로(부드럽게).
    private void OnIconPointerEntered(object sender, PointerRoutedEventArgs e) => AnimateIconScale(sender, 1.15);

    private void OnIconPointerExited(object sender, PointerRoutedEventArgs e) => AnimateIconScale(sender, 1.0);

    private static void AnimateIconScale(object sender, double to)
    {
        if (sender is not FrameworkElement { RenderTransform: ScaleTransform scale })
            return;

        var storyboard = new Storyboard();
        var duration = new Duration(TimeSpan.FromMilliseconds(120));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        foreach (var property in new[] { "ScaleX", "ScaleY" })
        {
            var animation = new DoubleAnimation { To = to, Duration = duration, EasingFunction = easing };
            Storyboard.SetTarget(animation, scale);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }
        storyboard.Begin();
    }

    private void ConfigurePresenter()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            // 메인 창과 동일한 표준 프레임(둥근 모서리/시스템 테두리)은 유지하고 타이틀바(캡션 버튼)만 숨긴다.
            presenter.SetBorderAndTitleBar(true, false);
        }

        // 팝업 창을 작업 표시줄 버튼·Alt+Tab 스위처에 노출하지 않는다(핀 클릭 시 별도 앱 아이콘/미리보기 방지).
        AppWindow.IsShownInSwitchers = false;
    }

    /// <summary>콘텐츠 측정이 끝난 뒤 작업 표시줄 정위치로 이동해 처음으로 화면에 표시한다(점프·깜빡임 방지).</summary>
    private void RevealAtTaskbar()
    {
        AdjustToContent(); // 화면 밖에서 최종 크기 확정
        int height = _lastAppliedHeight > 0 ? _lastAppliedHeight : InitialPopupHeight;
        MoveToTaskbar(height);
        _positioned = true; // 이후 SizeChanged는 정위치에서 조정
        // 콜드 프로세스는 OS가 새 창을 포그라운드로 띄우지만, 상주 인스턴스(redirect)에서 띄울 땐
        // Activate()만으론 포그라운드 포커스를 못 잡는다. SetForegroundWindow로 활성 상태를 확보해야
        // 다른 앱 클릭 시 Deactivated→Close가 동작한다(FolderListPopupWindow와 동일).
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>캡처해 둔 클릭 시점 좌표로 작업 표시줄 변에 창을 이동한다(크기는 AdjustToContent가 담당).</summary>
    private void MoveToTaskbar(int height)
    {
        var placement = TaskbarPopupPositioner.Compute(
            _metrics.Monitor, _metrics.Work, _metrics.CursorX, _metrics.CursorY, _popupWidth, height);
        AppWindow.Move(new PointInt32(placement.X, placement.Y));
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated)
            Close();
    }
}
