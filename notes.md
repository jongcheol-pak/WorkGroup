# 작업 노트

## 최근 변경
- 2026-06-02: T12 — 자동 시작(StartupTask + 토글) + Win32 트레이 아이콘(Shell_NotifyIcon, 열기/종료) + 실행 별칭 `WorkGroupSpike.exe`→`WorkGroup.exe` finalize + README/notes 갱신. 빌드 0/0.
- 2026-06-02: T11 — 정식 팝업 런처(GroupPopupWindow, 아이콘 그리드 + 클릭 실행) + IAppLauncher + AppIconLoader. spike 팝업 제거.
- 2026-06-02: T9+T10 — 메인 관리 화면(MainViewModel/MainPage) + 그룹 드래그 등록(검증된 패턴) + DI 조립(ServiceConfiguration/WorkGroupPaths).
- 2026-06-01: T2 배포 수정 — 앱을 MSIX 패키지 모드로 전환(`WindowsPackageType=None` 제거). VS F5 배포 시 DEP1700(.appxrecipe 없음) 해결. 빌드 0/0, .appxrecipe 생성 확인.
- 2026-06-01: T1b — 레이어 프로젝트(Domain/Application/Infrastructure) + 테스트 2개(xUnit) 생성, 프로젝트 참조 연결, 솔루션 등록. AGENTS.md/README.md/notes.md 작성. 전체 빌드 0/0, 테스트 2/2 통과.
- 2026-06-01: T1a — WinUI3 앱(`WorkGroup.App`, net10.0-windows10.0.19041.0) + `WorkGroup.slnx` 스캐폴딩. `dotnet build WorkGroup.slnx` 0/0 검증. git 저장소 초기화.
