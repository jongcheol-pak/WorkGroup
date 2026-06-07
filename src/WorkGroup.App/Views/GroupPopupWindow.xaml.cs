using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
    // 세로 1열 아이템 영역 폭(px). 아이콘 박스는 48px(ItemContainerStyle로 컨테이너 여백 0)지만, 아이콘에 딱
    // 붙으면 답답하므로 가로 모드처럼 좌우에 여백을 두도록 키운 값(80 → 아이콘 48 + 좌우 16씩, 측정 신뢰 불가해 고정).
    private const int VerticalIconColumnWidth = 80;
    // 세로 1열 콘텐츠 폭 좌우에 더하는 루트 Grid 가로 패딩 합(Padding="12,12,12,0" → 좌 12 + 우 12).
    private const int VerticalContentSidePadding = 24;
    // 세로 스크롤바가 생길 때 콘텐츠 폭에 더해 잘림을 막는 근사 스크롤바 폭(px).
    private const int VerticalScrollBarWidth = 16;

    // 공유 초기화(InitializeChrome)에서 DI로 1회 해석하므로 readonly 불가. null! 초기화로 정의 할당 경고를 막는다.
    private IGroupAppService _groupService = null!;
    private IAppLauncher _launcher = null!;
    // 핀 클릭 시점의 커서/모니터 좌표(표시 시점에 재캡처 — 재사용 창은 클릭마다 갱신).
    private ScreenMetricsProvider.Metrics _metrics;
    // "그룹 수정" 메뉴에서 메인 앱에 편집 활성화 인자를 넘길 때 사용하는 그룹 식별자(표시마다 갱신).
    private string _groupId = string.Empty;

    // 상주(웜) 경로의 재사용 창 여부. true면 Deactivated 시 Close 대신 Hide로 살려 둔다.
    private bool _reusable;
    // AppWindow.Hide()가 유발하는 Deactivated 재진입(→ HidePopup 재귀)을 차단하는 가드.
    private bool _hiding;
    // 표시 경합 가드: ShowForGroup마다 증가. 늦게 끝난 이전 LoadAsync/Reveal이 최신 표시를 덮어쓰지 않게 한다.
    private int _showToken;

    // 현재 적용된 창 너비(px). 아이콘 1행 콘텐츠 너비에 맞춰 동적으로 결정한다(폭 상한까지).
    private int _popupWidth = InitialPopupWidth;
    // 마지막으로 적용한 창 너비/높이(px). 동일 값이면 재조정을 건너뛰어 SizeChanged 무한 루프를 막는다.
    private int _lastAppliedWidth = -1;
    private int _lastAppliedHeight = -1;
    // 콘텐츠 확정 후 작업 표시줄 정위치로 한 번 이동해 표시했는지. 그 전까지는 화면 밖에 머문다.
    private bool _positioned;
    // 작업 표시줄이 좌/우 변에 붙었는지. 참이면 아이콘을 세로 1열로 배치하고 측정도 세로 기준으로 한다(표시마다 재판정).
    private bool _isVertical;
    // 작업 표시줄 변(진입 애니메이션 방향 결정용). 표시마다 ApplyEdgeLayout에서 갱신.
    private TaskbarEdge _edge;

    // 콘텐츠 페이드인(Opacity 0→1) Storyboard(빠른 재표시 시 중지용).
    private Storyboard? _entranceStoryboard;
    // 창 위치 라이즈(작업 표시줄 쪽 → 정위치) 프레임 애니메이션 상태.
    private bool _windowAnimating;
    private Stopwatch? _riseClock;
    private PointInt32 _riseFrom, _riseTo;

    // 앱 항목(PopupAppItem)과 마지막 "+" 추가버튼 항목(PopupAddButtonItem)이 섞이므로 object 컬렉션.
    public ObservableCollection<object> Items { get; } = new();

    /// <summary>콜드(프로세스 단명) 경로 생성자: 창 1회 생성 후 표시, Deactivated 시 Close→Exit(기존 동작 불변).</summary>
    public GroupPopupWindow(string groupId)
    {
        InitializeChrome();
        _reusable = false;
        _groupId = groupId;

        ApplyEdgeLayout();
        if (_isVertical)
            // 세로 1열 팝업은 콘텐츠 폭이 OS 기본 최소 창 너비(약 198px)보다 작아 그대로면 못 줄어든다.
            // WM_GETMINMAXINFO를 가로채 최소 추적 크기를 낮춘다(콜드는 방향이 고정이라 세로일 때만 설치).
            RemoveMinimumTrackSize();

        // 측정이 끝날 때까지 화면 밖에 둔다 → 초기 리사이즈/깜빡임이 사용자에게 보이지 않음.
        AppWindow.Resize(new SizeInt32(InitialPopupWidth, InitialPopupHeight));
        AppWindow.Move(new PointInt32(OffScreen, OffScreen));

        _ = LoadAsync(groupId, _showToken);
    }

    /// <summary>상주(웜) 경로 재사용 창 생성자: 1회 생성 후 ShowForGroup으로 반복 표시한다(닫지 않고 Hide).</summary>
    public GroupPopupWindow()
    {
        InitializeChrome();
        _reusable = true;
        // 재사용 창은 방향이 표시마다 바뀔 수 있으므로 최소 추적 크기 가드를 항상 설치한다(1px 최소는 가로에도 무해).
        RemoveMinimumTrackSize();
    }

    // 창 1회 초기화(재질/테마/서비스/프레젠터/Activated 구독) — 두 생성자가 공유한다.
    private void InitializeChrome()
    {
        InitializeComponent();

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
        Activated += OnActivated;
        // 라이즈 프레임 루프(CompositionTarget.Rendering)는 정적 이벤트라, 콜드 Close·종료 Close 등
        // 모든 닫힘 경로에서 구독을 풀어야 닫히는 창에 유령 Move가 가지 않는다.
        Closed += (_, _) => StopWindowRise();
    }

    // 클릭 시점 좌표를 캡처하고 작업 표시줄 변에 맞춰 세로/가로 배치(ItemsPanel·하단 패딩)를 적용한다.
    // 콜드 생성자와 재사용 ShowForGroup이 공유 — 표시마다 변이 바뀔 수 있어 매번 재적용한다.
    private void ApplyEdgeLayout()
    {
        _metrics = new ScreenMetricsProvider().Capture();

        _edge = TaskbarPopupPositioner.DetectEdge(_metrics.Monitor, _metrics.Work);
        _isVertical = _edge is TaskbarEdge.Left or TaskbarEdge.Right;

        if (Content is FrameworkElement contentRoot)
            AppsGrid.ItemsPanel = (ItemsPanelTemplate)contentRoot.Resources[
                _isVertical ? "VerticalItemsPanel" : "HorizontalItemsPanel"];

        // 세로 1열은 마지막 아이템이 하단에 붙지 않도록 하단 패딩을 좌우와 같게(12) 준다. 가로는 XAML 기본(하단 0).
        if (Content is Grid rootGrid)
            rootGrid.Padding = new Thickness(12, 12, 12, _isVertical ? 12 : 0);
    }

    /// <summary>재사용 창을 지정 그룹으로 다시 채워 표시한다(상주 경로 전용). 상태를 리셋하고 오프스크린에서 재측정·재배치한다.</summary>
    internal void ShowForGroup(string groupId)
    {
        _groupId = groupId;
        StopWindowRise(); // 진행 중 라이즈가 있으면 새 표시 전에 정리.

        // 리셋·재배치 전에 먼저 화면 밖으로 치운다. 이미 떠 있는 창을 재표시할 때 온스크린에서
        // Items.Clear()·ItemsPanel 교체가 보이는 깜빡임을 막는다(측정 후 RevealAtTaskbar가 정위치로 이동).
        AppWindow.Resize(new SizeInt32(InitialPopupWidth, InitialPopupHeight));
        AppWindow.Move(new PointInt32(OffScreen, OffScreen));

        ApplyEdgeLayout();

        // 이전 표시 상태/측정 캐시 리셋(잔상·잘못된 크기 방지). 스크롤 모드는 측정(AdjustToContent) 단계가
        // 다시 설정하므로 여기서 따로 끄지 않는다(중복 제거).
        _positioned = false;
        _lastAppliedWidth = -1;
        _lastAppliedHeight = -1;
        Items.Clear();
        TitleText.Text = string.Empty;
        TitleText.Visibility = Visibility.Collapsed;
        if (Content is FrameworkElement root)
            root.RequestedTheme = App.Services.GetRequiredService<WorkGroup.App.Services.ThemeService>().Read();

        // 화면 밖에서 표시한 뒤(측정 후 정위치 이동), 토큰을 올려 늦은 이전 로드를 무효화한다.
        AppWindow.Show();
        Activate();

        _ = LoadAsync(groupId, ++_showToken);
    }

    private async Task LoadAsync(string groupId, int token)
    {
        try
        {
            var groups = await _groupService.GetAllAsync();
            // 로드 중 더 최근 표시가 시작됐으면(재사용 창 빠른 재클릭) 이 결과로 목록을 덮어쓰지 않는다.
            if (token != _showToken) return;

            var group = groups.FirstOrDefault(g => g.Id.Value == groupId);
            if (group is null)
            {
                TitleText.Visibility = Visibility.Visible; // 에러 메시지는 헤더 설정과 무관하게 항상 표시
                TitleText.Text = LocalizationService.Current?.Get("GroupPopup_NotFound") ?? string.Empty;
                return;
            }

            // 그룹 설정에 따라 이름 헤더 표시/숨김. 단 세로 1열(좌/우 작업 표시줄)에서는 헤더를 항상 숨긴다
            // — 긴 그룹 이름이 세로 팝업 너비를 아이콘 폭 이상으로 넓혀 어색해지므로(숨김 시 텍스트 설정 불필요).
            bool showHeader = group.ShowPopupHeader && !_isVertical;
            TitleText.Visibility = showHeader ? Visibility.Visible : Visibility.Collapsed;
            if (showHeader)
                TitleText.Text = group.Apps.Count == 0
                    ? LocalizationService.Current?.Get("GroupPopup_MemberlessFormat", group.Name) ?? group.Name
                    : group.Name;

            foreach (var app in group.Apps)
            {
                var item = new PopupAppItem(app);
                item.EvaluateAvailability(); // 실행 파일 누락 멤버는 비활성·흐림 처리(컨테이너 실현 전 확정).
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
            DispatcherQueue.TryEnqueue(() => RevealAtTaskbar(token));
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
        // 여기는 크기가 실제로 바뀐 경우에만 도달한다(위 dedup early-return). 라이즈 진행 중 크기가 바뀌면
        // 옛 _riseTo로 수렴하지 않도록 라이즈를 중단하고 새 크기 기준 정위치로 스냅한다(M2).
        if (_positioned)
        {
            if (_windowAnimating)
                StopWindowRise();
            MoveToTaskbar(total);
        }
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

        // GridView measure는 주어진 가용 폭을 그대로 DesiredSize로 반환하고(HorizontalAlignment=Left는 arrange만
        // 바꿔 창 크기엔 영향 없음, 내부 패널을 직접 measure해도 arrange된 가용 폭이 남음) 콘텐츠 폭을 신뢰할 수 없다.
        // 세로 1열 아이템은 모두 48px 고정 박스라 폭이 일정하므로 아이템 폭은 고정값으로 잡고,
        // 가변인 헤더(그룹 이름)만 측정해 둘 중 큰 값을 콘텐츠 폭으로 한다(헤더는 TextBlock이라 measure 정확).
        int contentWidth = VerticalIconColumnWidth;
        // 정상 그룹의 이름 헤더는 세로에서 숨기므로 보통 측정 대상이 아니지만, 에러 메시지(NotFound 등)는
        // 세로에서도 TitleText에 표시되므로 Visible일 때만 그 폭을 반영한다.
        if (TitleText.Visibility == Visibility.Visible)
        {
            TitleText.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            contentWidth = Math.Max(contentWidth, (int)Math.Ceiling(TitleText.DesiredSize.Width * scale));
        }
        // 루트 Grid 좌우 패딩을 더해 콘텐츠가 잘리지 않게 한다.
        contentWidth += VerticalContentSidePadding;
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

    // 누락(파일 삭제) 멤버는 컨테이너를 비활성화해 좌클릭(ItemClick)·우클릭(ContextFlyout)을 막는다(흐림은 템플릿 Opacity가 담당).
    private void OnAppsContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        if (args.Item is PopupAppItem item && args.ItemContainer is not null)
            args.ItemContainer.IsEnabled = item.IsAvailable;
    }

    private void OnAppClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PopupAppItem item)
        {
            if (!item.IsAvailable) return; // 누락 멤버는 실행하지 않는다. 컨테이너 IsEnabled=false가 클릭을 막지만, ContainerContentChanging 미발화 등으로 비활성이 적용 안 된 경우의 fallback.
            _launcher.Launch(item.App);
            DismissPopup();
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
            if (!item.IsAvailable) return; // 누락 멤버는 실행하지 않는다. 컨테이너 IsEnabled=false가 클릭을 막지만, ContainerContentChanging 미발화 등으로 비활성이 적용 안 된 경우의 fallback.
            _launcher.Launch(item.App);
            DismissPopup();
        }
    }

    // 우클릭 메뉴 "관리자 권한으로 실행": Win32만 승격 실행(Packaged 항목은 메뉴에서 비활성).
    private void OnRunAsAdminClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PopupAppItem item)
        {
            if (!item.IsAvailable) return; // 누락 멤버는 실행하지 않는다. 컨테이너 IsEnabled=false가 클릭을 막지만, ContainerContentChanging 미발화 등으로 비활성이 적용 안 된 경우의 fallback.
            _launcher.LaunchAsAdmin(item.App);
            DismissPopup();
        }
    }

    // 우클릭 메뉴 "그룹 수정": EditGroup으로 위임(목록 끝 "+" 버튼과 동일 동작).
    private void OnEditGroupClick(object sender, RoutedEventArgs e) => EditGroup();

    // 작은 팝업 창은 편집 다이얼로그를 담을 수 없으므로, 메인 앱을 "--edit-group {id}"로 실행
    // (single-instance가 기존 메인 인스턴스로 합침)해 메인 창에서 편집한다. 실행 후 팝업을 정리한다(재사용=Hide/콜드=Close).
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
            // 별칭 실행 실패(파일 없음/권한 등)는 사용자 개입 없이 무시한다(팝업은 곧 정리되고 로거 없음).
        }
        DismissPopup();
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
    private void RevealAtTaskbar(int token)
    {
        // 더 최근 표시가 시작됐으면(재사용 창 빠른 재클릭) 이 reveal은 stale이므로 건너뛴다.
        if (token != _showToken)
            return;

        AdjustToContent(); // 화면 밖에서 최종 크기 확정(_popupWidth/_lastAppliedHeight)
        int height = _lastAppliedHeight > 0 ? _lastAppliedHeight : InitialPopupHeight;

        // 최종 위치를 계산해, 작업 표시줄 쪽으로 오프셋한 시작 위치에 둔 뒤 창 자체를 정위치로 떠오르게 한다.
        var placement = TaskbarPopupPositioner.Compute(
            _metrics.Monitor, _metrics.Work, _metrics.CursorX, _metrics.CursorY, _popupWidth, height);
        _riseTo = new PointInt32(placement.X, placement.Y);
        _riseFrom = StartOffset(_riseTo);
        AppWindow.Move(_riseFrom);

        _positioned = true; // 이후 SizeChanged는 정위치에서 조정
        // 콜드 프로세스는 OS가 새 창을 포그라운드로 띄우지만, 상주 인스턴스(redirect)에서 띄울 땐
        // Activate()만으론 포그라운드 포커스를 못 잡는다. SetForegroundWindow로 활성 상태를 확보해야
        // 다른 앱 클릭 시 Deactivated→Close가 동작한다(FolderListPopupWindow와 동일).
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));

        StartWindowRise();  // 창 위치를 시작 오프셋 → 정위치로 ease-out 이동
        PlayContentFade();  // 콘텐츠 페이드인(Opacity 0→1)
    }

    // 최종 위치에서 작업 표시줄 쪽으로 28px 오프셋한 시작 위치를 구한다(변별 방향).
    private PointInt32 StartOffset(PointInt32 to)
    {
        const int offset = 28;
        return _edge switch
        {
            TaskbarEdge.Bottom => new PointInt32(to.X, to.Y + offset),   // 아래(표시줄)에서 위로
            TaskbarEdge.Top => new PointInt32(to.X, to.Y - offset),      // 위(표시줄)에서 아래로
            TaskbarEdge.Left => new PointInt32(to.X - offset, to.Y),     // 왼쪽(표시줄)에서 오른쪽으로
            TaskbarEdge.Right => new PointInt32(to.X + offset, to.Y),    // 오른쪽(표시줄)에서 왼쪽으로
            _ => new PointInt32(to.X, to.Y + offset)
        };
    }

    // 창 위치를 _riseFrom → _riseTo로 프레임마다 ease-out 보간 이동한다(Mica 창 자체가 떠오름).
    // 주의: Mica 창을 매 렌더 프레임 AppWindow.Move로 옮기면 DWM 합성 주기와 어긋나 끊겨 보일 수 있다
    // (plan.md T1 — 부드러움은 GUI 수동 검증 대상. 미흡 시 DispatcherTimer 폴백 또는 콘텐츠 애니메이션 롤백).
    private void StartWindowRise()
    {
        StopWindowRise();
        _riseClock = Stopwatch.StartNew();
        _windowAnimating = true;
        CompositionTarget.Rendering += OnRiseFrame;
    }

    private void OnRiseFrame(object? sender, object e)
    {
        double t = (_riseClock?.ElapsedMilliseconds ?? 180) / 180.0;
        if (t >= 1)
        {
            AppWindow.Move(_riseTo); // 정위치 안착
            StopWindowRise();
            return;
        }
        double k = 1 - Math.Pow(1 - t, 3); // EaseOutCubic
        AppWindow.Move(new PointInt32(Lerp(_riseFrom.X, _riseTo.X, k), Lerp(_riseFrom.Y, _riseTo.Y, k)));
    }

    private void StopWindowRise()
    {
        if (!_windowAnimating)
            return;
        CompositionTarget.Rendering -= OnRiseFrame; // 정적 이벤트라 모든 종료 경로에서 명시적 해제(유령 Move 방지).
        _windowAnimating = false;
        _riseClock = null;
    }

    private static int Lerp(int a, int b, double k) => (int)(a + (b - a) * k);

    // 콘텐츠(루트 Grid)를 페이드인한다(Opacity 0→1, ease-out 180ms). 창 라이즈와 동시 진행.
    private void PlayContentFade()
    {
        if (Content is not Grid root)
            return;

        // 시작값 0을 base로 먼저 설정한 뒤 직전 Storyboard를 Stop한다(Stop이 base로 되돌려도 시작값 고착 없음).
        root.Opacity = 0;
        _entranceStoryboard?.Stop();

        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(fade, root);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(fade);
        _entranceStoryboard = storyboard;
        storyboard.Begin();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    // WM_GETMINMAXINFO 구독 델리게이트를 필드로 보관해 GC 수집을 막는다(네이티브가 콜백을 계속 호출).
    private SUBCLASSPROC? _minSizeSubclass;
    private const uint WM_GETMINMAXINFO = 0x0024;

    /// <summary>창에 서브클래스를 걸어 OS가 강제하는 최소 추적 크기를 1px로 낮춘다(세로 1열 팝업이 콘텐츠 폭대로 줄도록).</summary>
    private void RemoveMinimumTrackSize()
    {
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _minSizeSubclass = MinSizeSubclassProc;
        SetWindowSubclass(hwnd, _minSizeSubclass, IntPtr.Zero, IntPtr.Zero);
    }

    private IntPtr MinSizeSubclassProc(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        // 세로 1열일 때만 OS 최소 추적 크기를 1px로 낮춘다. 재사용 창은 서브클래스를 항상 설치하지만
        // 가로 배치에서는 OS 기본 최소 폭을 유지해야 콜드 가로 팝업과 너비가 일치한다(설치 시점이 아닌 현재 방향으로 판단).
        if (uMsg == WM_GETMINMAXINFO && lParam != IntPtr.Zero && _isVertical)
        {
            var info = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);
            info.ptMinTrackSize.X = 1;
            info.ptMinTrackSize.Y = 1;
            System.Runtime.InteropServices.Marshal.StructureToPtr(info, lParam, false);
            return IntPtr.Zero;
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
    }

    private delegate IntPtr SUBCLASSPROC(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

    [System.Runtime.InteropServices.DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

    [System.Runtime.InteropServices.DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    /// <summary>캡처해 둔 클릭 시점 좌표로 작업 표시줄 변에 창을 이동한다(크기는 AdjustToContent가 담당).</summary>
    private void MoveToTaskbar(int height)
    {
        var placement = TaskbarPopupPositioner.Compute(
            _metrics.Monitor, _metrics.Work, _metrics.CursorX, _metrics.CursorY, _popupWidth, height);
        AppWindow.Move(new PointInt32(placement.X, placement.Y));
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState != WindowActivationState.Deactivated)
            return;
        // Hide()가 유발한 Deactivated가 다시 들어와 HidePopup이 재귀하는 것을 막는다(c000027b 재진입 영역).
        if (_hiding)
            return;

        DismissPopup();
    }

    // 표시를 마친다: 재사용 창은 숨겨 다음 표시를 위해 살려 두고(닫지 않음), 콜드 창은 기존대로 Close→Exit.
    // 앱 실행/그룹 수정 클릭과 Deactivated 모두 이 경로로 통일한다(재사용 창을 Close로 파괴하지 않도록).
    private void DismissPopup()
    {
        if (_reusable)
            HidePopup();
        else
            Close();
    }

    // 재사용 창을 화면에서 숨긴다(닫지 않음). Hide()가 Deactivated를 재발화시킬 수 있어 _hiding으로 재진입을 차단한다.
    private void HidePopup()
    {
        if (_hiding)
            return;
        _hiding = true;
        StopWindowRise();            // 숨기는 창에 라이즈 프레임 Move가 계속 가지 않도록 중단.
        _entranceStoryboard?.Stop(); // 숨긴 창에서 페이드 Storyboard 콜백이 계속 발화하지 않도록 정리.
        try
        {
            AppWindow.Hide();
        }
        finally
        {
            _hiding = false;
        }
    }
}
