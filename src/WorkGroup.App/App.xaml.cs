using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
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
        // 트레이 좌클릭으로 띄운 폴더 목록 팝업(중복 생성 방지용 추적).
        private Views.FolderListPopupWindow? _folderPopup;
        // 상주 인스턴스가 재사용하는 그룹 팝업 창(첫 핀 클릭에 1회 생성, 이후 ShowForGroup으로 반복 표시).
        private Views.GroupPopupWindow? _groupPopup;

        // 메인 인스턴스 등록 키. 핀 클릭 시 상주 메인 인스턴스를 찾을 때도 이 키로 식별한다.
        private const string MainInstanceKey = "WorkGroupMainInstance";

        // single-instance 키(메인/편집/핀 redirect 경로 등록). Activated 이벤트는 이 인스턴스에서 발생한다.
        private AppInstance? _keyInstance;
        // 메인 UI 스레드 큐(OnLaunched에서 캡처). Activated가 백그라운드 스레드라 UI 마샬링에 사용.
        private DispatcherQueue? _uiDispatcherQueue;
        // 메인 창/페이지가 아직 준비되기 전 들어온 "그룹 수정" 요청을 보관했다가 WorkGroupsPage가 소비한다.
        internal static string? PendingEditGroupId { get; set; }

        /// <summary>전역 DI 컨테이너(plan.md T9 조립).</summary>
        public static IServiceProvider Services { get; private set; } = default!;

        /// <summary>메인 창(파일 선택 등 HWND가 필요한 작업용).</summary>
        public static Window? MainWindow { get; private set; }

        public App()
        {
            // DI 구성과 전역 로컬라이저 설정을 InitializeComponent 이전에 수행한다(plan.md T1).
            // → App.xaml을 포함한 모든 XAML 로드 시점에 {loc:Localize}의 Current가 보장된다.
            Services = ServiceConfiguration.Build();
            LocalizationService.Current = Services.GetRequiredService<LocalizationService>();

            // 저장된 언어를 창 생성 이전에 적용한다(plan.md T2) — 이후 모든 XAML이 해당 언어로 로드된다.
            Services.GetRequiredService<LanguageService>().ApplyOnStartup();

            this.InitializeComponent();
        }

        /// <summary>
        /// 활성화 분기(plan.md T2/T12):
        /// - 그룹 id(핀 클릭/프로토콜) → 메인 인스턴스 상주 시 redirect, 없으면 이 프로세스가 트레이에 상주 + 팝업 표시.
        /// - 로그인 자동 시작 → 트레이만 상주(메인 창 미표시).
        /// - 일반 실행 → 트레이 + 메인 창.
        /// </summary>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            // Activated(백그라운드 스레드) 마샬링에 쓸 메인 UI 스레드 큐를 진입 시 캡처한다.
            _uiDispatcherQueue = DispatcherQueue.GetForCurrentThread();

            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            var groupId = ActivationParser.TryGetGroupId(activation);

            if (!string.IsNullOrWhiteSpace(groupId))
            {
                // 상주 중인 메인 인스턴스가 있으면 그쪽으로 활성화를 넘겨(redirect) 즉시 팝업을 띄운다
                // — 새 프로세스/DI 초기화/저장소 로드 비용을 피해 표시 지연을 없앤다.
                // 없으면(콜드) 이 프로세스가 키를 등록해 트레이에 상주하면서 팝업을 표시한다(이후 클릭은 redirect, 종료는 트레이로).
                var keyInstance = AppInstance.FindOrRegisterForKey(MainInstanceKey);
                if (!keyInstance.IsCurrent)
                {
                    RedirectActivationTo(activation, keyInstance);
                    Exit();
                    return;
                }
                BecomeResidentInstance(keyInstance);
                ShowGroupPopup(groupId); // 재사용 창으로 표시(닫혀도 Hide로 상주 유지).
                return;
            }

            // 메인/편집 활성화는 단일 인스턴스로 합친다. 이미 메인 인스턴스가 있으면 그쪽으로 넘기고 종료.
            var keyInstanceMain = AppInstance.FindOrRegisterForKey(MainInstanceKey);
            if (!keyInstanceMain.IsCurrent)
            {
                RedirectActivationTo(activation, keyInstanceMain);
                Exit();
                return;
            }
            BecomeResidentInstance(keyInstanceMain);

            // "그룹 수정" 활성화면 메인 창을 열어 해당 그룹 편집 다이얼로그를 표시한다.
            var editId = ActivationParser.TryGetEditGroupId(activation);
            if (!string.IsNullOrWhiteSpace(editId))
                RouteEditRequest(editId);
            else if (activation.Kind != ExtendedActivationKind.StartupTask)
                // 로그인 자동 시작이면 트레이만 상주(메인 창은 사용자가 트레이로 연다).
                ShowMainWindow();
        }

        // 이 프로세스를 상주(트레이) 메인 인스턴스로 설정한다: 키 인스턴스 보관 + Activated(redirect) 구독 +
        // 트레이 아이콘 + 팝업 창 prewarm + 고아 산출물 정리. 메인/편집 경로와 핀-콜드 상주 경로가 공유한다.
        private void BecomeResidentInstance(AppInstance keyInstance)
        {
            _keyInstance = keyInstance;
            keyInstance.Activated += OnAppInstanceActivated;

            EnsureTray();

            // 그룹 팝업 재사용 창을 유휴 시점에 미리 생성(prewarm)해 첫 핀 클릭의 창 생성·Mica 초기화 비용을 옮긴다.
            // 가시 시작(트레이/메인 창)을 늦추지 않도록 Low 우선순위로 지연, 실패해도 첫 클릭이 지연 생성으로 폴백한다.
            _uiDispatcherQueue?.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                try { EnsureGroupPopup(); }
                catch { /* prewarm 실패는 무시 — ShowGroupPopup이 지연 생성 폴백 */ }
            });

            // 상주 시작 시 저장소와 불일치하는 고아 .lnk/.ico 정리(plan.md T8 연결).
            _ = Services.GetRequiredService<IGroupAppService>().CleanupOrphansAsync();
        }

        // UI 스레드 데드락을 피하려고 redirect를 별도 스레드에서 수행하고 완료를 동기 대기한다(공식 Instancing 패턴).
        private static void RedirectActivationTo(AppActivationArguments args, AppInstance keyInstance)
        {
            using var redirectSemaphore = new SemaphoreSlim(0, 1);
            _ = Task.Run(async () =>
            {
                await keyInstance.RedirectActivationToAsync(args);
                redirectSemaphore.Release();
            });
            redirectSemaphore.Wait();
        }

        // 상주 중인 메인 인스턴스가 redirect로 받은 활성화(백그라운드 스레드) — UI 스레드로 마샬링해 처리한다.
        private void OnAppInstanceActivated(object? sender, AppActivationArguments e)
        {
            _uiDispatcherQueue?.TryEnqueue(() =>
            {
                // 핀 클릭/프로토콜(그룹 id)이면 팝업을 띄운다(editId와 상호 배타 — 먼저 분기 후 즉시 반환).
                var groupId = ActivationParser.TryGetGroupId(e);
                if (!string.IsNullOrWhiteSpace(groupId))
                {
                    ShowGroupPopup(groupId);
                    return;
                }

                var editId = ActivationParser.TryGetEditGroupId(e);
                if (!string.IsNullOrWhiteSpace(editId))
                    RouteEditRequest(editId);
                else if (e.Kind != ExtendedActivationKind.StartupTask)
                    // 일반 재활성화 — 메인 창을 앞으로.
                    // 로그인 자동 시작이 redirect로 들어온 경우는 제외(이미 상주 중이므로 아무 동작 불필요)
                    // — 콜드 경로(OnLaunched)와 동일한 StartupTask 검사로 메인 창 표시를 막는다.
                    ShowMainWindow();
            });
        }

        // "그룹 수정" 요청을 메인 창의 작업 그룹 페이지로 라우팅한다.
        // 페이지가 아직 준비 전이면 PendingEditGroupId로 보관해 페이지 Loaded가 소비한다(plan D9).
        private void RouteEditRequest(string editId)
        {
            PendingEditGroupId = editId;
            ShowMainWindow();

            if (_window?.Content is Frame { Content: MainShell shell })
            {
                shell.SelectWorkGroups();
                // 페이지가 이미 "로드 완료"된 경우(상주 중 재요청)에만 즉시 처리한다.
                // 콜드 시작에선 SelectWorkGroups가 페이지를 동기 navigate해 CurrentWorkGroupsPage가 non-null이 되지만
                // 아직 Loaded 전이라 XamlRoot이 null → 즉시 EditGroupByIdAsync를 호출하면 ContentDialog가 실패한다.
                // 이 경우 PendingEditGroupId를 남겨 페이지 Loaded(OnPageLoaded)가 XamlRoot 준비 후 소비하도록 한다.
                if (shell.CurrentWorkGroupsPage is { IsLoaded: true } page && PendingEditGroupId is { } id)
                {
                    PendingEditGroupId = null;
                    _ = page.EditGroupByIdAsync(id);
                }
            }
        }

        private void EnsureTray()
        {
            if (_tray is not null) return;
            _tray = new TrayIconService();
            // 트레이 콜백은 메시지 전용 창의 원시 Win32 WndProc(=UI 스레드) 안에서 동기로 호출된다.
            // 그 재진입 상태에서 WinUI Window를 생성/Activate하면 Microsoft.UI.Input가 fail-fast(c0000602)로 종료한다.
            // → TryEnqueue로 깨끗한 메시지 루프 턴까지 지연해 재진입을 푼다(OnAppInstanceActivated와 동일 패턴).
            _tray.OpenRequested += () => _uiDispatcherQueue?.TryEnqueue(ShowMainWindow);       // 우클릭 메뉴 "열기" → 메인 창
            _tray.LeftClickRequested += () => _uiDispatcherQueue?.TryEnqueue(ShowFolderListPopup); // 좌클릭 → 폴더 목록 팝업
            _tray.ExitRequested += () => _uiDispatcherQueue?.TryEnqueue(() =>
            {
                // 좌/우클릭과 동일하게 WndProc 재진입을 벗어난 뒤 종료한다(자기 hwnd DestroyWindow를 그 WndProc 안에서 호출하지 않도록).
                _exiting = true; // 이후 창 Closing을 취소하지 않고 실제 종료한다.
                // 메인 창을 실제로 닫아 WinUIEx의 Window.Closed가 발생하도록 한다
                // → 창 크기/위치 persistence가 이 시점에 저장된다(plan.md DW5).
                // 핀-콜드 상주 경로는 메인 창을 띄우지 않아 _window가 null일 수 있다(?.로 null-safe 처리).
                _window?.Close();
                _groupPopup?.Close(); // 재사용 그룹 팝업은 Hide로 살아 있으므로 종료 시 명시적으로 닫는다.
                _tray?.Dispose();
                Exit();
            });
            _tray.Initialize();
        }

        private void ShowFolderListPopup()
        {
            // 직전 팝업이 떠 있으면 닫고 새로 띄운다(중복 창 방지).
            // 외부 Close가 아니라 CloseSelf로 닫아, 직전 팝업의 _closed를 동기 set(큐에 남은 호버 Tick 차단).
            _folderPopup?.CloseSelf();
            var popup = new FolderListPopupWindow();
            _folderPopup = popup;
            popup.Closed += (_, _) => { if (ReferenceEquals(_folderPopup, popup)) _folderPopup = null; };
            popup.Activate();
        }

        // 상주 인스턴스가 핀 클릭 redirect를 받아 그룹 팝업을 띄운다. 매 클릭 새 창을 만들지 않고
        // 재사용 창(_groupPopup) 1개를 ShowForGroup으로 다시 채워 표시한다(창 생성·Mica 초기화 비용·깜빡임 제거).
        // 재사용 창은 Deactivated 시 Close가 아니라 Hide로 살아 있고, 종료 정리는 EnsureTray의 ExitRequested가 담당한다.
        private void ShowGroupPopup(string groupId)
        {
            EnsureGroupPopup();
            _groupPopup!.ShowForGroup(groupId); // EnsureGroupPopup 직후라 비-null.
        }

        // 재사용 그룹 팝업 창을 1회 생성한다(이미 있으면 무시). 첫 클릭 또는 시작 시 prewarm이 공유한다.
        private void EnsureGroupPopup()
        {
            if (_groupPopup is not null)
                return;
            var popup = new GroupPopupWindow(); // 재사용(웜) 생성자 — InitializeChrome만 수행(Activate 전까지 비표시).
            _groupPopup = popup;
            // 종료 등으로 닫히면 추적 해제(닫힌 창 재사용 방지).
            popup.Closed += (_, _) => { if (ReferenceEquals(_groupPopup, popup)) _groupPopup = null; };
        }

        /// <summary>폴더 팝업의 설정(톱니)에서 메인 창의 "트레이 메뉴" 탭을 연다.</summary>
        internal void ShowTrayMenuFromPopup()
        {
            ShowMainWindow();
            if (_window?.Content is Frame { Content: MainShell shell })
                shell.SelectTrayMenu();
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
