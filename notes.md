# 작업 노트

## 최근 변경
- 2026-06-02: WinUI Gallery Fluent 디자인 정합(참조: NapCat) — CommunityToolkit.WinUI.Controls.SettingsControls 8.2.251219 추가(WinAppSDK 1.8 호환). 디자인 토큰 리소스(Resources/Spacing.xaml=PageContentPadding/ContentMaxWidth/SideNavWidth, ControlStyles.xaml=CardStyle/Hero/Primary·SecondaryActionStyle/SettingsGroupHeaderStyle). 셸: 커스텀 TitleBar(ExtendsContentIntoTitleBar) + NavigationView 정교화(280 pane, 컨텐츠 보더 제거, Transparent). 설정/정보 페이지를 SettingsCard로, 작업그룹/트레이/정보/설정 전 페이지를 공통 레이아웃(ScrollViewer/PageContentPadding/MaxWidth/헤더)으로 통일. 기능/바인딩 불변(코드비하인드 미변경). 빌드 0/0, 테스트 80/80. (TitleBar 드래그/캡션 시각은 GUI 수동 확인 대상.)
- 2026-06-02: WinUIEx 2.9.1 재도입 — 메인 창을 WindowEx로 전환해 창 크기/위치 지속(PersistenceId="WorkGroupMain")·최소 크기(800×560) 관리. Mica는 표준 SystemBackdrop 유지(WinUIEx 자체 Backdrop은 CS0618 deprecated). 트레이 종료 시 `_window.Close()`로 persistence 저장 보장(닫기→트레이 숨김 구조에서 Window.Closed 발생 시점 확보). 트레이는 WinUIEx 미지원이라 Win32 Shell_NotifyIcon 유지. 빌드 0/0, 테스트 80/80.
- 2026-06-02: UI 전면 개편(Plan 3, T1~T8) — NavigationView 셸(MainShell: 작업 그룹/트레이 메뉴/설정/정보) + Fluent(Mica 백드롭). 설정 페이지(자동 시작 토글 + 테마 시스템/다크/라이트, ThemeService LocalSettings 영속). 정보 페이지(버전 + 오픈소스 라이선스 목록). 트레이 메뉴 placeholder. 그룹 추가/수정은 ContentDialog(설치앱 체크박스 선택 + 아이콘 설정 + 미리보기). 작업 그룹 페이지(그룹 아이콘 + 2라인[이름/멤버 아이콘] + 수정/삭제 아이콘 버튼 + 드래그 핀 + 삭제 확인). 단일 MainPage/MainViewModel 제거. 빌드 0/0, 테스트 80/80.
- 2026-06-02: T12 — 자동 시작(StartupTask + 토글) + Win32 트레이 아이콘(Shell_NotifyIcon, 열기/종료) + 실행 별칭 `WorkGroupSpike.exe`→`WorkGroup.exe` finalize + README/notes 갱신. 빌드 0/0.
- 2026-06-02: T11 — 정식 팝업 런처(GroupPopupWindow, 아이콘 그리드 + 클릭 실행) + IAppLauncher + AppIconLoader. spike 팝업 제거.
- 2026-06-02: T9+T10 — 메인 관리 화면(MainViewModel/MainPage) + 그룹 드래그 등록(검증된 패턴) + DI 조립(ServiceConfiguration/WorkGroupPaths).
- 2026-06-01: T2 배포 수정 — 앱을 MSIX 패키지 모드로 전환(`WindowsPackageType=None` 제거). VS F5 배포 시 DEP1700(.appxrecipe 없음) 해결. 빌드 0/0, .appxrecipe 생성 확인.
- 2026-06-01: T1b — 레이어 프로젝트(Domain/Application/Infrastructure) + 테스트 2개(xUnit) 생성, 프로젝트 참조 연결, 솔루션 등록. AGENTS.md/README.md/notes.md 작성. 전체 빌드 0/0, 테스트 2/2 통과.
- 2026-06-01: T1a — WinUI3 앱(`WorkGroup.App`, net10.0-windows10.0.19041.0) + `WorkGroup.slnx` 스캐폴딩. `dotnet build WorkGroup.slnx` 0/0 검증. git 저장소 초기화.
