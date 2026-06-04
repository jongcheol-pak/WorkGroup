using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using WorkGroup.App.Services;
using WorkGroup.App.ViewModels;
using WorkGroup.Application.Shortcuts;
using WorkGroup.Domain.Groups;

namespace WorkGroup.App.Views;

/// <summary>
/// 작업 그룹 페이지(plan.md T7). 그룹 목록(아이콘 + 2라인 + 수정/삭제) 표시,
/// 그룹 추가/수정 다이얼로그 호출, 항목을 작업 표시줄로 드래그해 .lnk 핀.
/// </summary>
public sealed partial class WorkGroupsPage : Page
{
    public WorkGroupsViewModel ViewModel { get; }

    // 그룹 수정 다이얼로그 표시 중 재요청을 무시하기 위한 가드(plan D10).
    private bool _editDialogOpen;

    public WorkGroupsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<WorkGroupsViewModel>();
        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
        // 페이지 준비 전 들어온 "그룹 수정" 요청을 로드 완료 후 소비한다(plan D9).
        if (App.PendingEditGroupId is { } id)
        {
            App.PendingEditGroupId = null;
            await EditGroupByIdAsync(id);
        }
    }

    /// <summary>외부("그룹 수정" 활성화)에서 지정한 그룹의 편집 다이얼로그를 연다.</summary>
    public async Task EditGroupByIdAsync(string groupId)
    {
        if (_editDialogOpen)
            return; // 표시 중 재요청은 무시(plan D10).

        // 직접 호출 경로(상주 중)에서 아직 목록이 비어 있으면 1회 로드한다.
        if (ViewModel.Groups.Count == 0)
            await ViewModel.LoadAsync();

        var item = ViewModel.Groups.FirstOrDefault(g => g.Group.Id.Value == groupId);
        if (item is null)
            return; // 삭제됐거나 없는 그룹 — 메인 창만 표시(plan D8).

        _editDialogOpen = true;
        try
        {
            await ShowEditDialogAsync(item.Group);
        }
        finally
        {
            _editDialogOpen = false;
        }
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
    private async void OnGroupDragStarting(UIElement sender, DragStartingEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GroupListItem item)
            return;

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
                var providerDeferral = request.GetDeferral();
                try
                {
                    var folder = await StorageFolder.GetFolderFromPathAsync(tempDir);
                    var file = await folder.GetFileAsync(Path.GetFileName(tempLnk));
                    request.SetData(new List<IStorageItem> { file });
                }
                finally
                {
                    providerDeferral.Complete();
                }
            });

            // 드래그 비주얼을 그룹 아이콘으로. 화면 Image에 쓰이는 BitmapImage를 그대로 넘기면
            // 드래그 표면에 빈 이미지로 렌더되므로, 아이콘 파일을 SoftwareBitmap으로 새로 로드해 지정한다(비동기 → deferral).
            var deferral = e.GetDeferral();
            try
            {
                // 드래그 비주얼 크기(최대 변, 물리 px). 필요 시 이 값만 조정.
                const uint dragSize = 128;
                await SetDragVisualFromIconAsync(e, item.Group.Id, dragSize);
            }
            finally
            {
                deferral.Complete();
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"드래그 준비 실패: {ex.Message}";
        }
    }

    /// <summary>그룹 아이콘 파일(PNG 우선, 없으면 .ico)을 SoftwareBitmap으로 로드해 드래그 비주얼로 지정한다.</summary>
    /// <param name="maxSize">드래그 비주얼 최대 변 크기(물리 px). Windows 표준 아이콘 크기에 맞춤.</param>
    private static async Task SetDragVisualFromIconAsync(DragStartingEventArgs e, GroupId id, uint maxSize)
    {
        var path = GroupIconLoader.GetPngPath(id);
        if (!File.Exists(path))
            path = GroupIconLoader.GetIconPath(id);
        if (!File.Exists(path))
            return;

        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);

        // 원본 해상도 그대로면 너무 크다 → maxSize로 축소(종횡비 보존, 업스케일 금지).
        var scale = Math.Min(1.0, (double)maxSize / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
            InterpolationMode = BitmapInterpolationMode.Fant
        };

        // SetContentFromSoftwareBitmap은 BGRA8(Premultiplied)을 요구한다.
        var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        e.DragUI.SetContentFromSoftwareBitmap(bitmap);
    }
}
