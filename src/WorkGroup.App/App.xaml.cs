using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.AppLifecycle;
using WinUIEx;
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
            _tray.OpenRequested += ShowMainWindow;       // 우클릭 메뉴 "열기" → 메인 창
            _tray.LeftClickRequested += ShowFolderListPopup; // 좌클릭 → 폴더 목록 팝업
            _tray.ExitRequested += () =>
            {
                _exiting = true; // 이후 창 Closing을 취소하지 않고 실제 종료한다.
                // 메인 창을 실제로 닫아 WinUIEx의 Window.Closed가 발생하도록 한다
                // → 창 크기/위치 persistence가 이 시점에 저장된다(plan.md DW5).
                // (트레이는 메인 프로세스에만 초기화되므로 여기서 _window는 항상 메인 WindowEx — 팝업 분기는 EnsureTray 미호출로 미도달.)
                _window?.Close();
                _tray?.Dispose();
                Exit();
            };
            _tray.Initialize();
        }

        private void ShowFolderListPopup()
        {
            // 폴더 목록 팝업(FolderListPopupWindow)은 T8에서 연결한다.
        }

        private void ShowMainWindow()
        {
            if (_window is null)
            {
                // 메인 창을 WinUIEx WindowEx로 생성해 창 크기/위치 지속(PersistenceId)·최소 크기를 관리한다(plan.md DW1/DW3).
                // Mica는 표준 SystemBackdrop으로 둔다 — WinUIEx 2.9.1의 자체 Backdrop은 CS0618(Obsolete, 표준 SystemBackdrop 권장)이라 표준 API 사용(DW2).
                // WindowEx 전용 멤버는 로컬 변수로 설정하고, 필드/정적은 Window? 타입을 유지한다(팝업 분기·HWND 호환).
                var win = new WindowEx
                {
                    SystemBackdrop = new MicaBackdrop(),
                    PersistenceId = "WorkGroupMain",
                    MinWidth = 800,
                    MinHeight = 560,
                    // 콘텐츠를 타이틀바 영역까지 확장 → MainShell의 TitleBar 컨트롤이 드래그/캡션 영역과 자동 동기(plan.md DG3).
                    ExtendsContentIntoTitleBar = true
                };
                // 작업 표시줄/Alt+Tab 미리보기 아이콘을 앱 아이콘으로 지정(패키지 설치 폴더 기준 상대경로).
                win.AppWindow.SetIcon(@"Assets\AppIcon.ico");
                // 닫기를 가로채 트레이로 숨긴다(종료는 트레이 메뉴에서만).
                win.AppWindow.Closing += OnMainWindowClosing;
                _window = win;
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
