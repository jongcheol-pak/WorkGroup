using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WorkGroup.App.ViewModels;
using WorkGroup.Application.Folders;

namespace WorkGroup.App.Views;

/// <summary>
/// 트레이 메뉴(폴더 바로가기 관리) 페이지. 등록 폴더 목록/검색/추가/편집/삭제/설정.
/// 트레이 아이콘 좌클릭 시 여기 등록된 폴더가 팝업으로 표시된다(T8).
/// </summary>
public sealed partial class TrayMenuPage : Page
{
    public FolderShortcutsViewModel ViewModel { get; }

    public TrayMenuPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<FolderShortcutsViewModel>();
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    private async void OnAddClick(object sender, RoutedEventArgs e)
        => await ShowEditDialogAsync(null);

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is FolderShortcutItem item)
            await ShowEditDialogAsync(item);
    }

    private async Task ShowEditDialogAsync(FolderShortcutItem? item)
    {
        var dialog = new FolderEditDialog { XamlRoot = XamlRoot };
        dialog.Configure(item?.Id, item?.Name, item?.Path);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.LoadAsync();
    }

    private void OnOpenLocationClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is FolderShortcutItem item)
            App.Services.GetRequiredService<IShellOpener>().Open(item.Path);
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FolderShortcutItem item)
            return;

        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "폴더 삭제",
            Content = $"'{item.Name}' 폴더를 목록에서 삭제할까요?",
            PrimaryButtonText = "삭제",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.DeleteAsync(item.Id);
    }

    private async void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new FolderPopupSettingsDialog { XamlRoot = XamlRoot };
        await dialog.ShowAsync();
    }
}
