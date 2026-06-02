using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WorkGroup.App.ViewModels;
using WorkGroup.Application.Shortcuts;

namespace WorkGroup.App.Views;

/// <summary>
/// 작업 그룹 페이지(plan.md T7). 그룹 목록(아이콘 + 2라인 + 수정/삭제) 표시,
/// 그룹 추가/수정 다이얼로그 호출, 항목을 작업 표시줄로 드래그해 .lnk 핀.
/// </summary>
public sealed partial class WorkGroupsPage : Page
{
    public WorkGroupsViewModel ViewModel { get; }

    public WorkGroupsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<WorkGroupsViewModel>();
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    private async void OnAddClick(object sender, RoutedEventArgs e)
        => await ShowEditDialogAsync(null);

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GroupListItem item)
            await ShowEditDialogAsync(item.Group);
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not GroupListItem item)
            return;

        // 파괴적 작업(.lnk/.ico 영구 삭제)이므로 확인을 받는다.
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "그룹 삭제",
            Content = $"'{item.Group.Name}' 그룹을 삭제할까요? 작업 표시줄 핀(.lnk)도 제거됩니다.",
            PrimaryButtonText = "삭제",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.DeleteAsync(item.Group);
    }

    private async Task ShowEditDialogAsync(Domain.Groups.AppGroup? group)
    {
        var dialog = new GroupEditDialog { XamlRoot = XamlRoot };
        dialog.Configure(group);
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            await ViewModel.LoadAsync();
    }

    /// <summary>
    /// 그룹 카드를 작업 표시줄로 드래그(plan.md T7). 검증된 방식: .lnk 임시 복사 + 지연 SetDataProvider.
    /// 드래그 비주얼은 그룹 아이콘으로 표시한다(DragUI). DragStarting에는 Cancel이 없어, 중단은 데이터 미설정으로 처리.
    /// </summary>
    private void OnGroupDragStarting(UIElement sender, DragStartingEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GroupListItem item)
            return;

        // 드래그 비주얼을 항목 카드 스냅샷 대신 그룹 아이콘으로(이미 로드된 BitmapImage 재사용; 없으면 기본 비주얼).
        if (item.Icon is BitmapImage iconBitmap)
            e.DragUI.SetContentFromBitmapImage(iconBitmap);

        var shortcuts = App.Services.GetRequiredService<IShortcutService>();
        var lnkPath = shortcuts.GetShortcutPath(item.Group);
        if (!File.Exists(lnkPath))
        {
            // DragStarting은 취소 불가 → 데이터를 넣지 않으면 드롭해도 받을 페이로드가 없어 무해(no-op).
            ViewModel.StatusMessage = "그룹을 먼저 저장하세요(.lnk 없음).";
            return;
        }

        try
        {
            e.AllowedOperations = DataPackageOperation.Copy | DataPackageOperation.Link;
            e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Link;

            var tempDir = Path.Combine(Path.GetTempPath(), "WorkGroupDrag");
            // 이전 드래그의 임시 .lnk 누적을 막는다(직전 드롭은 이미 완료된 상태).
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
            catch { /* 정리 실패는 무시 */ }
            Directory.CreateDirectory(tempDir);
            var tempLnk = Path.Combine(tempDir, Path.GetFileName(lnkPath));
            File.Copy(lnkPath, tempLnk, overwrite: true);

            e.Data.SetText(lnkPath);
            e.Data.SetDataProvider(StandardDataFormats.StorageItems, async request =>
            {
                var deferral = request.GetDeferral();
                try
                {
                    var folder = await StorageFolder.GetFolderFromPathAsync(tempDir);
                    var file = await folder.GetFileAsync(Path.GetFileName(tempLnk));
                    request.SetData(new List<IStorageItem> { file });
                }
                finally
                {
                    deferral.Complete();
                }
            });
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"드래그 준비 실패: {ex.Message}";
        }
    }
}
