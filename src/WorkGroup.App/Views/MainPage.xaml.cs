using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WorkGroup.Infrastructure.Activation;
using WorkGroup.Infrastructure.Shortcuts;

namespace WorkGroup.App.Views
{
    /// <summary>
    /// T2 게이트 수동 검증용 화면. 테스트 .lnk 생성(C1)과 작업 표시줄 드래그(C4)를 제공한다.
    /// </summary>
    public partial class MainPage : Page
    {
        private const string SpikeGroupId = "spike-test";
        private string? _lnkPath;

        public MainPage()
        {
            this.InitializeComponent();
        }

        private void OnCreateShortcut(object sender, RoutedEventArgs e)
        {
            try
            {
                var lnkPath = BuildSpikeShortcut();
                _lnkPath = lnkPath;
                ShowStatus(InfoBarSeverity.Success, $"바로가기 생성됨: {lnkPath}");
                RevealInExplorer(lnkPath);
            }
            catch (Exception ex)
            {
                ShowStatus(InfoBarSeverity.Error, $"바로가기 생성 실패: {ex.Message}");
            }
        }

        /// <summary>실행 별칭(WorkGroupSpike.exe)을 가리키는 테스트 .lnk를 만든다.</summary>
        private static string BuildSpikeShortcut()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var aliasExe = Path.Combine(localAppData, "Microsoft", "WindowsApps", "WorkGroupSpike.exe");
            var lnkPath = Path.Combine(localAppData, "WorkGroup", "Shortcuts", "spike-test.lnk");

            new ShortcutWriter().Create(
                lnkPath,
                aliasExe,
                GroupArgs.BuildCommandLineArguments(SpikeGroupId),
                iconPath: aliasExe,
                description: "WorkGroup spike 테스트 그룹");

            return lnkPath;
        }

        private static void RevealInExplorer(string path)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }

        private async void OnDragStarting(UIElement sender, DragStartingEventArgs e)
        {
            if (string.IsNullOrEmpty(_lnkPath) || !File.Exists(_lnkPath))
            {
                e.Cancel = true;
                ShowStatus(InfoBarSeverity.Warning, "먼저 '테스트 바로가기 생성'을 눌러주세요.");
                return;
            }

            var deferral = e.GetDeferral();
            try
            {
                // 탐색기에서 파일을 끌 때와 동일하게 .lnk를 셸 파일로 제공한다(plan.md T2 C4).
                var file = await StorageFile.GetFileFromPathAsync(_lnkPath);
                e.Data.SetStorageItems(new[] { file });
                e.Data.RequestedOperation = DataPackageOperation.Copy;
            }
            catch (Exception ex)
            {
                e.Cancel = true;
                ShowStatus(InfoBarSeverity.Error, $"드래그 준비 실패: {ex.Message}");
            }
            finally
            {
                deferral.Complete();
            }
        }

        private void ShowStatus(InfoBarSeverity severity, string message)
        {
            StatusBar.Severity = severity;
            StatusBar.Message = message;
            StatusBar.IsOpen = true;
        }
    }
}
