using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WorkGroup.App.ViewModels;
using WorkGroup.Domain.Groups;

namespace WorkGroup.App.Views;

/// <summary>
/// 그룹 추가/수정 다이얼로그(plan.md T4). 상단 아이콘+이름, 앱 추가/삭제 목록, 확인 시 검증·저장.
/// 아이콘/앱 선택 팝업은 Flyout(ContentDialog 중첩 불가). 호출자는 XamlRoot 지정 + Configure 후 ShowAsync.
/// </summary>
public sealed partial class GroupEditDialog : ContentDialog
{
    private AppGroup? _group;

    public GroupEditViewModel ViewModel { get; }

    public GroupEditDialog()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<GroupEditViewModel>();
        Opened += OnOpened;
    }

    /// <summary>신규(null) 또는 편집할 그룹을 지정한다(ShowAsync 전에 호출).</summary>
    public void Configure(AppGroup? group) => _group = group;

    private async void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        => await ViewModel.InitializeAsync(_group);

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // 검증·저장이 끝날 때까지 닫힘을 보류한다. 실패(빈 목록/중복 이름/저장 실패) 시 닫지 않는다.
        var deferral = args.GetDeferral();
        try
        {
            if (!await ViewModel.ValidateAndSaveAsync())
                args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    // 아이콘 Flyout이 열릴 때마다 리소스 그리드를 접고, 리소스 아이콘을 지연 로드한다(오픈 성능).
    private async void OnIconFlyoutOpening(object? sender, object e)
    {
        ViewModel.ShowResourceGrid = false;
        await ViewModel.EnsureResourceIconsAsync();
    }

    // "앱 추가" Flyout이 처음 열릴 때 설치 앱을 지연 로드한다(다이얼로그 오픈/취소 성능 — plan.md Debug 섹션).
    private async void OnAppPickerFlyoutOpening(object? sender, object e)
        => await ViewModel.EnsurePickerLoadedAsync();

    private async void OnUserIconClick(object sender, RoutedEventArgs e)
    {
        // 아이콘 Flyout을 닫고 파일 선택기를 띄운다(닫힘 UI 사이클 양보 후 호출해 포커스 경합 회피).
        (IconButton.Flyout as Flyout)?.Hide();
        await Task.Yield();

        var picker = new FileOpenPicker();
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".ico" })
            picker.FileTypeFilter.Add(ext);

        if (App.MainWindow is not null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            ViewModel.SetUserImage(file.Path);
    }

    // "파일 추가" 클릭 시 실행 파일(.exe/.lnk) 선택기를 띄워 선택 파일을 앱 목록에 추가한다(아이콘 선택기와 동일 패턴).
    private async void OnAddFileClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        picker.FileTypeFilter.Add(".lnk");

        if (App.MainWindow is not null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
            ViewModel.AddManualFile(file.Path);
    }

    private void OnShowResourceGrid(object sender, RoutedEventArgs e)
        => ViewModel.ShowResourceGrid = true;

    private void OnResourceIconSelected(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ResourceIconItem item)
            ViewModel.SetResourceIcon(item.Uri);
        (IconButton.Flyout as Flyout)?.Hide();
    }

    private void OnAppPickerItemClick(object sender, ItemClickEventArgs e)
    {
        // 항목 클릭으로 추가↔해제 토글(Flyout은 유지하여 연속 선택 가능).
        if (e.ClickedItem is PopupAppItem item)
            ViewModel.ToggleApp(item.App);
    }

    // 앱 피커 목록은 컨테이너가 실제로 화면에 실현될 때만 아이콘을 지연 로드한다
    // (설치 앱 전체 선로딩 제거 → WinRT 객체 churn/파이널라이저 압력↓, plan.md 크래시 완화 T2).
    private void OnAppPickerContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        // 재활용 큐로 들어가는 컨테이너는 무시(검색마다 Clear+재구성되어 재활용 빈번).
        if (args.InRecycleQueue) return;
        if (args.Item is PopupAppItem item)
            item.EnsureIconLoad();
    }

    private void OnRemoveAppClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PopupAppItem item)
            ViewModel.RemoveApp(item);
    }

    // 읽기전용 이름을 클릭하면 입력창으로 전환하고, 전환 직후 포커스를 줘 바로 편집 가능하게 한다.
    private void OnEditNameClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsNameEditing = true;
        // Visibility 전환 직후 즉시 Focus가 무시될 수 있어 한 틱 양보 후 포커스/전체 선택.
        DispatcherQueue.TryEnqueue(() =>
        {
            NameTextBox.Focus(FocusState.Programmatic);
            NameTextBox.SelectAll();
        });
    }
}
