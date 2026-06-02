using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    /// 그룹을 작업 표시줄로 드래그(plan.md T7). 검증된 방식: .lnk 임시 복사 + 지연 SetDataProvider.
    /// </summary>
    private void OnGroupDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Count == 0 || e.Items[0] is not GroupListItem item)
        {
            e.Cancel = true;
            return;
        }

        var shortcuts = App.Services.GetRequiredService<IShortcutService>();
        var lnkPath = shortcuts.GetShortcutPath(item.Group);
        if (!File.Exists(lnkPath))
        {
            e.Cancel = true;
            ViewModel.StatusMessage = "그룹을 먼저 저장하세요(.lnk 없음).";
            return;
        }

        try
        {
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
            e.Cancel = true;
            ViewModel.StatusMessage = $"드래그 준비 실패: {ex.Message}";
        }
    }
}
