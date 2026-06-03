using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WorkGroup.App.Services;
using WorkGroup.Application.Folders;
using WorkGroup.Domain.Folders;
using WorkGroup.Infrastructure.Interop;

namespace WorkGroup.App.Views;

/// <summary>
/// 트레이 좌클릭 시 뜨는 등록 폴더 목록 팝업. 작업 표시줄 변에 배치, 항상 위, 포커스 손실 시 닫힘.
/// 폴더 호버 시 그 안의 파일/하위폴더를 2차 팝업(FolderContentsPopupWindow)으로 보여준다(T9 연결).
/// </summary>
public sealed partial class FolderListPopupWindow : Window
{
    private const int SingleColumnWidth = 360;
    private const int GridCellWidth = 96;
    private const int InitialPopupHeight = 120;
    private const int OffScreen = -32000;
    private const int HoverDelayMs = 200;

    private readonly IFolderShortcutRepository _repository;
    private readonly IShellOpener _shellOpener;
    private readonly FolderPopupSettingsService _settingsService;
    // 좌클릭 시점의 커서/모니터 좌표(표시를 미뤄도 클릭 위치 기준으로 배치).
    private readonly ScreenMetricsProvider.Metrics _metrics;

    private FolderPopupSettings _settings = FolderPopupSettings.Default;
    private int _popupWidth = SingleColumnWidth;
    private int _lastAppliedHeight = -1;
    private bool _positioned;

    private bool _isActive;
    // 창이 닫힌 뒤 큐에 남은 호버 타이머 Tick이 닫힌 창에 접근하는 것을 막는 가드.
    private bool _closed;
    private FolderContentsPopupWindow? _child;

    // 호버 타이머 + 현재 호버 중인 버튼/경로.
    private readonly DispatcherTimer _hoverTimer;
    private Button? _hoveredButton;
    private string? _hoveredPath;

    public FolderListPopupWindow()
    {
        InitializeComponent();

        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;

        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = App.Services.GetRequiredService<ThemeService>().Read();
            // 아이콘/항목 배치로 콘텐츠 크기가 바뀌면 창 높이를 다시 맞춘다.
            root.SizeChanged += (_, _) => AdjustToContent();
        }

        _repository = App.Services.GetRequiredService<IFolderShortcutRepository>();
        _shellOpener = App.Services.GetRequiredService<IShellOpener>();
        _settingsService = App.Services.GetRequiredService<FolderPopupSettingsService>();

        ConfigurePresenter();
        _metrics = new ScreenMetricsProvider().Capture();
        AppWindow.Resize(new SizeInt32(_popupWidth, InitialPopupHeight));
        AppWindow.Move(new PointInt32(OffScreen, OffScreen));
        Activated += OnActivated;
        Closed += OnClosed;

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HoverDelayMs) };
        _hoverTimer.Tick += OnHoverTick;

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _settings = _settingsService.Read();
            _popupWidth = _settings.ColumnCount <= 1
                ? SingleColumnWidth
                : _settings.ColumnCount * GridCellWidth + 24;

            var folders = await _repository.LoadAllAsync();
            BuildFolderUI(folders);
        }
        catch
        {
            ShowEmpty("폴더를 불러오지 못했습니다.");
        }
        finally
        {
            // 콘텐츠 확정 후 실제 높이로 측정하고 작업 표시줄 정위치로 이동해 표시한다.
            DispatcherQueue.TryEnqueue(RevealAtTaskbar);
        }
    }

    private void BuildFolderUI(IReadOnlyList<FolderShortcut> folders)
    {
        FolderPanel.Children.Clear();
        if (folders.Count == 0)
        {
            ShowEmpty("등록된 폴더가 없습니다.");
            return;
        }

        if (_settings.ColumnCount <= 1)
        {
            foreach (var f in folders)
                FolderPanel.Children.Add(CreateHorizontalButton(f));
        }
        else
        {
            int cols = _settings.ColumnCount;
            for (int i = 0; i < folders.Count; i += cols)
            {
                var rowPanel = new StackPanel { Orientation = Orientation.Horizontal };
                for (int c = 0; c < cols && i + c < folders.Count; c++)
                    rowPanel.Children.Add(CreateGridButton(folders[i + c]));
                FolderPanel.Children.Add(rowPanel);
            }
        }
    }

    private void ShowEmpty(string message)
    {
        FolderPanel.Children.Clear();
        FolderPanel.Children.Add(new TextBlock
        {
            Text = message,
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Center
        });
    }

    // 세로 목록: 아이콘(28) 왼쪽 + 이름 오른쪽.
    private Button CreateHorizontalButton(FolderShortcut folder)
    {
        var image = new Image { Width = 28, Height = 28, Stretch = Stretch.Uniform };
        _ = SetIconAsync(image, folder.Path);

        var name = new TextBlock
        {
            Text = folder.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        // 가로 StackPanel은 자식을 무한 너비로 측정해 TextTrimming이 동작하지 않으므로,
        // Grid(아이콘 Auto + 이름 *)로 이름 열 너비를 제약해 긴 이름이 말줄임(...)으로 표시되게 한다.
        var content = new Grid { ColumnSpacing = 10 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(image, 0);
        Grid.SetColumn(name, 1);
        content.Children.Add(image);
        content.Children.Add(name);

        return CreateFolderButton(folder, content, HorizontalAlignment.Stretch);
    }

    // 그리드: 아이콘(40) 위 + 이름 아래.
    private Button CreateGridButton(FolderShortcut folder)
    {
        var image = new Image { Width = 40, Height = 40, Stretch = Stretch.Uniform };
        _ = SetIconAsync(image, folder.Path);

        var name = new TextBlock
        {
            Text = folder.Name,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 12,
            MaxWidth = GridCellWidth - 12
        };

        var content = new StackPanel { Spacing = 4, Width = GridCellWidth - 8 };
        content.Children.Add(image);
        content.Children.Add(name);

        return CreateFolderButton(folder, content, HorizontalAlignment.Center);
    }

    private Button CreateFolderButton(FolderShortcut folder, UIElement content, HorizontalAlignment align)
    {
        var button = new Button
        {
            Content = content,
            Tag = folder.Path,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 8, 10, 8),
            HorizontalAlignment = align,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        button.Click += OnFolderClick;
        button.PointerEntered += OnFolderPointerEntered;
        button.PointerExited += OnFolderPointerExited;
        return button;
    }

    private static async Task SetIconAsync(Image image, string path)
    {
        var icon = await FolderIconLoader.LoadAsync(path);
        if (icon is not null)
            image.Source = icon;
    }

    private void OnFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
        {
            _shellOpener.Open(path);
            CloseSelf();
        }
    }

    private void OnFolderPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button { Tag: string path } button)
        {
            _hoveredButton = button;
            _hoveredPath = path;
            _hoverTimer.Stop();
            _hoverTimer.Start();
        }
    }

    private void OnFolderPointerExited(object sender, PointerRoutedEventArgs e) => _hoverTimer.Stop();

    private void OnHoverTick(object? sender, object e)
    {
        _hoverTimer.Stop();
        // 창이 이미 닫혔으면(큐에 남은 Tick) 닫힌 창 접근을 피한다.
        if (_closed)
            return;
        // 하위폴더 깊이가 2 이상일 때만 내용 팝업을 띄운다(깊이 1이면 폴더 클릭=탐색기 열기).
        if (_settings.SubfolderDepth >= 2 && _hoveredButton is not null && _hoveredPath is not null)
        {
            try
            {
                ShowChildPopup(_hoveredButton, _hoveredPath);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // 창이 외부에서 막 닫히는 라이프사이클 race로 닫힌 창에 접근한 경우 — 무시(안전망).
            }
        }
    }

    // 2차 내용 팝업 표시. 부모(이 창) 좌/우에 배치하고 포커스 가드 체인을 설정한다(B2).
    private void ShowChildPopup(Button button, string path)
    {
        // 창이 이미 닫혔으면 닫힌 창 접근(Content/AppWindow)을 피한다.
        if (_closed)
            return;

        _child?.CloseSelf();

        double scale = (Content as FrameworkElement)?.XamlRoot?.RasterizationScale ?? 1.0;
        int buttonY = 0;
        if (Content is FrameworkElement root)
        {
            var p = button.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, 0));
            buttonY = (int)(p.Y * scale);
        }

        var anchor = new ChildPopupAnchor(
            AppWindow.Position.X, AppWindow.Position.Y,
            AppWindow.Position.X + AppWindow.Size.Width, buttonY, _metrics.Work);

        var child = new FolderContentsPopupWindow(path, FolderDisplayName(path), 2, _settings, anchor);
        _child = child;
        child.CloseChainRequested += CloseSelf;      // 파일 실행 등으로 체인 닫힘 → 1차도 닫기
        child.Closed += (_, _) =>
        {
            // 이미 다른 자식으로 교체됐으면(기존 자식 Closed가 늦게 도착) 무시 — 새 _child 추적 보존(누적 방지).
            if (!ReferenceEquals(_child, child))
                return;
            _child = null;
            // 자식이 닫힌 뒤, 마우스가 체인(1차) 위로 복귀했으면 유지하고 밖으로 나갔으면 체인 종료.
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_closed)
                    return;
                if (_child is null && !IsPointerOverChain())
                    CloseSelf();
            });
        };
        child.Activate();
    }

    private static string FolderDisplayName(string path)
    {
        var name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        // 설정은 메인 창의 "트레이 메뉴" 탭에서 한다.
        // using WorkGroup.Application.* 와의 이름 충돌을 피하려 Application을 정규화한다.
        CloseSelf();
        ((App)Microsoft.UI.Xaml.Application.Current).ShowTrayMenuFromPopup();
    }

    /// <summary>콘텐츠의 실제 세로 길이를 측정해 창 높이를 맞춘다(작업영역 높이로 상한 클램프).</summary>
    private void AdjustToContent()
    {
        // 닫힌 뒤 SizeChanged/DispatcherQueue 콜백이 닫힌 창의 Content/AppWindow에 접근하는 것을 막는다.
        if (_closed || Content is not FrameworkElement root)
            return;

        double scale = root.XamlRoot?.RasterizationScale ?? 1.0;
        root.UpdateLayout();
        root.Measure(new Windows.Foundation.Size(_popupWidth / scale, double.PositiveInfinity));

        int contentHeight = (int)Math.Ceiling(root.DesiredSize.Height * scale);
        if (contentHeight <= 0)
            contentHeight = InitialPopupHeight;

        int chrome = AppWindow.Size.Height - AppWindow.ClientSize.Height;
        if (chrome < 0)
            chrome = 0;

        int total = contentHeight + chrome;
        // 폴더가 많아 화면을 넘으면 작업영역 높이로 제한(내부 ScrollViewer가 스크롤).
        if (total > _metrics.Work.Height)
            total = _metrics.Work.Height;

        if (total == _lastAppliedHeight)
            return;
        _lastAppliedHeight = total;

        AppWindow.Resize(new SizeInt32(_popupWidth, total));
        if (_positioned)
            MoveToTaskbar(total);
    }

    private void ConfigurePresenter()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(true, false);
        }
        AppWindow.IsShownInSwitchers = false;
    }

    private void RevealAtTaskbar()
    {
        // 표시 전에 창이 닫혔으면(빠른 호버/닫힘) 닫힌 창 접근을 피한다.
        if (_closed)
            return;
        AdjustToContent();
        int height = _lastAppliedHeight > 0 ? _lastAppliedHeight : InitialPopupHeight;
        MoveToTaskbar(height);
        _positioned = true;
    }

    private void MoveToTaskbar(int height)
    {
        var placement = TaskbarPopupPositioner.Compute(
            _metrics.Monitor, _metrics.Work, _metrics.CursorX, _metrics.CursorY, _popupWidth, height);
        AppWindow.Move(new PointInt32(placement.X, placement.Y));
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        _isActive = e.WindowActivationState != WindowActivationState.Deactivated;
        if (_isActive)
            return;
        // 포커스를 잃으면 한 틱 뒤 마우스가 체인(이 창+자식들) 밖인지 확인해 전체를 닫는다.
        // 다른 앱/작업표시줄/바탕화면 클릭 모두 Deactivated → 체인 밖이면 닫힘.
        // (자식으로 마우스/포커스가 이동한 경우는 IsPointerOverChain이 true라 유지)
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed || _isActive)
                return;
            if (IsPointerOverChain())
                return;
            CloseSelf();
        });
    }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        _closed = true;
        // 1차가 닫히면 살아있는 2차 팝업 체인도 함께 닫는다.
        _hoverTimer.Stop();
        _child?.CloseSelf();
        _child = null;
    }

    /// <summary>
    /// 자가 종료 경로 통일. Closed 이벤트는 큐에서 Tick보다 늦게 올 수 있으므로,
    /// Close() 호출 즉시(동기) _closed를 set해 이후 큐에 남은 호버 Tick이 닫힌 창에 접근하지 않게 한다.
    /// </summary>
    internal void CloseSelf()
    {
        if (_closed)
            return;
        _closed = true;
        _hoverTimer.Stop();
        Close();
    }

    /// <summary>마우스 커서가 이 팝업 창 영역 위에 있는지(닫혔으면 false). 포커스(_isActive)는 hover로 안 바뀌므로 좌표로 판정.</summary>
    private bool IsPointerOverWindow()
    {
        if (_closed || !GetCursorPos(out var pt))
            return false;
        var pos = AppWindow.Position;
        var size = AppWindow.Size;
        return pt.X >= pos.X && pt.X < pos.X + size.Width
            && pt.Y >= pos.Y && pt.Y < pos.Y + size.Height;
    }

    /// <summary>마우스가 이 팝업 또는 자식 체인의 어느 창 위에든 있으면 true.</summary>
    internal bool IsPointerOverChain() => IsPointerOverWindow() || (_child?.IsPointerOverChain() ?? false);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint lpPoint);
}
