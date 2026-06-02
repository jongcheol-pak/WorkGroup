using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WorkGroup.App.ViewModels;
using WorkGroup.Domain.Groups;

namespace WorkGroup.App.Views;

/// <summary>
/// 그룹 추가/수정 다이얼로그(plan.md T6/DU2). 상단에 이름·아이콘 설정, 하단에 설치 앱 체크 목록.
/// 확인 시 GroupAppService로 저장하고, 실패하면 닫히지 않는다. 호출자는 XamlRoot 지정 + Configure 후 ShowAsync.
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
        // 저장이 끝날 때까지 다이얼로그 닫힘을 보류한다. 실패 시 닫지 않는다.
        var deferral = args.GetDeferral();
        try
        {
            var ok = await ViewModel.SaveAsync();
            if (!ok)
                args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void OnIconOptionChanged(object sender, SelectionChangedEventArgs e)
    {
        // "이미지 선택..."을 고르면 파일 선택기를 띄운다(기존 MainPage 로직 이식).
        if (ViewModel.SelectedIconOption != "이미지 선택...")
            return;

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
            ViewModel.CustomImagePath = file.Path;
        else if (string.IsNullOrWhiteSpace(ViewModel.CustomImagePath))
            // 선택 취소 + 기존 이미지 없음 → 옵션을 기본으로 되돌려 UI와 저장값을 일치시킨다.
            ViewModel.SelectedIconOption = "기본";
    }
}
