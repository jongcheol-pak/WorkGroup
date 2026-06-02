using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.AppLifecycle;
using WorkGroup.App.Activation;
using WorkGroup.App.Services;
using WorkGroup.App.Views;
using WorkGroup.Application.Groups;

namespace WorkGroup.App
{
    /// <summary>
    /// 애플리케이션 진입점. WorkGroup.Application 네임스페이스와의 이름 충돌을 피하기 위해
    /// 베이스 타입을 정규화하여 지정한다.
    /// </summary>
    public partial class App : Microsoft.UI.Xaml.Application
    {
        private Window? _window;
        private TrayIconService? _tray;
        private bool _exiting;

        /// <summary>전역 DI 컨테이너(plan.md T9 조립).</summary>
        public static IServiceProvider Services { get; private set; } = default!;

        /// <summary>메인 창(파일 선택 등 HWND가 필요한 작업용).</summary>
        public static Window? MainWindow { get; private set; }

        public App()
        {
            this.InitializeComponent();
            Services = ServiceConfiguration.Build();
        }

        /// <summary>
        /// 활성화 분기(plan.md T2/T12):
        /// - 그룹 id(핀 클릭/프로토콜) → 팝업만 띄우고 닫히면 종료.
        /// - 로그인 자동 시작 → 트레이만 상주(메인 창 미표시).
        /// - 일반 실행 → 트레이 + 메인 창.
        /// </summary>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            var groupId = ActivationParser.TryGetGroupId(activation);

            if (!string.IsNullOrWhiteSpace(groupId))
            {
                var popup = new GroupPopupWindow(groupId);
                // 팝업 전용 인스턴스는 닫히면 프로세스를 종료한다(상주 안 함).
                popup.Closed += (_, _) => Exit();
                _window = popup;
                popup.Activate();
                return;
            }

            EnsureTray();

            // 메인 시작 시 저장소와 불일치하는 고아 .lnk/.ico 정리(plan.md T8 연결).
            _ = Services.GetRequiredService<IGroupAppService>().CleanupOrphansAsync();

            // 로그인 자동 시작이면 트레이만 상주(메인 창은 사용자가 트레이로 연다).
            if (activation.Kind != ExtendedActivationKind.StartupTask)
                ShowMainWindow();
        }

        private void EnsureTray()
        {
            if (_tray is not null) return;
            _tray = new TrayIconService();
            // 트레이 콜백은 메시지 전용 창의 WndProc(=UI 스레드)에서 호출된다.
            _tray.OpenRequested += ShowMainWindow;
            _tray.ExitRequested += () =>
            {
                _exiting = true; // 이후 창 Closing을 취소하지 않고 실제 종료한다.
                _tray?.Dispose();
                Exit();
            };
            _tray.Initialize();
        }

        private void ShowMainWindow()
        {
            if (_window is null)
            {
                _window = new Window();
                // 창 백드롭을 Mica로 설정한다(plan.md DU6 — 최초 생성 시 1회).
                _window.SystemBackdrop = new MicaBackdrop();
                // 닫기를 가로채 트레이로 숨긴다(종료는 트레이 메뉴에서만).
                _window.AppWindow.Closing += OnMainWindowClosing;
            }

            MainWindow = _window;
            if (_window.Content is not Frame rootFrame)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                _window.Content = rootFrame;
                // 저장된 테마를 루트 Frame에 적용·등록한다(plan.md DU5 — 이후 설정 변경도 이 루트에 반영).
                Services.GetRequiredService<Services.ThemeService>().Initialize(rootFrame);
            }
            if (rootFrame.Content is null)
                _ = rootFrame.Navigate(typeof(MainShell));

            _window.AppWindow.Show();
            _window.Activate();
        }

        private void OnMainWindowClosing(
            Microsoft.UI.Windowing.AppWindow sender,
            Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            if (_exiting) return; // 트레이 종료 시에는 실제로 닫는다.
            args.Cancel = true;   // 종료 대신
            sender.Hide();        // 트레이로 숨김
        }

        private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }
    }
}
