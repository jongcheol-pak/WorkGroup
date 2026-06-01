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

                // MSIX 가상화/실패로 인한 거짓 성공을 막기 위해 실제 존재를 확인한다.
                if (!File.Exists(lnkPath))
                {
                    ShowStatus(InfoBarSeverity.Error, $"저장은 보고됐으나 파일이 없습니다: {lnkPath}");
                    return;
                }

                _lnkPath = lnkPath;
                ShowStatus(InfoBarSeverity.Success, $"바로가기 생성됨: {lnkPath}");
                RevealInExplorer(lnkPath);
            }
            catch (Exception ex)
            {
                ShowStatus(InfoBarSeverity.Error, $"바로가기 생성 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 실행 별칭(WorkGroupSpike.exe)을 가리키는 테스트 .lnk를 만든다.
        /// 셸이 접근하는 .lnk는 MSIX 가상화 대상이 아닌 %USERPROFILE% 하위에 둔다(AppGroup 검증 방식).
        /// </summary>
        private static string BuildSpikeShortcut()
        {
            // 별칭 타깃은 셸이 실행하는 실제 경로(가상화되지 않음).
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var aliasExe = Path.Combine(localAppData, "Microsoft", "WindowsApps", "WorkGroupSpike.exe");

            // %USERPROFILE%\WorkGroup\Shortcuts (비가상화 — 셸/작업 표시줄이 일관되게 접근).
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var shortcutsDir = Path.Combine(userProfile, "WorkGroup", "Shortcuts");
            var lnkPath = Path.Combine(shortcutsDir, "spike-test.lnk");

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

        /// <summary>
        /// ListView 항목 드래그 시작. .lnk를 임시 폴더로 복사하고 StorageItems를 '지연 제공'한다.
        /// 외부(작업 표시줄)가 드롭 시점에 데이터를 요청할 때만 StorageFile을 넘겨야 핀이 동작한다(AppGroup 검증 방식).
        /// </summary>
        private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            if (string.IsNullOrEmpty(_lnkPath) || !File.Exists(_lnkPath))
            {
                e.Cancel = true;
                ShowStatus(InfoBarSeverity.Warning, "먼저 '테스트 바로가기 생성'을 눌러주세요.");
                return;
            }

            try
            {
                e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Link;

                // 드래그용 .lnk를 임시 폴더로 복사(원본 잠금 방지).
                var tempDir = Path.Combine(Path.GetTempPath(), "WorkGroupDrag");
                Directory.CreateDirectory(tempDir);
                var tempLnk = Path.Combine(tempDir, Path.GetFileName(_lnkPath));
                File.Copy(_lnkPath, tempLnk, overwrite: true);

                e.Data.SetText(_lnkPath);

                // 지연 제공: 외부 드롭 대상이 요청할 때 StorageFile을 넘긴다.
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
                ShowStatus(InfoBarSeverity.Error, $"드래그 준비 실패: {ex.Message}");
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
