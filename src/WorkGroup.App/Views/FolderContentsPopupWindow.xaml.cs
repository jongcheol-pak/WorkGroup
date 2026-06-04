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
/// 폴더 호버 시 그 안의 파일/하위폴더를 보여주는 2차(이상) 팝업. 부모 팝업 좌/우에 배치한다.
/// 부모-자식이 하나의 포커스 체인으로 동작한다(B2): 자식이 떠 있으면 부모가 닫히지 않고,
/// 파일 실행/최대 깊이 폴더 열기는 <see cref="CloseChainRequested"/>로 체인 전체를 닫는다.
/// </summary>
public sealed partial class FolderContentsPopupWindow : Window
{
    private const int PopupWidth = 400;
    private const int InitialPopupHeight = 120;
    private const int OffScreen = -32000;
    private const int HoverDelayMs = 200;
    // 자식 팝업이 부모 위로 포개지는 가로 겹침(클수록 더 많이 겹침).
    private const int Overlap = 34;
    private const int TopMargin = 100;

    private readonly IDirectoryBrowser _browser;
    private readonly IShellOpener _shellOpener;
    private readonly string _folderPath;
    private readonly int _depth;
    private readonly FolderPopupSettings _settings;
    private readonly ChildPopupAnchor _anchor;

    private int _lastAppliedHeight = -1;
    private bool _positioned;

    // 창이 닫힌 뒤 큐에 남은 호버 타이머 Tick이 닫힌 창에 접근하는 것을 막는 가드.
    private bool _closed;
    private FolderContentsPopupWindow? _child;

    private readonly DispatcherTimer _hoverTimer;
    private Button? _hoveredButton;
    private string? _hoveredPath;

    /// <summary>파일 실행/최대 깊이 폴더 열기 등으로 전체 팝업 체인을 닫아야 할 때 발생.</summary>
    public event Action? CloseChainRequested;

    public FolderContentsPopupWindow(
        string folderPath, string folderName, int depth, FolderPopupSettings settings, ChildPopupAnchor anchor)
    {
        InitializeComponent();

        _folderPath = folderPath;
        _depth = depth;
        _settings = settings;
        _anchor = anchor;

        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;

        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = App.Services.GetRequiredService<ThemeService>().Read();
            root.SizeChanged += (_, _) => AdjustToContent();
        }

        _browser = App.Services.GetRequiredService<IDirectoryBrowser>();
        _shellOpener = App.Services.GetRequiredService<IShellOpener>();

        ConfigurePresenter();
        AppWindow.Resize(new SizeInt32(PopupWidth, InitialPopupHeight));
        AppWindow.Move(new PointInt32(OffScreen, OffScreen));
        Activated += OnActivated;
        Closed += OnClosed;

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HoverDelayMs) };
        _hoverTimer.Tick += OnHoverTick;

        HeaderText.Text = folderName;
        LoadContents();
        DispatcherQueue.TryEnqueue(RevealAtAnchor);
    }

    private void LoadContents()
    {
        ContentPanel.Children.Clear();
        var listing = _browser.Browse(_folderPath, _settings.ShowHiddenItems);

        switch (listing.Status)
        {
            case DirectoryBrowseStatus.NotFound:
                ShowMessage(LocalizationService.Current?.Get("FolderContents_NotFound") ?? string.Empty);
                return;
            case DirectoryBrowseStatus.AccessDenied:
                ShowMessage(LocalizationService.Current?.Get("FolderContents_AccessDenied") ?? string.Empty);
                return;
            case DirectoryBrowseStatus.Empty:
                ShowMessage(LocalizationService.Current?.Get("FolderContents_Empty") ?? string.Empty);
                return;
        }

        // 파일 먼저, 그다음 폴더(각각 이름순 — DirectoryBrowser가 정렬).
        foreach (var file in listing.Files)
            ContentPanel.Children.Add(CreateEntryButton(file));
        foreach (var folder in listing.Folders)
            ContentPanel.Children.Add(CreateEntryButton(folder));
    }

    private void ShowMessage(string message)
    {
        ContentPanel.Children.Clear();
        ContentPanel.Children.Add(new TextBlock { Text = message, Margin = new Thickness(8) });
    }

    private Button CreateEntryButton(DirectoryEntryInfo entry)
    {
        var image = new Image { Width = 20, Height = 20, Stretch = Stretch.Uniform };
        _ = SetIconAsync(image, entry.FullPath);

        var name = new TextBlock
        {
            Text = entry.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        // 가로 StackPanel은 자식을 무한 너비로 측정해 TextTrimming이 동작하지 않으므로,
        // Grid(아이콘 Auto + 이름 *)로 이름 열 너비를 제약해 긴 이름이 말줄임(...)으로 표시되게 한다.
        var content = new Grid { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(image, 0);
        Grid.SetColumn(name, 1);
        content.Children.Add(image);
        content.Children.Add(name);

        var button = new Button
        {
            Content = content,
            Tag = entry,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 6, 8, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        button.Click += OnEntryClick;
        button.PointerEntered += OnEntryPointerEntered;
        button.PointerExited += OnEntryPointerExited;
        return button;
    }

    private static async Task SetIconAsync(Image image, string path)
    {
        var icon = await FolderIconLoader.LoadAsync(path);
        if (icon is not null)
            image.Source = icon;
    }

    private void OnEntryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DirectoryEntryInfo entry })
            return;

        _hoverTimer.Stop();

        // 파일/폴더 모두 클릭하면 셸로 열고 전체 체인을 닫는다(하위 폴더 탐색은 호버로).
        _shellOpener.Open(entry.FullPath);
        RequestCloseChain();
    }

    private void OnEntryPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button { Tag: DirectoryEntryInfo { IsDirectory: true } entry } button)
        {
            _hoveredButton = button;
            _hoveredPath = entry.FullPath;
            _hoverTimer.Stop();
            _hoverTimer.Start();
        }
    }

    private void OnEntryPointerExited(object sender, PointerRoutedEventArgs e) => _hoverTimer.Stop();

    private void OnHoverTick(object? sender, object e)
    {
        _hoverTimer.Stop();
        // 창이 이미 닫혔으면(큐에 남은 Tick) 닫힌 창 접근을 피한다.
        if (_closed)
            return;
        if (_depth < _settings.SubfolderDepth && _hoveredPath is not null)
        {
            try
            {
                ShowChildPopup(_hoveredPath);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // 창이 외부에서 막 닫히는 라이프사이클 race로 닫힌 창에 접근한 경우 — 무시(안전망).
            }
        }
    }

    private void ShowChildPopup(string folderPath)
    {
        // 창이 이미 닫혔으면 닫힌 창 접근(Content/AppWindow)을 피한다.
        if (_closed)
            return;

        _child?.CloseSelf();

        var child = new FolderContentsPopupWindow(
            folderPath, FolderDisplayName(folderPath), _depth + 1, _settings, BuildChildAnchor());
        _child = child;
        child.CloseChainRequested += RequestCloseChain;
        // 손자가 닫히면 추적만 해제(교체로 늦게 온 Closed는 무시). 닫기 판정은 1차 포커스가 담당.
        child.Closed += (_, _) =>
        {
            if (ReferenceEquals(_child, child))
                _child = null;
        };
        // 자식은 RevealAtAnchor에서 Show(false)로 표시(포커스 안 뺏음) → Activate 호출하지 않는다.
    }

    // 호버 중인 버튼의 화면 위치를 기준으로 자식 팝업 배치 앵커를 만든다.
    private ChildPopupAnchor BuildChildAnchor()
    {
        double scale = (Content as FrameworkElement)?.XamlRoot?.RasterizationScale ?? 1.0;
        int buttonY = 0;
        if (_hoveredButton is not null && Content is FrameworkElement root)
        {
            var p = _hoveredButton.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, 0));
            buttonY = (int)(p.Y * scale);
        }

        return new ChildPopupAnchor(
            AppWindow.Position.X, AppWindow.Position.Y,
            AppWindow.Position.X + AppWindow.Size.Width, buttonY, _anchor.Work);
    }

    private void RequestCloseChain()
    {
        CloseChainRequested?.Invoke(); // 상위로 전파
        CloseSelf();
    }

    private void AdjustToContent()
    {
        // 닫힌 뒤 SizeChanged/DispatcherQueue 콜백이 닫힌 창의 Content/AppWindow에 접근하는 것을 막는다.
        if (_closed || Content is not FrameworkElement root)
            return;

        double scale = root.XamlRoot?.RasterizationScale ?? 1.0;
        root.UpdateLayout();
        root.Measure(new Windows.Foundation.Size(PopupWidth / scale, double.PositiveInfinity));

        int contentHeight = (int)Math.Ceiling(root.DesiredSize.Height * scale);
        if (contentHeight <= 0)
            contentHeight = InitialPopupHeight;

        int chrome = AppWindow.Size.Height - AppWindow.ClientSize.Height;
        if (chrome < 0)
            chrome = 0;

        int total = contentHeight + chrome;
        // 항목이 많아 화면을 넘으면 작업영역 높이로 제한(내부 ScrollViewer가 스크롤).
        if (total > _anchor.Work.Height)
            total = _anchor.Work.Height;

        if (total == _lastAppliedHeight)
            return;
        _lastAppliedHeight = total;

        AppWindow.Resize(new SizeInt32(PopupWidth, total));
        if (_positioned)
            PlaceAtAnchor(total);
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

    private void RevealAtAnchor()
    {
        // 표시 전에 창이 닫혔으면(빠른 호버/닫힘) 닫힌 창 접근을 피한다.
        if (_closed)
            return;
        AdjustToContent();
        int height = _lastAppliedHeight > 0 ? _lastAppliedHeight : InitialPopupHeight;
        PlaceAtAnchor(height);
        _positioned = true;
        // 포커스를 뺏지 않고 표시한다(AppGroup의 SWP_NOACTIVATE와 동일). 1차 팝업이 활성 상태를 유지해야
        // 자식 표시·호버에도 1차가 Deactivated되지 않고, 다른 앱 클릭 시에만 1차 Deactivated→전체 닫힘.
        AppWindow.Show(activateWindow: false);
    }

    // 부모 팝업 왼쪽(공간 없으면 오른쪽)에, 호버 버튼 높이 기준으로 배치한다(작업영역 안으로 클램프).
    private void PlaceAtAnchor(int height)
    {
        var work = _anchor.Work;
        int x = _anchor.ParentLeft - PopupWidth + Overlap;
        // 호버한 항목 높이에 맞춰 배치한다(선택한 폴더 위치에 하위 팝업 표시).
        // ButtonY는 부모 클라이언트 기준 항목 Y 오프셋 → 부모 창 상단(ParentTop)을 더해 화면 절대 Y로 변환.
        int y = _anchor.ParentTop + _anchor.ButtonY;

        if (x < work.Left)
            x = _anchor.ParentRight - Overlap;
        if (x + PopupWidth > work.Right)
            x = work.Right - PopupWidth;
        if (y + height > work.Bottom)
            y = work.Bottom - height;
        if (y < work.Top + TopMargin)
            y = work.Top + TopMargin;

        AppWindow.Move(new PointInt32(x, y));
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        // 자식은 Show(false)로 떠 포커스를 거의 받지 않는다. 클릭 등으로 활성화됐다 잃으면 닫는 안전망.
        if (e.WindowActivationState == WindowActivationState.Deactivated)
            CloseSelf();
    }

    private void OnClosed(object sender, WindowEventArgs e)
    {
        _closed = true;
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

    // 폴더 경로에서 표시용 이름을 얻는다(루트 등 이름이 비면 경로 자체).
    private static string FolderDisplayName(string path)
    {
        var name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? path : name;
    }
}

/// <summary>자식 내용 팝업의 배치 기준(부모 창 사각형 + 호버 버튼 Y + 작업영역).</summary>
public readonly record struct ChildPopupAnchor(int ParentLeft, int ParentTop, int ParentRight, int ButtonY, ScreenRect Work);
