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

    // 2차 내용 팝업이 떠 있는 동안 1차가 Deactivated로 닫히지 않게 막는 가드(B2).
    private bool _childOpen;
    private bool _isActive;
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

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
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
            Close();
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
        // 하위폴더 깊이가 2 이상일 때만 내용 팝업을 띄운다(깊이 1이면 폴더 클릭=탐색기 열기).
        if (_settings.SubfolderDepth >= 2 && _hoveredButton is not null && _hoveredPath is not null)
            ShowChildPopup(_hoveredButton, _hoveredPath);
    }

    // 2차 내용 팝업 표시. 부모(이 창) 좌/우에 배치하고 포커스 가드 체인을 설정한다(B2).
    private void ShowChildPopup(Button button, string path)
    {
        _child?.Close();
        _childOpen = true;

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
        child.CloseChainRequested += Close;          // 파일 실행 등으로 체인 닫힘 → 1차도 닫기
        child.Closed += (_, _) =>
        {
            _childOpen = false;
            _child = null;
            // 자식이 닫혔는데 1차도 포커스가 없으면 체인 종료.
            if (!_isActive)
                Close();
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
        Close();
        ((App)Microsoft.UI.Xaml.Application.Current).ShowTrayMenuFromPopup();
    }

    /// <summary>콘텐츠의 실제 세로 길이를 측정해 창 높이를 맞춘다(작업영역 높이로 상한 클램프).</summary>
    private void AdjustToContent()
    {
        if (Content is not FrameworkElement root)
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
        // 2차 내용 팝업이 떠 있으면 닫지 않는다(B2 포커스 가드).
        if (_childOpen)
            return;
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        // 1차가 닫히면 살아있는 2차 팝업 체인도 함께 닫는다.
        _hoverTimer.Stop();
        _child?.Close();
        _child = null;
    }
}
