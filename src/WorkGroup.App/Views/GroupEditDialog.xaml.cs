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
        // 후보 앱을 클릭하면 선택 목록에 추가한다(Flyout은 유지하여 연속 추가 가능).
        if (e.ClickedItem is PopupAppItem item)
            ViewModel.AddApp(item.App);
    }

    private void OnRemoveAppClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PopupAppItem item)
            ViewModel.RemoveApp(item);
    }
}
