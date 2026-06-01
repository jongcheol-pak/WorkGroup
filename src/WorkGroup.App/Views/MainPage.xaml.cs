using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;
using WorkGroup.App.Interop;
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
        /// 출력은 MSIX 가상화 대상이 아닌 패키지 실제 폴더(LocalFolder)에 둔다.
        /// </summary>
        private static string BuildSpikeShortcut()
        {
            // 별칭 타깃은 셸이 실행하는 실제 경로(가상화되지 않음).
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var aliasExe = Path.Combine(localAppData, "Microsoft", "WindowsApps", "WorkGroupSpike.exe");

            // 출력은 %LOCALAPPDATA% 대신 패키지 LocalFolder(리다이렉트 없음).
            var shortcutsDir = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Shortcuts");
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

        private void OnDragSourcePointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lnkPath) || !File.Exists(_lnkPath))
            {
                ShowStatus(InfoBarSeverity.Warning, "먼저 '테스트 바로가기 생성'을 눌러주세요.");
                return;
            }

            try
            {
                // 셸 IDataObject(Shell IDList 포함)로 네이티브 OLE 드래그를 시작한다.
                // 누른 상태로 작업 표시줄까지 끌어 떼면 핀된다(plan.md T2 C4).
                ShellFileDragSource.BeginDrag(_lnkPath);
            }
            catch (Exception ex)
            {
                ShowStatus(InfoBarSeverity.Error, $"드래그 시작 실패: {ex.Message}");
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
