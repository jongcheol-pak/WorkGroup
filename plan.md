# Plan: WinUI Gallery Fluent 디자인 정합 (NapCat 참조 적용)

> 이전 plan들(UI 개편·WinUIEx)은 완료되어 git 이력에 보존. 본 plan은 참조 프로젝트 `D:\Personal Project\Windows\rest\NapCat`(WinUI Gallery Fluent 정석)의 디자인 시스템을 WorkGroup에 동일 적용한다. **기능/동작 변경 없음 — 순수 룩앤필.**

## Goal
WorkGroup의 셸·페이지를 NapCat과 동일한 Fluent 룩으로 맞춘다: (1) 디자인 토큰 리소스 딕셔너리, (2) 커스텀 TitleBar(ExtendsContentIntoTitleBar), (3) NavigationView 정교화(280 pane·컨텐츠 보더 제거·FontIcon), (4) 공통 페이지 레이아웃(ScrollViewer+PageContentPadding+MaxWidth+헤더), (5) CardStyle 카드 + CommunityToolkit SettingsCard.

## Out of Scope
- 기능/동작 변경: 그룹 CRUD, 드래그 핀, 테마 전환, 자동시작, 트레이(Win32), 팝업 런처(`GroupPopupWindow`), WinUIEx WindowEx/persistence/Mica — 모두 불변.
- ViewModel/Service/Domain 로직 — 변경 없음(XAML 재구성 시 기존 x:Bind·이벤트 핸들러 시그니처 유지).
- WinAppSDK 버전 상향(NapCat 2.1.3) — **불요**(검증: TitleBar는 1.7+, SettingsControls 8.2는 1.8 지원). 현재 1.8 유지.
- NapCat 앱 전용 리소스(Colors.xaml 상태/위험색, Brushes.xaml 커스텀 테마, Typography.xaml 타이머 스타일) — 표준 ThemeResource로 대체, 이식 안 함.
- `GroupEditDialog` 내부 — 이미 Fluent ContentDialog. 본 plan에서 토큰 정합 외 재구성 안 함.

## Investigation Log
- Explore(NapCat 전수) + Read(App.xaml, Resources/Spacing.xaml, Resources/ControlStyles.xaml, Views/MainPage.xaml) → 디자인 토큰·CardStyle·NavigationView 구조 확보(아래 Decisions에 정확값).
- grep(NapCat csproj) → `CommunityToolkit.WinUI.Controls.SettingsControls 8.2.251219`, `Microsoft.WindowsAppSDK 2.1.3`, `WinUIEx 2.*`, TFM `net10.0-windows10.0.19041.0`(WorkGroup과 동일).
- WebSearch(TitleBar) → `Microsoft.UI.Xaml.Controls.TitleBar`는 **WinAppSDK 1.7 도입**(1.6 없음). WorkGroup 1.8 → 사용 가능.
- WebSearch(SettingsControls 8.2) → 8.2.251219는 **WinAppSDK 1.8 지원 첫 공식 빌드**(`Microsoft.WindowsAppSDK.WinUI` 의존).
- **`dotnet list package --include-transitive`(실측, B1/B2 정정)** → WorkGroup `Microsoft.WindowsAppSDK 1.*`는 **1.8.260508005**로 해석되고, 전이 의존으로 **`Microsoft.WindowsAppSDK.WinUI 1.8.260505002`가 이미 포함**(1.8부터 meta가 `.WinUI` 분리 패키지를 견인). → SettingsControls 8.2가 요구하는 `.WinUI >= 1.8` 충족. (NapCat의 2.1.3은 근거 아님 — WorkGroup 실측 버전이 근거.) **최종 확정은 T1 복원·빌드 게이트.**
- **Resources 포함 방식(B3 정정)**: NapCat은 `Resources\*.xaml`을 **명시적 `<Page Update= Generator=MSBuild:Compile>`로 등록**(자동 글로빙 의존 아님). → WorkGroup도 T1에서 신규 `Resources\*.xaml`을 **명시적 `<Page>`로 등록**해 컴파일·패키징 보장(런타임 StaticResource 미해석 방지).
- grep(NapCat MainPage.xaml.cs, App.xaml.cs) → **SetTitleBar 호출 없음.** TitleBar 컨트롤은 창의 `ExtendsContentIntoTitleBar=true`와 **자동 동기화**(NapCat 주석·코드 확인, 단 NapCat=2.1.3). 창 아이콘은 별도 `AppWindow.SetIcon`(선택).
- Read(WorkGroup App.xaml/App.xaml.cs/MainShell.xaml/각 페이지) → 현재 SymbolIcon 셸(작업그룹=AllApps/트레이=List/설정=Setting/정보=Help, MenuItems 2 + FooterMenuItems 2 분리), 페이지별 개별 Padding(16/24)·MaxWidth(720/760), 표준 ThemeResource 브러시 이미 사용 중. App.xaml에 템플릿 잔여 리소스(Primary/MyLabel/Action 등) 존재.

## Risks & Unknowns
| 위험 | 영향 | 완화책 |
|---|---|---|
| SettingsControls 8.2가 WorkGroup에서 복원·빌드 실패 | 빌드 실패 | 실측 근거: WorkGroup이 WinAppSDK **1.8.260508005** + `.WinUI 1.8.260505002` 보유(SettingsControls 8.2 요구 `.WinUI>=1.8` 충족). **T1 복원·빌드 게이트로 최종 확정.** 실패 시 호환 하위 버전(8.1.x) 또는 SDK 조정(승인 후). |
| 신규 Resources/*.xaml이 컴파일/패키징 누락 → 런타임 StaticResource 미해석(빌드는 통과) | 자율 루프 멈춤 | B3 정정: T1에서 **명시적 `<Page>` 등록**(NapCat 동일). 빌드 산출물에 `.g.cs`/`.xbf` 생성 확인. |
| WinUIEx WindowEx의 deprecated Window.Icon이 XamlTypeInfo.g.cs에서 CS0612 경고 유발(TitleBar/Icon 경로 도입 시) | "빌드 0/0" 게이트 깨짐 | M1: T2에서 CS0612 발생 시 `<NoWarn>CS0612</NoWarn>`(csproj, NapCat 동일) 추가 — 단 직전 WinUIEx 작업에서 WindowEx 사용에도 미발생했으므로 발생 여부부터 확인. |
| ExtendsContentIntoTitleBar + 기존 닫기→트레이 숨김/재표시와 충돌 | 타이틀바 깨짐 | T2 수동 확인. 창 재사용 구조는 불변, 속성만 추가. |
| TitleBar 컨트롤이 SetTitleBar 없이 자동 동기 안 될 가능성 | 드래그/캡션버튼 영역 오작동 | NapCat 실증(자동 동기). 안 되면 `Window.SetTitleBar(titleBar)` 추가(T2 Halt Forecast). |
| FontIcon 글리프 코드 오타 → 빈 네모 | 아이콘 안 보임(빌드는 통과) | 표준 Segoe Fluent 글리프 사용(DG5). T2 수동 확인으로 검출. |
| ResourceDictionary StaticResource 키 해석 순서(병합 딕셔너리 간) | 런타임/빌드 오류 | ControlStyles.xaml이 Spacing.xaml을 자체 머지(NapCat 패턴). 빌드로 확정. |

## Impact Analysis
순수 UI(XAML/리소스/창 속성) 변경. ViewModel/Service/Domain 시그니처 불변.

### 4-A. 변경 파일과 보존 계약
| 파일 | 변경 | 보존(코드비하인드 계약) |
|---|---|---|
| `App.xaml` | MergedDictionaries에 Spacing/ControlStyles 추가 | 기존 잔여 리소스(Primary 등) 유지(범위 밖). |
| `App.xaml.cs` | WindowEx 초기화에 `ExtendsContentIntoTitleBar = true` 1줄 추가 | 나머지(Mica/PersistenceId/MinSize/트레이/테마) 불변. |
| `Views/MainShell.xaml` | Grid(row0 TitleBar + row1 NavigationView 정교화 + FontIcon) | **x:Name `Nav`·`ContentFrame` 유지**(MainShell.xaml.cs가 참조), `Loaded`/`SelectionChanged` 핸들러명 유지, MenuItem `Tag`(WorkGroups/TrayMenu/Settings/About) 유지. |
| `Views/WorkGroupsPage.xaml` | 공통 레이아웃 + CardStyle | **이벤트 핸들러(OnAddClick/OnEditClick/OnDeleteClick/OnGroupDragStarting), ListView `CanDragItems`/`DragItemsStarting`, x:Bind(ViewModel.*) 유지.** |
| `Views/SettingsPage.xaml` | SettingsCard 레이아웃 | x:Bind(AutoStartEnabled/ThemeIndex/HasStatus/StatusMessage) 유지. |
| `Views/AboutPage.xaml` | 공통 레이아웃 + SettingsCard/카드 | x:Bind(AppName/Version/Licenses), DataTemplate `svc:LicenseInfo`(Name/License/Link) 유지. |
| `Views/TrayMenuPage.xaml` | 공통 레이아웃 | 정적(바인딩 없음). |
| `WorkGroup.App.csproj` | SettingsControls PackageReference + Resources/*.xaml을 **명시적 `<Page Update Generator="MSBuild:Compile"/>` 등록**(B3 — 자동 글로빙 의존 안 함) | 기존 참조 유지. |
| 신규 `Resources/Spacing.xaml`, `Resources/ControlStyles.xaml` | 생성 | - |

### 4-B. 계약·직렬화 변경
- **없음.** XAML 구조만 변경, 바인딩 경로·이벤트 시그니처·ViewModel 공개 멤버 불변.

### 4-C. 영향 받는 테스트
- 없음(UI). 기존 80건은 무관 → 빌드/테스트로 회귀 확인.

### Verified by
- 각 페이지 코드비하인드의 x:Name·이벤트 핸들러·x:Bind 참조를 Read로 확인(위 보존 목록). XAML 재구성 시 이들을 유지하면 .cs 변경 불요.

## Decisions

### DG1. WinAppSDK 유지(2.x 미상향)
- **Chosen**: 현재 `Microsoft.WindowsAppSDK 1.*` 유지(실측 해석 = **1.8.260508005**, 전이 `.WinUI 1.8.260505002` 포함). 근거: TitleBar는 1.7+ 도입(1.8 보유), SettingsControls 8.2는 `.WinUI>=1.8` 요구(충족). NapCat의 2.x는 근거 아님 — WorkGroup 실측 버전이 근거. **복원 가능성 최종 확정 = T1 빌드 게이트.**
- **실패 분기(사전 확정)**: T1에서 SettingsControls 8.2 복원 실패 시 → 8.1.x로 하향 시도, 그래도 실패면 Halt(SDK 상향은 승인 필요).
- **Source**: `dotnet list package --include-transitive` 실측, WebSearch(TitleBar/SettingsControls).

### DG2. 새 의존성 — CommunityToolkit.WinUI.Controls.SettingsControls 8.2.251219
- **Chosen**: 추가(사용자 승인). SettingsCard로 설정/정보 항목 표준화(NapCat 동일). 버전 핀 8.2.251219.
- **Source**: 사용자 확인, NapCat csproj.

### DG3. 커스텀 TitleBar(자동 동기화 + SetTitleBar 폴백)
- **Chosen**: WindowEx에 `ExtendsContentIntoTitleBar = true`. MainShell row0에 `Microsoft.UI.Xaml.Controls.TitleBar`(Title="WorkGroup" + IconSource=`ms-appx:///Assets/Square44x44Logo.scale-200.png`). 1차로 **SetTitleBar 미호출**(NapCat 자동 동기화 패턴).
- **폴백(GUI 검증 의존, 사전 확정)**: 1.8에서 자동 동기가 드래그/캡션버튼을 정상 처리하지 못하면(사용자 GUI 확인), MainShell이 TitleBar를 노출하고 App.xaml.cs가 navigate 후 `_window.SetTitleBar(titleBar)` 호출하는 폴백을 추가한다(T2 범위 내). **자율 종료 기준은 빌드 0/0**이며, 드래그/캡션 시각 정상은 사용자 수동 확인.
- **Source**: 사용자 승인, NapCat grep(단 2.1.3), TitleBar는 WorkGroup 1.8 보유.

### DG4. Resources 범위 — Spacing + ControlStyles만
- **Chosen**: 신규 `Resources/Spacing.xaml`(토큰: SpacingUnit/Element/CardGap/ContainerPadding, PageContentPadding=36,24,40,24, CardPaddingMd/Lg, RadiusSm/Default/Md/Lg/Xl/Full, SideNavWidth=280, ContentMaxWidth=1024) + `Resources/ControlStyles.xaml`(CardStyle, HeroCardStyle, PrimaryActionStyle, SecondaryActionStyle, SectionHeaderStyle, SettingsGroupHeaderStyle). ControlStyles는 Spacing을 자체 머지(StaticResource 해석). Colors/Brushes/Typography(커스텀)는 **제외** — 표준 ThemeResource 브러시·텍스트 스타일 사용.
- **Rationale**: WorkGroup은 NapCat의 상태색/타이머 스타일이 불필요. CardStyle은 표준 `CardBackgroundFillColorDefaultBrush`+`OverlayCornerRadius`(전역) 기반이라 추가 색 정의 불요.
- **Source**: NapCat Resources Read, WorkGroup 필요 분석.

### DG5. 메뉴 아이콘 — 기존 SymbolIcon 유지(변경 없음)
- **Chosen**: 현재 SymbolIcon(작업그룹=AllApps, 트레이=List, 설정=Setting, 정보=Help) **그대로 유지.** SymbolIcon과 FontIcon은 동일 Segoe Fluent 글리프를 렌더해 **시각 차이가 없으므로**, 아이콘 의미 변경·글리프 코드 오타 위험을 피하기 위해 전환하지 않는다(M3 반영). 디자인 차이는 NavigationView 레이아웃·카드·타이틀바에서 나오며 아이콘 폼은 무관.
- **Source**: M3 정정(불필요한 변경 제거).

### DG6. 페이지 공통 레이아웃
- **Chosen**: 각 페이지 = `ScrollViewer Padding="{StaticResource PageContentPadding}" HorizontalScrollMode="Disabled"` → `StackPanel MaxWidth="{StaticResource ContentMaxWidth}" HorizontalAlignment="Stretch" Spacing="24"` → 헤더(`TextBlock Style=TitleTextBlockStyle` 제목 + `TextBlock Style=BodyTextBlockStyle Foreground=TextFillColorSecondaryBrush` 부제) → 본문(CardStyle 카드 / SettingsCard).
- **레이아웃 폭 변화(m3)**: 현재 페이지 MaxWidth(720/760) → `ContentMaxWidth`(1024)로 넓어진다(Gallery 표준). 의도된 변화.
- **Source**: NapCat DashboardView/GeneralSettingsView 패턴.

### DG7. App.xaml 잔여 템플릿 리소스 — 유지
- **Chosen**: 기존 Primary/PrimaryBrush/WhiteBrush/BlackBrush/AppFontSize/MyLabel/Action/PrimaryAction는 **유지**(MergedDictionaries만 추가). 미사용이라도 제거는 범위 밖.
- **follow-up**: 미사용 확인 시 별도 정리(승인 후).

### DG8. 창 MinSize 유지
- **Chosen**: 직전 작업의 `MinWidth=800`/`MinHeight=560` 유지. ExtendsContentIntoTitleBar만 추가.

## Tasks

> 공통: 한글 주석, UTF-8(BOM 없음), 빌드 `dotnet build WorkGroup.slnx` 0/0, 테스트 80/80 회귀 없음. XAML 재구성 시 4-A 보존 목록(x:Name·핸들러·x:Bind) 유지 → .cs 변경 최소.

- [x] **T1. SettingsControls 의존성 + Resources 토큰 + App.xaml 병합** *(~2h)*
  - **Type**: D
  - **Acceptance**: `WorkGroup.App.csproj`에 `CommunityToolkit.WinUI.Controls.SettingsControls 8.2.251219` 추가되어 복원·빌드 0/0. 신규 `Resources/Spacing.xaml`(DG4 토큰)·`Resources/ControlStyles.xaml`(CardStyle 등, Spacing 자체 머지) 생성. **csproj에 두 리소스를 명시적 `<Page Update="Resources\Spacing.xaml" Generator="MSBuild:Compile"/>`(및 ControlStyles)로 등록**(B3 — 컴파일·패키징 보장). `App.xaml` MergedDictionaries에 두 딕셔너리 추가(XamlControlsResources 다음). 기존 App.xaml 리소스 유지. 빌드 산출물에 두 ResourceDictionary가 컴파일됨(obj의 .g.cs/.g.i.cs 또는 .xbf 생성) 확인.
  - **Files**: 주: `src/WorkGroup.App/WorkGroup.App.csproj`, `src/WorkGroup.App/Resources/Spacing.xaml`(신규), `src/WorkGroup.App/Resources/ControlStyles.xaml`(신규), `src/WorkGroup.App/App.xaml`
  - **Edge Cases**: SettingsControls 복원 실패→DG1 실패 분기(8.1.x 하향, 안 되면 Halt). StaticResource 키 미해석→ControlStyles가 Spacing 머지로 해소. Resources xaml 컴파일 누락→명시적 Page 등록으로 방지.
  - **Halt Forecast**: "SettingsControls 버전 비호환?" → Risks. "리소스 경로?" → `ms-appx:///Resources/...`(NapCat 동일). "CardStyle OverlayCornerRadius?" → 전역 ThemeResource(XamlControlsResources 제공).
  - **Depends on**: -

- [x] **T2. 셸 — 커스텀 TitleBar + NavigationView 정교화** *(~2.5h)*
  - **Type**: D
  - **Acceptance**(자율 종료 = 빌드 0/0; 드래그/캡션 시각은 사용자 수동): `App.xaml.cs` WindowEx에 `ExtendsContentIntoTitleBar = true` 추가. `MainShell.xaml`을 Grid(row0=`TitleBar` Title="WorkGroup"+IconSource, row1=NavigationView)로 재구성: `OpenPaneLength=280`, `IsPaneToggleButtonVisible=False`, `IsSettingsVisible=False`, `IsTitleBarAutoPaddingEnabled=False`, `Background=Transparent`, NavigationView.Resources로 컨텐츠 마진/보더 0. **메뉴 아이콘은 기존 SymbolIcon 유지(DG5).** **MenuItems(작업그룹·트레이메뉴)/FooterMenuItems(설정·정보) 분리 유지(M2).** ContentFrame `Background=Transparent`. x:Name `Nav`/`ContentFrame`·핸들러(`OnLoaded`/`OnSelectionChanged`)·Tag(WorkGroups/TrayMenu/Settings/About) 유지(.cs 불변).
  - **Files**: 주: `src/WorkGroup.App/App.xaml.cs`, `src/WorkGroup.App/Views/MainShell.xaml`
  - **Edge Cases**: TitleBar 자동 동기 실패→DG3 폴백(`_window.SetTitleBar`). IconSource 자산 부재→Title만. 닫기→트레이 재표시 시 타이틀바 유지. CS0612(WinUIEx Window.Icon, XamlTypeInfo)→발생 시 NoWarn 추가(M1).
  - **Halt Forecast**: "SetTitleBar 필요?" → 1차 자동 동기, GUI 실패 시 DG3 폴백(T2 내 추가). "TitleBar 네임스페이스?" → Microsoft.UI.Xaml.Controls(전역 using). "CS0612 빌드 경고?" → M1(NoWarn, csproj). "footer 분리?" → M2(유지).
  - **Depends on**: T1
  - **MainShell.xaml.cs 확인**: 재구성 후에도 `Nav.MenuItems[0]`(OnLoaded)·`Nav`(SelectionChanged 시그니처)·`ContentFrame.Content/Navigate`가 유효한지 빌드로 확정.

- [x] **T3. 설정 페이지 — SettingsCard** *(~2h)*
  - **Type**: C
  - **Acceptance**: `SettingsPage.xaml`을 공통 레이아웃(DG6) + 그룹 헤더(SettingsGroupHeaderStyle) + `controls:SettingsCard`(자동시작=ToggleSwitch, 테마=RadioButtons 또는 ComboBox)로 재구성. HeaderIcon=FontIcon. x:Bind(AutoStartEnabled/ThemeIndex/HasStatus/StatusMessage) 유지(.cs 불변). 빌드 0/0, 수동: 토글/테마 동작 동일.
  - **Files**: 주: `src/WorkGroup.App/Views/SettingsPage.xaml`
  - **Edge Cases**: SettingsCard.Content에 RadioButtons(세로 3개)→레이아웃 폭 확인. 상태 InfoBar 위치 유지. xmlns `controls` 선언 누락→빌드 에러로 검출.
  - **Halt Forecast**: "SettingsCard xmlns?" → `using:CommunityToolkit.WinUI.Controls`. "테마 3택을 SettingsCard에?" → SettingsExpander 또는 SettingsCard+RadioButtons(폭 넓으면 별 카드).
  - **Depends on**: T1, T2

- [x] **T4. 정보 페이지 — 공통 레이아웃 + SettingsCard/카드** *(~1.5h)*
  - **Type**: C
  - **Acceptance**: `AboutPage.xaml`을 공통 레이아웃(DG6)으로: 앱 이름/버전을 CardStyle 카드 또는 SettingsCard, 라이선스 목록을 ItemsControl(SettingsCard 또는 CardStyle 항목, HyperlinkButton 유지). x:Bind/DataTemplate(svc:LicenseInfo) 유지. 빌드 0/0, 수동: 버전·라이선스·링크 동작 동일.
  - **Files**: 주: `src/WorkGroup.App/Views/AboutPage.xaml`
  - **Edge Cases**: 라이선스 7개 ItemsControl 비가상화(소량 OK). 링크 HyperlinkButton 유지. 카드 간격 CardGap.
  - **Halt Forecast**: "라이선스 항목을 SettingsCard로?" → SettingsCard(Header=이름, Description=종류, Content=HyperlinkButton) 권장.
  - **Depends on**: T1, T2

- [x] **T5. 작업 그룹 + 트레이 페이지 공통 레이아웃** *(~2.5h)*
  - **Type**: D
  - **Acceptance**: `WorkGroupsPage.xaml`을 공통 레이아웃(DG6)으로: 헤더(제목+부제) + 우상단 "그룹 추가"(PrimaryActionStyle 또는 AccentButtonStyle), 그룹 목록을 CardStyle 카드 안에 ListView(드래그/아이콘버튼/2라인 유지). 모든 이벤트 핸들러·CanDragItems·x:Bind 유지(.cs 불변). `TrayMenuPage.xaml`도 공통 레이아웃(헤더 + InfoBar). 빌드 0/0, 수동: 추가/수정/삭제/드래그 핀 동작 동일.
  - **Files**: 주: `src/WorkGroup.App/Views/WorkGroupsPage.xaml`, `src/WorkGroup.App/Views/TrayMenuPage.xaml`
  - **Edge Cases**: ListView를 CardStyle Border로 감쌀 때 드래그 영역 유지. 빈 상태 안내 유지. 그룹 아이콘/멤버 미니아이콘 템플릿 유지. PageContentPadding 적용 시 ListView 높이(*).
  - **Halt Forecast**: "드래그가 카드 래핑으로 깨지나?" → DragItemsStarting은 ListView 속성이라 무관, 래핑 무해. "그룹 추가 버튼 위치?" → 헤더 행 우측(Grid 2열) 또는 헤더 아래.
  - **Depends on**: T1, T2

- [x] **T6. 문서 갱신 + 최종 점검** *(~0.5h)*
  - **Type**: A
  - **Acceptance**: `README.md`(디자인 시스템·리소스·SettingsCard 반영), `notes.md`, `AGENTS.md`(승인된 의존성에 SettingsControls 추가) 갱신. 전체 빌드 0/0 + 테스트 80/80 최종 확인.
  - **Files**: 문서: `README.md`, `notes.md`, `AGENTS.md`
  - **Edge Cases**: 없음.
  - **Halt Forecast**: 없음.
  - **Depends on**: T3, T4, T5

## Verification Strategy
- **자율 종료 기준(각 task)**: `dotnet build WorkGroup.slnx` → 0/0 + `dotnet test` 80/80 회귀 없음. (UI 시각 정상은 빌드로 보장 불가 — 아래 수동.)
- 수동(GUI — 사용자 확인, 자율 실행 관찰 불가): ① 커스텀 TitleBar(아이콘+제목, 드래그, 캡션버튼) ② NavigationView 280 pane·컨텐츠 보더 없음 ③ 페이지 공통 여백/카드/SettingsCard ④ 설정 토글·테마·정보 링크 동작 동일 ⑤ 그룹 추가/수정/삭제/드래그 핀 동작 동일 ⑥ 라이트/다크 테마 일관.

## Progress Log
<!-- implement-task가 갱신 -->
- **T3-T4 완료** (커밋 6fa4184, 다음): T3=SettingsPage 공통 레이아웃 + SettingsCard(자동시작 ToggleSwitch/테마 ComboBox, ThemeIndex 동일 바인딩). T4=AboutPage 공통 레이아웃 + SettingsCard(앱이름/버전 + 라이선스 ItemsControl). 바인딩 계약(VM/.cs/LicenseCatalog) 전부 불변. 빌드 0/0, 테스트 80/80, spec OK.
- **T5-T6 완료** (커밋 71d9d27, 다음): T5=WorkGroupsPage(공통 레이아웃+CardStyle 목록+PrimaryActionStyle, 핸들러/드래그/바인딩 보존, ListView Padding 0,4 클리핑 방어)+TrayMenuPage 공통 레이아웃. T6=문서(README/notes/AGENTS — SettingsControls 등재, 디자인 시스템 반영). 빌드 0/0, 테스트 80/80. **NapCat Fluent 정합 plan 전체 완료(T1~T6).**

## Next Steps
- **현재 상태(2026-06-02)**: ✅ WinUI Gallery Fluent 디자인 정합 완료(T1~T6). 디자인 토큰·SettingsCard·커스텀 TitleBar·NavigationView 정교화·공통 페이지 레이아웃. 기능/바인딩 불변, 빌드 0/0, 테스트 80/80.
- **GUI 수동 검증 필요**(자율 관찰 불가): ① 커스텀 TitleBar(아이콘/제목/드래그/캡션버튼) — 자동 동기 실패 시 DG3 폴백(AppTitleBar에 SetTitleBar) ② NavigationView 280 pane·컨텐츠 보더 없음 ③ 페이지 카드/SettingsCard/여백 ④ 설정·정보·작업그룹 동작 동일 ⑤ 라이트/다크 일관.
- 권장 다음 액션: 사용자 GUI 검증 → 정상 시 PR 생성.
- follow-up: App.xaml 잔여 템플릿 리소스(Primary/MyLabel/Action 등) 미사용 정리(DG7, 승인 후).
- Suggested skills: 공식 /code-review, /security-review.
- **T1-T2 완료** (커밋 dfad8f9, 다음): T1=SettingsControls 8.2.251219 추가(1.8 호환 실증)+Resources(Spacing 3토큰/ControlStyles CardStyle 등)+App.xaml 병합, 명시적 Page 등록(.xbf 생성). T2=App.xaml.cs ExtendsContentIntoTitleBar+MainShell 재구성(TitleBar Title/Icon + NavigationView 280pane·컨텐츠보더0·SymbolIcon 유지·MenuItems/Footer 분리·계약 보존). 빌드 0/0(CS0612 미발생), 테스트 80/80. spec/quality OK(Spacing YAGNI 정리). **TitleBar 드래그/캡션 시각은 사용자 GUI 확인 대기(폴백 SetTitleBar용 x:Name AppTitleBar 준비됨).**

## Open Questions (모두 해결됨)
- [x] 타이틀바 → **커스텀 TitleBar(NapCat 동일)**(사용자).
- [x] SettingsCard 의존성 → **SettingsControls 8.2.251219 추가**(사용자).
- [x] WinAppSDK → **1.8 유지**(검증: TitleBar 1.7+/SettingsControls 8.2가 1.8 지원).
