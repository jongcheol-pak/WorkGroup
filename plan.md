# Plan: WorkGroup — 전체 UI 개편 (NavigationView 셸 + Fluent)

> **이전 계획(T1a~T12, 코어·인프라·런처)은 완료되어 git 이력에 보존됨.** 본 plan은 UI 레이어 전면 재구성(Plan 3)이며, 기존 Domain/Application/Infrastructure 서비스(IAppInventory/IGroupAppService/IShortcutService/IIconService/StartupService)는 **변경 없이 재사용**한다.

## Goal
단일 `MainPage`(3분할: 설치앱·그룹편집·그룹목록)를 **좌측 메뉴 + 우측 컨텐츠(NavigationView)** 레이아웃으로 재구성하고, WinUI 3 Gallery / Fluent 디자인 가이드(D17)를 적용한다. 메뉴는 **작업 그룹 / 트레이 메뉴 / 설정 / 정보** 4개이며, 그룹 추가·수정은 모달 다이얼로그(설치 앱 체크박스 선택 + 아이콘 설정)로 처리한다.

## Out of Scope
- Domain/Application/Infrastructure 로직 변경(서비스 인터페이스·구현 그대로 재사용). 인벤토리/아이콘/.lnk/영속화 로직 수정 없음.
- "트레이 메뉴" 페이지의 실제 기능(작업 그룹 UI 완료 후 별도 plan). 본 plan에서는 **placeholder만**.
- 팝업 런처(`GroupPopupWindow`) **동작·레이아웃** 변경 — 기존 그대로 유지(저장된 테마 적용만 추가, DU5).
- 작업 표시줄 드래그 핀 **방식** 변경(기존 검증된 로직을 새 페이지로 이식만; 알고리즘 동일).
- 새 의존성 추가(승인된 패키지 내에서만 구현).
- 다국어/로컬라이즈, 접근성 전수 점검(표준 컨트롤로 자연 확보되는 범위만).

## Investigation Log
- `MainPage.xaml(.cs)` / `MainViewModel.cs` Read → 현재 3분할 단일 화면. 그룹 편집 인라인, 아이콘은 ComboBox, 자동시작 토글은 헤더, 드래그 핀은 우측 그룹 목록.
- grep `MainViewModel|MainPage` (src, obj 제외) → 참조처 전수: `App.xaml.cs:96`(Navigate), `ServiceConfiguration.cs:57`(AddTransient), 자기 파일들. **외부 참조 없음** → 제거 시 영향 국소.
- grep `StartupService|GetShortcutPath|App.MainWindow` → `StartupService`는 `MainViewModel`만 사용(→ SettingsViewModel로 이동), `GetShortcutPath`/`App.MainWindow`는 `MainPage.xaml.cs`만 사용(→ WorkGroupsPage/GroupEditDialog로 이동).
- 전체 `*.csproj` PackageReference 수집 → 배포 런타임 의존성: CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection/Logging(+Abstractions), Microsoft.WindowsAppSDK, Microsoft.Web.WebView2, Microsoft.Windows.SDK.BuildTools, Microsoft.Windows.CsWin32. 테스트 전용(xunit 등)은 배포 제외 → 라이선스 목록에서 제외.
- `WorkGroupPaths`(Infrastructure, App에서 참조 가능) → `IconsDirectory` = `%USERPROFILE%\WorkGroup\Icons`. 그룹 .ico는 `{IconsDirectory}\{groupId}.ico`(IIconService 출력 규칙).
- `AppIconLoader`(App/Services) → AppEntry 아이콘을 ImageSource로 비동기 로드(셸 썸네일/이미지). 멤버 앱 미니아이콘 표시에 재사용 가능.
- `App.xaml`/`App.xaml.cs` → 루트 Frame이 `MainPage`로 navigate. 창에 SystemBackdrop 미설정(현재 Mica 미적용).
- WinUI 런타임 테마: `Application.RequestedTheme`는 시작 시에만 설정 가능 → 런타임 전환은 루트 `FrameworkElement.RequestedTheme`로 처리해야 함(확인 필요는 수동 검증).

## Risks & Unknowns
| 위험 | 영향 | 완화책 |
|---|---|---|
| ContentDialog가 XamlRoot 미지정 시 표시 실패(WinUI) | 그룹 추가/수정 불능 | DU2: 다이얼로그 `XamlRoot = 호출 페이지.XamlRoot` 명시. T5 수동 확인. |
| .ico를 `Image.Source`(BitmapImage)로 로드 시 프레임 선택/표시 품질 | 그룹 아이콘 흐림/미표시 | DU7: 실패 시 IconSource 기반 폴백(내장색=단색 사각형, 멤버앱=첫 앱 아이콘). T6 수동 확인. |
| 런타임 테마 전환이 일부 표면(Mica/팝업 창)에 즉시 반영 안 될 수 있음 | 테마 불일치 | DU5/DU6: 루트 RequestedTheme + Mica는 다음 창부터. 팝업 창은 별 프로세스라 영향 적음. T2 수동 확인. |
| 설치 앱 인벤토리 로딩이 느려 다이얼로그가 멈춘 듯 보임 | UX 저하 | DU8: 다이얼로그 오픈 시 lazy 로드 + ProgressRing. |
| Microsoft 런타임 패키지를 "MIT"로 오표기 | 라이선스 부정확 | DU4: WindowsAppSDK/WebView2/SDK.BuildTools는 **독점(Microsoft) 라이선스**로 정확히 표기. DU4 확정값을 그대로 사용(추가 조사 없이 표기만 검증). |

## Impact Analysis
UI 레이어 한정 재구성. 변경/제거 대상 심볼의 사용처를 전수 확인(Investigation Log grep).

### 4-A. 제거·이동 대상과 사용처
| 심볼 | 현재 사용처(전수) | 처리 |
|---|---|---|
| `MainViewModel` | `MainPage.xaml.cs:24`, `ServiceConfiguration.cs:57` | **제거**. 기능 분리: 그룹 목록·추가·수정·삭제 → `WorkGroupsViewModel`/`GroupEditViewModel`; 자동시작 → `SettingsViewModel`. |
| `MainPage` | `App.xaml.cs:96`(Navigate), `MainPage.xaml` | **제거**. 루트 Frame은 `MainShell`로 navigate. |
| `StartupService` | `MainViewModel`(제거됨), `ServiceConfiguration.cs:54`(등록) | **유지**. 사용처를 `SettingsViewModel`로 이동. 등록 그대로. |
| `IShortcutService.GetShortcutPath` | `MainPage.xaml.cs:64`(드래그) | 사용처를 `WorkGroupsPage.xaml.cs`로 이동(시그니처 변경 없음). |
| `App.MainWindow` | `MainPage.xaml.cs:113`(FileOpenPicker HWND) | 사용처를 `GroupEditDialog.xaml.cs`로 이동. |

### 4-B. 계약·직렬화 변경
- **없음.** 그룹 직렬화(groups.json), .lnk 인자(`--group {id}`), IIconService 출력 경로 규칙 모두 불변. 새 테마 설정은 `ApplicationData.LocalSettings`의 신규 키 `AppTheme`(문자열) — 기존 데이터 영향 없음.

### 4-C. 영향 받는 테스트
- 제거 대상 `MainViewModel`/`MainPage`에 대한 **단위 테스트 없음**(UI는 수동 검증 — 기존 Verification Strategy). 신규 UI도 수동 검증.
- Domain/Application 테스트(80건)는 UI 변경과 무관 → 회귀 없음(빌드/테스트로 확인).

### Verified by
- grep 전수(obj 제외)로 `MainViewModel`/`MainPage`/이동 심볼의 외부 참조가 위 표로 한정됨을 확인.

## Decisions

### DU1. 셸 레이아웃 — NavigationView (좌측 메뉴)
- **Chosen**: `NavigationView`(PaneDisplayMode 좌측). **MenuItems(상단)** = 작업 그룹, 트레이 메뉴. **FooterMenuItems(하단)** = 설정, 정보. 내부 `Frame`로 컨텐츠 전환. 시작 선택 = 작업 그룹.
- **Rationale**: 요구사항(좌측 메뉴 + 우측 컨텐츠) + D17(표준 컨트롤). "정보는 하단" 요구 → footer. 설정도 Fluent 관례상 footer.
- **Source**: 사용자 요구 + WinUI Gallery NavigationView 패턴.

### DU2. 그룹 추가/수정 = ContentDialog 재사용
- **Chosen**: 단일 `GroupEditDialog`(ContentDialog)를 **신규/편집 모드**로 재사용. 편집은 기존 값 프리필. `XamlRoot`는 호출 페이지의 것으로 지정.
- **Rationale**: Fluent 표준 모달, 요구사항의 "팝업". 추가·수정 UI 동일 → 재사용으로 중복 제거.
- **Source**: 사용자 확인(라이선스/아이콘 질문 라운드에서 다이얼로그 전제 합의), WinUI 표준.

### DU3. 아이콘 설정 옵션 — 기존 세트 유지
- **Chosen**: 현재 옵션(기본/빨강/초록/주황/보라/첫 멤버 앱/이미지 선택...)을 **그대로 유지**하고, 다이얼로그 상단에 **미리보기 + 선택 UI**로 배치. 도메인 `IconSource`/`IIconService` 변경 없음.
- **Source**: 사용자 확정("기존 옵션 유지").

### DU4. 라이선스 표시 — 이름+종류+링크(정적 큐레이션)
- **Chosen**: 배포 런타임 의존성만 **이름 + 라이선스 종류 + 프로젝트 URL** 목록으로. 전체 본문은 링크로 연결(앱 내 본문 미동봉). 데이터는 코드 내 정적 큐레이션(`LicenseCatalog`).
- **정확성 규칙**: CommunityToolkit.Mvvm/Microsoft.Extensions.*/Microsoft.Windows.CsWin32 = **MIT**. Microsoft.WindowsAppSDK/Microsoft.Web.WebView2/Microsoft.Windows.SDK.BuildTools = **독점(Microsoft Software License)** — MIT로 표기 금지. 테스트 전용 패키지(xunit 등) 제외.
- **Source**: 사용자 확정("이름+종류+링크"). 라이선스 종류는 각 패키지 실제 라이선스로 확정.

### DU5. 테마 전환 — 시스템/다크/라이트
- **Chosen**: 설정 페이지에서 3택. 적용 = 루트 `FrameworkElement.RequestedTheme`(시스템=`ElementTheme.Default`, 다크=`Dark`, 라이트=`Light`). 지속 = `ApplicationData.Current.LocalSettings["AppTheme"]`. 창 생성 시 저장값을 읽어 적용.
- **팝업 창 일관성(M1)**: `GroupPopupWindow`도 **별 프로세스 기동 시 저장된 테마를 읽어** 루트 `Border`에 `RequestedTheme` 적용한다(다크 설정인데 팝업만 라이트로 뜨는 불일치 방지). 그 외 팝업 동작/레이아웃은 불변(Out of Scope).
- **Rationale**: `Application.RequestedTheme`는 시작 시 1회만 가능 → 런타임 전환은 루트 요소 RequestedTheme로. LocalSettings는 단일 enum 영속에 적합(MSIX).
- **Source**: 사용자 요구. WinUI 테마 API.

### DU6. 창 백드롭 — Mica
- **Chosen**: 메인 창에 `MicaBackdrop` 적용(`_window.SystemBackdrop = new MicaBackdrop()`). 라이트/다크 자동 대응.
- **Source**: D17(Fluent 표면).

### DU7. 그룹 목록 항목 표시
- **Chosen**: 항목 = [그룹 아이콘] + [2라인: 1=그룹 이름, 2=멤버 앱 아이콘 가로 나열] + [수정 아이콘버튼][삭제 아이콘버튼(아이콘 전용)]. 그룹 아이콘 = `{IconsDirectory}\{groupId}.ico` 로드(`GroupIconLoader`), 실패 시 IconSource 기반 폴백. 멤버 아이콘 = `AppIconLoader` 재사용, **상한 8개 + 초과 시 "+N"**.
- **Source**: 요구사항(그룹 아이콘 + 2라인). 상한은 레이아웃 보호용 기본값.

### DU8. 설치 앱 로딩 시점
- **Chosen**: 다이얼로그 오픈 시 `IAppInventory.GetInstalledAppsAsync()` **lazy 로드** + ProgressRing. 각 항목 아이콘은 비동기(가상화 리스트).
- **Source**: 인벤토리 로딩 지연 대비(Risks).

### DU9. MainPage/MainViewModel 제거(구조 변경 — 승인 대상)
- **Chosen**: 기능을 페이지/뷰모델로 분리 후 제거. 본 plan 승인이 제거 승인을 포함한다.
- **Source**: 4-A 영향 분석(외부 참조 없음).

## Tasks

> 공통 완료 기준(CLAUDE.md): 추가/수정 코드 **한글 주석**, 파일 **1500라인 내외**, **UTF-8(BOM 없음)**, 빌드 0 경고/0 에러. UI는 D17(Fluent·표준 컨트롤·라이트/다크) 적용. 모든 페이지/다이얼로그는 `x:Bind` 기반 MVVM.

- [ ] **T1. 앱 셸(NavigationView) + 4개 stub 페이지** *(~2h)*
  - **Type**: C
  - **Acceptance**: `MainShell`이 NavigationView(상단 작업그룹/트레이메뉴, 하단 설정/정보, `IsSettingsVisible=false`)로 표시되고 내부 `Frame`로 4개 **빈 stub 페이지**가 전환된다(수동). 시작 선택 = 작업 그룹. 빌드 0/0. (App 연결·테마·Mica는 T2.)
  - **stub 규칙(B2)**: 4개 stub 페이지의 코드비하인드는 **VM을 resolve하지 않는다**(빈 `InitializeComponent`만). VM resolve 추가는 각 채움 task(T3·T4·T5·T7)에서 동반 수행 → T1 단독으로 navigate 시 DI 미등록 예외가 나지 않음.
  - **Files**:
    - 주: `src/WorkGroup.App/Views/MainShell.xaml(.cs)`
    - stub(빈 Page, VM resolve 없음): `src/WorkGroup.App/Views/WorkGroupsPage.xaml(.cs)`, `TrayMenuPage.xaml(.cs)`, `SettingsPage.xaml(.cs)`, `AboutPage.xaml(.cs)`
  - **Edge Cases**: NavigationView 선택 변경 시 동일 페이지 재navigate 방지(현재 Tag 비교). footer 항목(설정/정보) 선택도 동일 핸들러로 처리.
  - **Halt Forecast**: "메뉴→페이지 매핑?" → MenuItem `Tag`에 페이지 타입 키, SelectionChanged에서 분기. "settings item?" → `IsSettingsVisible=false`로 끄고 footer에 직접 추가.
  - **Depends on**: -

- [ ] **T2. ThemeService + App 통합(MainShell 연결·Mica·테마 적용)** *(~2.5h)*
  - **Type**: D
  - **Acceptance**: `App.xaml.cs`가 루트 Frame을 `MainShell`로 navigate하고, 메인 창에 **Mica 백드롭** + 시작 시 저장된 **테마**가 적용된다. `GroupPopupWindow`도 저장된 테마를 적용한다(M1). `ThemeService`가 LocalSettings의 `AppTheme`를 읽고/쓰며 대상 루트 요소의 `RequestedTheme`를 설정한다. 빌드 0/0.
  - **적용 위치 명시(B1)**:
    - Mica: `ShowMainWindow`의 **`_window = new Window()` 최초 생성 직후(현 L83 부근) 1회** `_window.SystemBackdrop = new MicaBackdrop()` 설정(재사용 호출마다 재설정 금지).
    - 테마: `ThemeService.Apply(FrameworkElement root)`가 `root.RequestedTheme` 설정. 메인 창은 **`rootFrame`**(현 L91-96에서 생성·content 설정되는 그 Frame)에 적용. 팝업은 `GroupPopupWindow` 생성자에서 루트 `Border`에 적용.
    - 팝업 분기(`OnLaunched` L46)·트레이 숨김(`OnMainWindowClosing`)·활성화 로직은 **변경하지 않는다**(테마/Mica 추가만).
  - **Files**:
    - 주: `src/WorkGroup.App/Services/ThemeService.cs`
    - 수정: `src/WorkGroup.App/App.xaml.cs`(Navigate→MainShell, Mica 1회, rootFrame 테마), `src/WorkGroup.App/Views/GroupPopupWindow.xaml.cs`(생성자 테마 적용), `src/WorkGroup.App/ServiceConfiguration.cs`(ThemeService 등록)
  - **Edge Cases**: LocalSettings에 `AppTheme` 키 없음→`System`(`ElementTheme.Default`). 알 수 없는 값→`System`. 비패키지 실행으로 LocalSettings 접근 실패→`System` 폴백(예외 흡수).
  - **Halt Forecast**: "테마 적용 대상?" → 위 B1 명시(rootFrame / 팝업 Border). "Mica API?" → `Microsoft.UI.Xaml.Media.MicaBackdrop`. "LocalSettings 키 타입?" → 문자열 `"System"|"Dark"|"Light"`.
  - **Depends on**: T1

- [ ] **T3. 설정 페이지(자동 시작 + 테마)** *(~2.5h)*
  - **Type**: C
  - **Acceptance**: 설정 페이지에서 (1) "로그인 시 자동 시작" 토글이 `StartupService` 현재 상태를 반영/변경하고(정책 거부 시 되돌림), (2) 테마 3택(시스템/다크/라이트)이 `ThemeService`로 즉시 적용+영속된다(수동: 재시작 후 유지). 페이지가 `SettingsViewModel`을 resolve(B2). 빌드 0/0.
  - **로드 시점 명시(M3)**: 초기 상태(자동시작 on/off, 현재 테마 선택)는 **페이지 `Loaded` 이벤트에서 `suppress` 플래그를 켜고 로드**한 뒤 끈다 → 진입 즉시 토글/테마 변경 핸들러가 오발동하지 않는다(기존 `MainViewModel._suppressAutoStart` 패턴 이식).
  - **Files**:
    - 주: `src/WorkGroup.App/Views/SettingsPage.xaml(.cs)`, `src/WorkGroup.App/ViewModels/SettingsViewModel.cs`
    - 수정: `src/WorkGroup.App/ServiceConfiguration.cs`(SettingsViewModel 등록)
  - **Edge Cases**: 자동시작 정책 거부→실제 상태로 되돌리고 안내(기존 `MainViewModel.OnAutoStartEnabledChanged` 이식). 테마/자동시작 초기 로드 중 set→suppress로 무시.
  - **Halt Forecast**: "자동시작 로직?" → 기존 `MainViewModel` 이식. "테마 선택 UI?" → `RadioButtons`(표준). "초기화 시점?" → M3(Loaded + suppress).
  - **Depends on**: T2

- [ ] **T4. 정보 페이지(버전 + 라이선스 목록)** *(~2h)*
  - **Type**: C
  - **Acceptance**: 정보 페이지에 앱 이름 + 버전(`Package.Current.Id.Version` → `Major.Minor.Build.Revision`)이 표시되고, 오픈소스 라이선스 목록(이름+종류+링크, DU4)이 리스트로 표시되며 링크 클릭 시 브라우저로 열린다(수동). 페이지가 `AboutViewModel`을 resolve(B2). 빌드 0/0.
  - **Files**:
    - 주: `src/WorkGroup.App/Views/AboutPage.xaml(.cs)`, `src/WorkGroup.App/ViewModels/AboutViewModel.cs`, `src/WorkGroup.App/Services/LicenseCatalog.cs`(정적 라이선스 데이터)
    - 수정: `src/WorkGroup.App/ServiceConfiguration.cs`(AboutViewModel 등록)
  - **Edge Cases**: 비패키지 실행 등 `Package.Current` 접근 실패→어셈블리 버전 폴백 + 예외 흡수. 링크 열기 실패→무시(로깅).
  - **Halt Forecast**: "버전 출처?" → `Package.Current.Id.Version`. "라이선스 데이터?" → DU4 확정값(테스트 전용 제외, Microsoft 독점 정확 표기). "링크 열기?" → `HyperlinkButton NavigateUri`.
  - **Depends on**: T1

- [ ] **T5. 트레이 메뉴 페이지(placeholder)** *(~0.5h)*
  - **Type**: B
  - **Acceptance**: 트레이 메뉴 페이지에 "추후 추가 예정" 안내(InfoBar 또는 중앙 텍스트)가 표시된다(수동). 빌드 0/0.
  - **Files**: 주: `src/WorkGroup.App/Views/TrayMenuPage.xaml(.cs)`(T1 stub 채움; VM 없음)
  - **Edge Cases**: 없음(정적 안내).
  - **Halt Forecast**: 없음.
  - **Depends on**: T1

- [ ] **T6. 그룹 추가/수정 다이얼로그(설치앱 체크리스트 + 아이콘 설정)** *(~4h)*
  - **Type**: D
  - **Acceptance**: `GroupEditDialog`(ContentDialog)가 **상단**에 그룹 이름 + 아이콘 설정(DU3 기존 옵션 + 미리보기 + 이미지 선택 FileOpenPicker), **하단**에 설치 앱 목록(아이콘 + 이름 + 체크박스, 검색)을 표시한다. 신규/편집 모드(편집=프리필) 지원. 확인 시 선택 앱·이름·아이콘으로 `AppGroup`을 만들어 `IGroupAppService.SaveAsync` 호출, 성공 시 닫힘(수동). 빌드 0/0.
  - **Files**:
    - 주: `src/WorkGroup.App/Views/GroupEditDialog.xaml(.cs)`, `src/WorkGroup.App/ViewModels/GroupEditViewModel.cs`, `src/WorkGroup.App/ViewModels/SelectableAppItem.cs`
    - 수정: `src/WorkGroup.App/ServiceConfiguration.cs`(GroupEditViewModel 등록 — 또는 페이지에서 생성)
  - **Edge Cases**: 그룹명 미입력→확인 비활성/저장 거부. 인벤토리 로딩 중→ProgressRing(DU8). 빈 검색 결과→빈 상태 안내. 이미지 선택 취소→기존 아이콘 유지. 저장 실패(Result.Fail)→다이얼로그 유지 + 메시지. 편집 시 기존 멤버 체크 상태 복원.
  - **Halt Forecast**: "XamlRoot?" → DU2(호출 페이지). "이미지 선택 HWND?" → 기존 `App.MainWindow` 이식. "아이콘 옵션?" → DU3(기존 이식). "앱 아이콘 로드?" → `AppIconLoader` 재사용(SelectableAppItem). "저장 흐름?" → 기존 `MainViewModel.SaveGroup`/`BuildIconSource`/`CreateNew` 로직 이식.
  - **Depends on**: T1

- [ ] **T7. 작업 그룹 페이지(그룹 목록 2라인 + 아이콘 버튼 + 추가 버튼 + 드래그 핀)** *(~4h)*
  - **Type**: D
  - **Acceptance**: 작업 그룹 페이지 **상단**에 "그룹 추가" 버튼(아이콘+라벨), 아래에 그룹 목록. 각 항목 = 그룹 아이콘 + 2라인(이름 / 멤버 앱 아이콘들, DU7) + **수정·삭제 아이콘 버튼(아이콘 전용)**. "그룹 추가"/수정 클릭 시 `GroupEditDialog`(신규/편집) 오픈, 삭제 시 `IGroupAppService.DeleteAsync`. 항목을 작업 표시줄로 **드래그하면 .lnk 핀**(기존 검증 로직 이식). 저장/삭제 후 목록 갱신(수동). 페이지가 `WorkGroupsViewModel`을 resolve(B2). 빌드 0/0.
  - **Files**:
    - 주: `src/WorkGroup.App/Views/WorkGroupsPage.xaml(.cs)`(T1 stub 채움), `src/WorkGroup.App/ViewModels/WorkGroupsViewModel.cs`, `src/WorkGroup.App/ViewModels/GroupListItem.cs`, `src/WorkGroup.App/Services/GroupIconLoader.cs`
    - 수정: `src/WorkGroup.App/ServiceConfiguration.cs`(WorkGroupsViewModel 등록)
  - **.ico 로드 구체화(M2)**: `GroupIconLoader`는 `{WorkGroupPaths.IconsDirectory}\{groupId}.ico`가 존재하면 `new BitmapImage(new Uri(path))`로 로드하고, **`BitmapImage.ImageFailed` 이벤트** 또는 파일 부재 시 IconSource 기반 폴백(내장색=단색 사각형 Brush, 멤버앱=첫 멤버 `AppIconLoader`)으로 대체한다. `DecodePixelWidth=32`로 표시 크기에 맞춰 디코드.
  - **Edge Cases**: .lnk 미생성 그룹 드래그→차단 + 안내(기존 로직). 그룹 0개→빈 상태 안내. 그룹 아이콘 .ico 없음/로드 실패→폴백(M2). 멤버 8개 초과→"+N". 드래그 취소→무동작. 삭제 후 편집 중이던 그룹이면 상태 정리.
  - **Halt Forecast**: "드래그 방식?" → 기존 `MainPage.OnGroupDragStarting`(임시복사+지연 SetDataProvider) 이식. "그룹 아이콘 로드?" → M2. "수정/삭제 아이콘?" → `SymbolIcon`(Edit/Delete).
  - **Depends on**: T6

- [ ] **T8. 정리(MainPage/MainViewModel 제거) + 문서 갱신** *(~1.5h)*
  - **Type**: C
  - **Acceptance**: `MainPage.xaml(.cs)`/`MainViewModel.cs` 제거, `ServiceConfiguration`에서 `MainViewModel` 등록(현 L57) 제거. **`grep -rn "MainPage\|MainViewModel" src/ ':!*/obj/*'` → 0 hit(M4)** 으로 잔여 참조 없음을 확정. 전체 빌드 0/0 + 기존 테스트 80건 통과. `README.md`(새 UI·메뉴 구조·테마·설정) 및 `notes.md` 갱신.
  - **Files**:
    - 제거: `src/WorkGroup.App/Views/MainPage.xaml(.cs)`, `src/WorkGroup.App/ViewModels/MainViewModel.cs`
    - 수정: `src/WorkGroup.App/ServiceConfiguration.cs`
    - 문서: `README.md`, `notes.md`
  - **Edge Cases**: 제거 후 빌드 깨짐→위 grep로 잔여 참조 확인 후 정리.
  - **Halt Forecast**: 없음(영향 4-A로 한정 + grep 0 hit로 확정).
  - **Depends on**: T3, T4, T5, T7

## Verification Strategy
- 빌드: `dotnet build WorkGroup.slnx` (0 경고/0 에러).
- 테스트: `dotnet test WorkGroup.slnx` (Domain/Application 80건 회귀 없음).
- 수동(GUI — 자율 실행 관찰 불가, 사용자 확인 필요):
  - T1: NavigationView 4메뉴(작업그룹/트레이메뉴/설정/정보) 전환.
  - T2: Mica 백드롭 + 시작 시 저장된 테마 적용 + 팝업 테마 일치.
  - T3: 자동시작 토글 동작 + 테마 3택 즉시 적용·재시작 유지.
  - T4: 버전 표시 + 라이선스 목록 + 링크 열림.
  - T6/T7: 그룹 추가 다이얼로그(앱 체크/아이콘) → 목록에 그룹아이콘+2라인 표시 → 수정/삭제 → 작업 표시줄 드래그 핀.

## Progress Log
<!-- implement-task가 2 task마다 갱신 -->

## Open Questions (모두 해결됨)
- [x] 자동 시작 토글 위치 → **설정 메뉴 신설**, 자동시작 토글 + 테마(시스템/다크/라이트) 포함(사용자).
- [x] 라이선스 표시 수준 → **이름+종류+링크**(사용자).
- [x] 아이콘 설정 UI → **기존 옵션 유지**(사용자).
