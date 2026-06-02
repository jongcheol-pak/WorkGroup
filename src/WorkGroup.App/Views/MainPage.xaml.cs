using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WorkGroup.App.ViewModels;
using WorkGroup.Application.Shortcuts;
using WorkGroup.Domain.Groups;

namespace WorkGroup.App.Views
{
    /// <summary>
    /// 메인 관리 화면(plan.md T9/T10). 설치 앱→그룹 구성, 그룹 목록을 작업 표시줄로 드래그 등록.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainViewModel ViewModel { get; }

        public MainPage()
        {
            this.InitializeComponent();
            ViewModel = App.Services.GetRequiredService<MainViewModel>();
            Loaded += async (_, _) => await ViewModel.LoadAsync();
        }

        private void OnInstalledAppDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is AppEntry app)
                ViewModel.AddAppCommand.Execute(app);
        }

        private void OnRemoveAppClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is AppEntry app)
                ViewModel.RemoveAppCommand.Execute(app);
        }

        private void OnGroupDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is AppGroup group)
                ViewModel.EditGroupCommand.Execute(group);
        }

        private void OnDeleteGroupClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is AppGroup group)
                ViewModel.DeleteGroupCommand.Execute(group);
        }

        /// <summary>
        /// 그룹을 작업 표시줄로 드래그(plan.md T10). 검증된 방식: .lnk 임시 복사 + 지연 SetDataProvider.
        /// </summary>
        private void OnGroupDragStarting(object sender, DragItemsStartingEventArgs e)
        {
            if (e.Items.Count == 0 || e.Items[0] is not AppGroup group)
            {
                e.Cancel = true;
                return;
            }

            var shortcuts = App.Services.GetRequiredService<IShortcutService>();
            var lnkPath = shortcuts.GetShortcutPath(group);
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

        private async void OnIconOptionChanged(object sender, SelectionChangedEventArgs e)
        {
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
        }
    }
}
