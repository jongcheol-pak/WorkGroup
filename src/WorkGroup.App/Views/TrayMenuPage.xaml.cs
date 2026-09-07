using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using WorkGroup.App.Services;
using WorkGroup.App.ViewModels;
using WorkGroup.Application.Folders;
using WorkGroup.Infrastructure.Ui;

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

        var loc = LocalizationService.Current;
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = loc?.Get("TrayMenu_DeleteTitle") ?? string.Empty,
            Content = loc?.Get("TrayMenu_DeleteConfirmFormat", item.Name) ?? string.Empty,
            PrimaryButtonText = loc?.Get("Common_Delete") ?? string.Empty,
            CloseButtonText = loc?.Get("Common_Cancel") ?? string.Empty,
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

    // ----- 드래그 순서 변경(핸들 → 목록에 드롭). 작업 그룹 페이지와 같은 어댑터를 쓴다. -----

    private void OnReorderDragStarting(UIElement sender, DragStartingEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FolderShortcutItem item)
            return;

        var index = ViewModel.Folders.IndexOf(item);
        if (index < 0)
            return;

        e.Data.RequestedOperation = DataPackageOperation.Move;
        e.Data.SetData(ReorderDrop.IndexFormat, index.ToString());
    }

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(ReorderDrop.IndexFormat))
        {
            // 재정렬이 아닌 드래그(외부 파일 등)는 받지 않는다.
            e.AcceptedOperation = DataPackageOperation.None;
            HideDropIndicator();
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        var target = ReorderDrop.ResolveDropTarget(FoldersList, e.GetPosition(FoldersList));
        DropIndicator.Margin = new Thickness(0, target.IndicatorOffset, 0, 0);
        DropIndicator.Visibility = Visibility.Visible;
    }

    private void OnListDragLeave(object sender, DragEventArgs e) => HideDropIndicator();

    private async void OnListDrop(object sender, DragEventArgs e)
    {
        HideDropIndicator();
        if (!e.DataView.Contains(ReorderDrop.IndexFormat))
            return;

        // 드롭 지점은 DataView를 읽는 await 전에 잡아 둔다(이후 좌표가 유효하지 않을 수 있다).
        var target = ReorderDrop.ResolveDropTarget(FoldersList, e.GetPosition(FoldersList));

        var raw = await e.DataView.GetDataAsync(ReorderDrop.IndexFormat);
        if (!int.TryParse(raw as string, out var fromIndex))
            return;

        if (ListInsertionPoint.ResolveMoveTarget(fromIndex, target.InsertionIndex, ViewModel.Folders.Count) is not { } toIndex)
            return;

        await ViewModel.MoveAsync(fromIndex, toIndex);
    }

    private void HideDropIndicator() => DropIndicator.Visibility = Visibility.Collapsed;
}
