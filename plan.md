# Plan: WinUIEx 재도입 — 메인 창 관리(WindowEx)

> 이전 plan(UI 개편 Plan 3, T1~T8)은 완료되어 git 이력에 보존됨. 본 plan은 **WinUIEx 의존성 재도입 + 메인 창 관리 적용**(사용자 지시 B). 트레이/팝업은 범위 밖.

## Goal
이전에 제거됐던 **WinUIEx**(v2.9.1)를 다시 추가하고, 메인 창을 `WindowEx`로 전환해 (1) Mica 백드롭을 WinUIEx로 관리, (2) 창 크기/위치 지속(persistence), (3) 최소 크기 설정을 적용한다. 기존 동작(닫기→트레이 숨김, 저장된 테마 적용)은 **그대로 보존**한다.

## Out of Scope
- 트레이 아이콘 — WinUIEx는 시스템 트레이 미지원(검증됨). 기존 Win32 `Shell_NotifyIcon`(`TrayIconService`) 유지, 변경 없음.
- 팝업 창(`GroupPopupWindow`) — 기존 OverlappedPresenter + `TaskbarPopupPositioner` 유지, 변경 없음.
- 설정/정보/작업 그룹 페이지·다이얼로그·ViewModel — 변경 없음.
- 코어/인프라/Domain — 변경 없음.

## Investigation Log
- WebFetch(github.com/dotMorten/WinUIEx) → 최신 **v2.9.1**(2026-05-31), NuGet 패키지 id **`WinUIEx`**.
- WebFetch(nuget.org WinUIEx) → 대상 프레임워크 `net8.0-windows10.0.19041`(+net9/net10-windows 호환), 의존성 **`Microsoft.WindowsAppSDK.WinUI (>= 1.8.250906003)`**.
- WebFetch(WindowManager 문서) → `WindowManager.Get(window)` 또는 `WindowEx`에서 `PersistenceId`/`Backdrop`/`MinWidth`/`MinHeight` 사용. 예(문서 예시값, 실채택값은 DW3): `manager.Backdrop = new WinUIEx.MicaSystemBackdrop(); manager.PersistenceId = "..."; manager.MinWidth=640;`.
- WebFetch(WindowManager.cs 소스) → **persistence는 `Window_Closed`에서 `SavePersistence()` 호출로만 저장**(PositionChanged/SizeChanged는 저장 트리거 아님). 저장 위치 = `ApplicationData.Current.LocalSettings`의 `"WinUIEx"` 컨테이너, 키 `WindowPersistance_{PersistenceId}`. **닫기를 취소(Hide)하면 그 시점엔 저장 안 됨** → 본 앱은 트레이 종료 시 실제 `Window.Closed`가 발생하므로 그때 저장됨(아래 DW5).
- Read(App.xaml.cs) → `ShowMainWindow`에서 `_window = new Window()`(L84) + `_window.SystemBackdrop = new MicaBackdrop()`(L86, `Microsoft.UI.Xaml.Media`) + `AppWindow.Closing += OnMainWindowClosing`(L88, 닫기→Hide) + `ThemeService.Initialize(rootFrame)`(L98). 팝업 분기(L47~52)는 `_window = popup`로 별도(별 프로세스).
- grep(MicaBackdrop|SystemBackdrop|new Window\(|_window, obj 제외) → `MicaBackdrop`/`SystemBackdrop`은 **App.xaml.cs L86 1곳뿐**. `new Window()`도 App.xaml.cs L84 1곳. `_window`는 App.xaml.cs 내부 필드(외부 참조 없음).
- Read(WorkGroup.App.csproj) → `Microsoft.WindowsAppSDK Version="1.*"`(메타패키지). WinUIEx가 요구하는 `Microsoft.WindowsAppSDK.WinUI >= 1.8`은 1.8 메타패키지에 포함 → `1.*`가 1.8+로 해석되면 충족(T1 빌드로 실측).

## Risks & Unknowns
| 위험 | 영향 | 완화책 |
|---|---|---|
| `Microsoft.WindowsAppSDK 1.*`가 1.8 미만으로 해석되면 WinUIEx 2.9.1 복원 실패 | 빌드 실패 | T1 빌드 게이트. 실패 시 WinAppSDK 하한을 `[1.8,)`로 올리거나 WinUIEx 호환 하위 버전 선택(승인 후). |
| `WindowEx`가 기본 타이틀바/백드롭을 자체 적용해 기존 룩과 충돌 | 시각 불일치 | DW2에서 Backdrop 명시 설정. T1 수동 시각 확인. |
| persistence는 `Window.Closed`에서만 저장 + 닫기는 항상 Hide → 저장 기회가 종료뿐인데 `Exit()`가 `Closed`를 발생시킬지 미검증 | **persistence 0% 저장 가능(핵심 기능 불능)** | **DW5: 트레이 종료 시 `_window?.Close()`를 명시 호출**해 `Window.Closed`를 결정적으로 발생 → 저장 보장. `Exit()`의 `Closed` 발생 여부에 의존하지 않음. T1 수동검증 ②로 실측. |
| WindowEx 전환이 `_window`(Window? 필드)·`MainWindow`(Window?)·HWND 획득(GroupEditDialog)과 충돌 | 런타임 오류 | `WindowEx : Window`라 대입·HWND 획득 호환(검증). T1 빌드+수동. |

## Impact Analysis
메인 창 생성부 한정 변경. 외부 계약·시그니처 변경 없음.

### 4-A. 변경/영향 심볼(전수)
| 심볼 | 사용처(grep 전수) | 처리 |
|---|---|---|
| `new Window()`(App.xaml.cs:84) | App.xaml.cs 1곳 | `new WindowEx()`로 교체(로컬 변수 경유 후 `_window`에 대입). |
| `_window.SystemBackdrop = new MicaBackdrop()`(L86) | App.xaml.cs 1곳 | 제거 → WinUIEx `WindowEx.Backdrop = new MicaSystemBackdrop()`. |
| `using Microsoft.UI.Xaml.Media;`(L2) | grep상 MicaBackdrop가 유일 사용(파일 내 타 Media 심볼 없음) | 제거 후 **빌드 0/0으로 확정**(타 심볼 의존 시 빌드 에러로 즉시 드러남). `using WinUIEx;` 추가. |
| `_window`(Window? 필드) / `MainWindow`(static Window?) | App.xaml.cs 내부(팝업 L50 `_window=popup` 공유) + GroupEditDialog(App.MainWindow HWND) | **둘 다 `Window?` 타입 유지.** WindowEx 전용 멤버(Backdrop/PersistenceId/MinWidth/MinHeight)는 ShowMainWindow 내 **로컬 `WindowEx win` 변수**로만 설정 후 `_window = win` 대입. 팝업 분기(`_window = popup`)·HWND 획득(`WindowNative.GetWindowHandle(MainWindow)`)은 그대로 동작(WindowEx는 Window). |
| `GroupPopupWindow`(: Window) | 팝업 분기 `_window = popup` | **영향 없음**(WindowEx 미적용, `_window`가 Window? 유지라 대입 호환). |
| 트레이 종료 핸들러(`ExitRequested`, L71-76) | App.xaml.cs | **변경**: `Exit()` 앞에 `_window?.Close()` 추가(DW5 — persistence 저장 보장). |
| `OnMainWindowClosing`(AppWindow.Closing) / `ThemeService.Initialize(rootFrame)` | App.xaml.cs | **보존**(닫기→트레이 숨김·테마 적용 그대로). |

### 4-B. 계약·직렬화 변경
- 없음. WinUIEx가 `LocalSettings`의 `"WinUIEx"` 컨테이너에 창 상태를 새로 저장(신규 키, 기존 데이터 영향 0, 마이그레이션 불필요).

### 4-C. 영향 받는 테스트
- 없음(UI·창 생성은 단위 테스트 비대상, 기존 Verification대로 수동). 기존 80건은 무관 → 빌드/테스트로 회귀 확인.

### Verified by
- grep 전수로 MicaBackdrop/new Window/_window 사용처가 App.xaml.cs 내부로 한정됨을 확인.

## Decisions

### DW1. 적용 방식 — WindowEx 전환
- **Options**: A) 메인 창을 `WindowEx`로 전환 / B) 기존 `new Window()` 유지 + `WindowManager.Get(window)`로 관리
- **Chosen**: **A (WindowEx)** — 사용자 지시. `WindowEx`는 `PersistenceId`/`Backdrop`/`MinWidth`/`MinHeight`를 직접 노출하는 완성형. B는 동일 기능을 plain Window에 부착하는 대안(미채택, 기록만).
- **Source**: 사용자 선택("메인 창"), WinUIEx WindowManager 문서.

### DW2. Mica 백드롭 — WinUIEx로 일원화
- **Chosen**: 기존 `Microsoft.UI.Xaml.Media.MicaBackdrop` 제거 → `WindowEx.Backdrop = new WinUIEx.MicaSystemBackdrop()`. 두 백드롭 동시 설정 금지.
- **Source**: WinUIEx 문서 예제.

### DW3. persistence·최소 크기 파라미터
- **Chosen**: `PersistenceId = "WorkGroupMain"`, `MinWidth = 800`, `MinHeight = 560`(NavigationView 셸 + 컨텐츠가 좁아지지 않을 하한). PersistenceId는 Show 전에 설정해 복원이 적용되게 한다.
- **Source**: 합리적 기본값(셸 레이아웃 기준).

### DW4. WinUIEx 버전 — 2.9.1 고정
- **Chosen**: `<PackageReference Include="WinUIEx" Version="2.9.1" />`(검증된 최신). MS 패키지는 `1.*` 와일드카드지만 서드파티(CommunityToolkit.Mvvm 8.4.2처럼)는 핀.
- **Source**: nuget.org 실측.

### DW5. 닫기→트레이 숨김과 persistence 저장 보장 (B1 해소)
- **문제**: WinUIEx는 `Window.Closed`에서만 저장한다. 그런데 본 앱은 닫기(X)를 항상 취소·Hide하므로 정상 닫힘 경로에선 `Closed`가 안 난다. 트레이 "종료"는 현재 `_exiting=true → Exit()`만 호출하는데, **`Application.Exit()`가 메인 창의 `Window.Closed`를 발생시키는지는 미검증 가정**(발생 안 하면 persistence 0% 저장).
- **Chosen**: 닫기→트레이 숨김은 **보존**. 트레이 종료 핸들러를 **`_exiting=true` → `_window?.Close()`(WinUIEx `Window.Closed` 발생 → SavePersistence 실행) → `Exit()`** 순으로 변경해 저장을 **결정적으로 보장**한다. `_exiting=true` 상태이므로 `OnMainWindowClosing`이 닫힘을 취소하지 않아 창이 실제로 닫히고 `Closed`가 발생한다.
- **Rationale**: `Exit()`의 `Closed` 발생 여부에 의존하지 않고 명시적 `Close()`로 저장 시점을 확정. 숨김 상태의 창도 마지막 크기를 유지하므로 저장값 정확.
- **분기 제거**: "Exit가 Closed를 알아서 발생시킬 것"이라는 추정 제거. 명시 `Close()`로 고정.
- **Source**: WindowManager.cs 소스(Window_Closed→SavePersistence), App.xaml.cs(`_exiting`/`OnMainWindowClosing`).

## Tasks

> 공통: 한글 주석, UTF-8(BOM 없음), 빌드 `dotnet build WorkGroup.slnx` 0/0, 기존 테스트 80건 회귀 없음.

- [ ] **T1. WinUIEx 추가 + 메인 창 WindowEx 전환** *(~1.5h)*
  - **Type**: D (의존성 추가 + 창 라이프사이클)
  - **Acceptance**: `WorkGroup.App.csproj`에 `WinUIEx 2.9.1` 추가되어 복원·빌드 0/0. `App.xaml.cs` ShowMainWindow가 로컬 `WindowEx win`을 생성해 `Backdrop=new MicaSystemBackdrop()`, `PersistenceId="WorkGroupMain"`, `MinWidth=800`/`MinHeight=560` 설정 후 `_window=win` 대입(`_window`/`MainWindow`는 `Window?` 유지). 기존 `_window.SystemBackdrop=MicaBackdrop`는 제거. 트레이 `ExitRequested`를 `_exiting=true; _window?.Close(); _tray?.Dispose(); Exit();` 순으로 변경(DW5 — Close로 persistence 저장 보장). `OnMainWindowClosing`(닫기→트레이 숨김)·`ThemeService.Initialize(rootFrame)`·`MainShell` navigate 보존. 수동: ① Mica 유지 ② 창 크기 변경→트레이 종료→재실행 시 크기/위치 복원 ③ 800×560 이하 축소 불가 ④ 닫기(X)→트레이 숨김.
  - **Files**:
    - 주: `src/WorkGroup.App/WorkGroup.App.csproj`(PackageReference WinUIEx), `src/WorkGroup.App/App.xaml.cs`(ShowMainWindow: 로컬 WindowEx + Backdrop/PersistenceId/MinSize, ExitRequested: `_window?.Close()` 추가, `using` 정리)
  - **Edge Cases**: WinAppSDK가 1.8 미만으로 해석→복원/빌드 실패(Risks 완화). 첫 실행(저장값 없음)→기본 크기. persistence 저장 실패(비패키지 등)→무해(기본 크기). 팝업 분기(`_window = popup`)는 WindowEx 미적용(불변). **복원 크기 < MinSize**(WinUIEx 도입 전 저장값 없음이라 사실상 미발생, 있어도 WinUIEx가 MinSize로 클램프 기대 — 수동 확인). **저장 위치가 현재 화면 밖**(멀티모니터 변경)→WinUIEx 복원 동작 수동 확인(미보정 시 사용자가 이동 가능, 무해).
  - **Halt Forecast**: "WinAppSDK 버전 부족?" → Risks(하한 상향, 승인 후). "WindowEx 기본 백드롭 충돌?" → DW2 명시 설정. "MinWidth 단위?" → DIP(WinUIEx 처리). "`MicaSystemBackdrop`/`WindowEx.Backdrop`/`PersistenceId` 심볼명이 2.9.1과 불일치?" → 빌드 에러 기반으로 WinUIEx 2.9.1 실제 심볼로 교정(WindowManager 문서 예제 기준이나 컴파일로 확정).
  - **Depends on**: -

- [ ] **T2. 문서 갱신** *(~0.3h)*
  - **Type**: A
  - **Acceptance**: `notes.md`에 WinUIEx 재도입 항목 추가. `plan.md`(본 파일)는 결과 기록. `AGENTS.md`의 "승인된 의존성"에 WinUIEx가 이미 등재돼 있어 추가 변경 불필요(확인만). README는 기능 변화 없음(창 크기 지속은 내부 동작)이라 갱신 불요.
  - **Files**: 문서: `notes.md`
  - **Edge Cases**: 없음.
  - **Halt Forecast**: 없음.
  - **Depends on**: T1

## Verification Strategy
- 빌드: `dotnet build WorkGroup.slnx` → 0/0(WinUIEx 복원 포함).
- 테스트: `dotnet test WorkGroup.slnx` → 80/80 회귀 없음.
- 수동(GUI — 사용자 확인): ① 메인 창 Mica 유지 ② 창 크기 변경 → 트레이 종료 → 재실행 시 크기/위치 복원 ③ 최소 크기(800×560) 이하로 축소 불가 ④ 닫기(X) → 트레이로 숨김(종료 아님) ⑤ 테마 전환 정상.

## Progress Log
<!-- implement-task가 갱신 -->

## Open Questions (모두 해결됨)
- [x] 적용 범위 → **메인 창**(사용자). 트레이는 WinUIEx 미지원이라 제외.
- [x] 적용 방식 → **WindowEx 전환**(DW1, 사용자 지시).
