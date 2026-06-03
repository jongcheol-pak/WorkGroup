# plan.md — 앱 아이콘 설정 (exe / 창 / 타이틀바 / 정보 카드)

> 이전 plan(그룹 수정 화면 이름 클릭-편집)은 완료(git 이력). 본 plan은 앱 아이콘(AppIcon.ico) 일괄 적용.

## 목표
프로젝트 루트의 `AppIcon.ico`를 앱 전반의 아이콘으로 적용한다.
1. **실행 파일(.exe) 아이콘** — 탐색기/작업 관리자에서 보이는 exe 아이콘을 AppIcon.ico로.
2. **창 아이콘(작업 표시줄 / Alt+Tab 미리보기)** — 메인 창의 작업 표시줄·미리보기 아이콘을 AppIcon.ico로.
3. **메인 화면 타이틀바 아이콘** — 타이틀바 좌측 앱 아이콘을 AppIcon.ico로 변경 + **크기 28px로 키움**.
4. **정보 화면 '앱 이름 카드' 아이콘** — AppIcon.ico로 통일 + **크기 48px로 키움**.

## 배경 / 근본 원인 (확인된 사실)
- 프로젝트: WinUI 3 / .NET 10 / MSIX **패키지 앱**(`UseWinUI=true`, `WindowsPackageType` 미지정 = 패키지 빌드). `app.manifest` `dpiAwareness=PerMonitorV2`.
- `AppIcon.ico`는 저장소 루트(`D:\Personal Project\Windows\WorkGroup\AppIcon.ico`)에 있고, 현재 프로젝트에서 **참조 없음**(grep 전수 확인).
- **exe 아이콘**: csproj `<ApplicationIcon>`을 지정하면 컴파일 시 exe에 Win32 아이콘 리소스가 박힌다(패키지 앱에서도 exe 자체에는 적용). 물리 파일이 프로젝트 내에 있어야 한다 → Assets로 복사 후 지정.
- **창 아이콘**: `Microsoft.UI.Windowing.AppWindow.SetIcon(string path)` — `.ico` 파일 경로를 받아 창(작업 표시줄/Alt+Tab 미리보기) 아이콘을 설정(표준 API, WinUIEx 불필요). 패키지 앱은 설치 폴더 기준 상대경로(`Assets\AppIcon.ico`)를 받으며, 파일이 출력에 포함(Content)되어야 한다. 현재 `App.xaml.cs`의 `ShowMainWindow`에서 `WindowEx win`을 생성하므로 `win.AppWindow.SetIcon(...)` 호출 가능.
- **타이틀바 아이콘 크기**: WinUI 3 `TitleBar` 컨트롤의 `IconSource`는 표시 크기가 **고정**(약 16px)이라 키울 수 없다(공식 확인). 시각 배치 순서는 **LeftHeader → IconSource → Title → Subtitle → Content → RightHeader**(공식 확인). 따라서 `IconSource`를 제거하고 `TitleBar.LeftHeader`에 크기 지정 `Image`를 넣으면 아이콘이 제목 **왼쪽 맨 앞**에 오면서 원하는 크기로 표시된다.
- **정보 카드 아이콘**: `SettingsCard.HeaderIcon`은 `PART_HeaderIconPresenterHolder`(Viewbox 계열) presenter에서 호스팅된다. `ImageIcon`에 `Width/Height`를 지정하면 그 크기로 스케일된다(슬롯이 제약하면 presenter 크기 리소스 조정 필요 — 위험 섹션).
- `.ico`는 WinUI `ImageSource`(BitmapImage) 및 `ImageIcon`/`Image`의 `Source`로 디코드 가능(가장 큰 프레임 사용).

## 영향 범위 (전수 조사 결과 — grep + Read 확인)
- `Square44x44Logo.scale-200.png` **코드** 참조: `Views/MainShell.xaml:24`(타이틀바 IconSource), `Views/AboutPage.xaml:26`(정보 카드 HeaderIcon) — **둘 다 본 작업의 변경 대상**. 그 외 코드 참조 없음.
  - `WorkGroup.App.csproj:31`의 `<Content Include="Assets\Square44x44Logo.scale-200.png" />`와 `Package.appxmanifest:42`의 타일 로고(`Square44x44Logo.png`)는 **타일/스토어 자산**으로 본 작업과 별개 → **변경하지 않음**(기존 png 파일·등록 유지).
- `AppTitleBar`(x:Name): `Views/MainShell.xaml:22`에만 존재. `MainShell.xaml.cs`·`App.xaml.cs`에 **코드 참조 없음** → IconSource 제거·LeftHeader 추가가 코드비하인드에 영향 없음. `SetTitleBar()` 호출도 없음(`ExtendsContentIntoTitleBar=true` + `TitleBar` 컨트롤 자동 동기, App.xaml.cs:99).
- `AppWindow.SetIcon` 추가 위치: `App.xaml.cs`의 `ShowMainWindow`(L92 `new WindowEx`). 메인 창 분기에만 적용. 팝업창(`GroupPopupWindow`)은 **요청 범위 밖**(Out of Scope).
- `AppIcon.ico` 기존 참조 없음 → 신규 도입(파일 추가 + csproj 2줄 + 코드/XAML).
- **테스트**: `WorkGroup.App`은 패키지 WinUI 실행 파일이고 테스트 프로젝트가 App을 참조하지 않아 UI 단위 테스트 불가(AGENTS.md/이전 plan 확인). → 빌드 + **수동 GUI 검증**.

## Out of Scope (명시)
- 트레이 아이콘 변경(`TrayIconService`는 현재 `LoadIcon(IDI_APPLICATION)` 시스템 기본 사용 — 요청에 없음, 미변경).
- 팝업창(`GroupPopupWindow`)의 창 아이콘(요청은 "메인 화면" 기준).
- Package.appxmanifest의 타일/스토어 로고 및 시작 메뉴 타일(기존 png 유지).
- 기존 `Square44x44Logo.*` 자산 파일 삭제(타일에서 계속 사용).

## 결정 사항 (사용자 확정)
- **D1. 타이틀바 아이콘 크기**: **28px** (LeftHeader 커스텀 Image 방식).
- **D2. 정보 카드 아이콘**: **AppIcon.ico로 통일 + 48px**. 구현은 `ImageIcon`에 `Width/Height=48` 지정까지로 **단일 확정**한다(분기 없음). SettingsCard presenter 한계로 GUI상 48px가 미반영되면 그때 사용자와 재협의(implement-task는 Width/Height 지정으로 task 완료, 자율 실행 중 추가 분기 없음).
- **D3. AppIcon.ico 포함 방식**: **Assets 폴더로 복사 후 Content 포함**(`src/WorkGroup.App/Assets/AppIcon.ico`).

## 결정 사항 (기술결정 — 추가 입력 불필요)
- **D4. exe 아이콘 경로**: csproj `<ApplicationIcon>Assets\AppIcon.ico</ApplicationIcon>`(프로젝트 상대경로).
- **D5. 창 아이콘 호출 위치/경로**: `ShowMainWindow`에서 `win` 생성 직후(필드 대입 전) `win.AppWindow.SetIcon(@"Assets\AppIcon.ico")` 1회 호출. 패키지 설치 폴더 기준 **상대경로로 단일 확정**(절대경로 폴백 분기 없음 — 미표시 시 사용자 GUI 확인 후 후속 옵션은 R2 참고).
- **D6. 타이틀바 LeftHeader 구성**: 기존 `<TitleBar.IconSource>` 제거 → `<TitleBar.LeftHeader>` 안에 `Image`(`Source="ms-appx:///Assets/AppIcon.ico"`, `Width=28`, `Height=28`, `VerticalAlignment=Center`, 제목과 간격용 `Margin="0,0,8,0"`). `Title="WorkGroup"` 유지.
- **D7. 정보 카드 HeaderIcon**: `ImageIcon`의 `Source`를 `ms-appx:///Assets/AppIcon.ico`로 변경하고 `Width="48" Height="48"` 지정.
- **D8. AppIcon.ico Content 등록**: csproj 기존 Assets `<Content Include>` 그룹에 `<Content Include="Assets\AppIcon.ico" />` 추가(SetIcon 출력 포함 + ms-appx 접근).

## 작업 분해

### T1. AppIcon.ico를 Assets로 복사 + csproj 아이콘 등록 (Type A — 빌드 구성)
- **Files**: `AppIcon.ico`(루트, 원본) → `src/WorkGroup.App/Assets/AppIcon.ico`(복사 대상, 신규), `src/WorkGroup.App/WorkGroup.App.csproj`
- **변경**:
  1. 루트 `AppIcon.ico`를 `src/WorkGroup.App/Assets/AppIcon.ico`로 **복사**(원본 보존).
  2. csproj `<PropertyGroup>`(App Options 영역)에 `<ApplicationIcon>Assets\AppIcon.ico</ApplicationIcon>` 추가.
  3. csproj 기존 Assets `<ItemGroup>`(현재 28~36행)에 `<Content Include="Assets\AppIcon.ico" />` 추가.
- **Acceptance (자동·완료 조건)**: `Assets\AppIcon.ico` 파일 존재. `dotnet build WorkGroup.slnx`(플랫폼 미지정, AGENTS.md L9) 경고/에러 0. 빌드 출력 폴더에 `Assets\AppIcon.ico` 복사됨.
- **사후 검수(사용자 GUI)**: 빌드된 exe의 탐색기/작업관리자 아이콘이 AppIcon.ico.
- **Edge Cases**: 경로 구분자는 Windows 백슬래시(`Assets\AppIcon.ico`); 파일명 대소문자 일치(`AppIcon.ico`).
- **Halt Forecast**: `<ApplicationIcon>` 경로 오타 시 빌드 에러(아이콘 파일 못 찾음) → 복사 위치/경로 재확인.

### T2. App.xaml.cs — 메인 창 아이콘 SetIcon (Type C)
- **Files**: `src/WorkGroup.App/App.xaml.cs`
- **변경**: `ShowMainWindow()`의 `win` 생성 블록(L92~100) 직후, `win.AppWindow.Closing += ...`(L102) 부근에서 `win.AppWindow.SetIcon(@"Assets\AppIcon.ico");` 호출(창 1회 생성 시점에만 실행되도록 `if (_window is null)` 블록 내부). 한글 주석으로 "작업 표시줄/Alt+Tab 미리보기 아이콘 지정" 명시.
- **Acceptance (자동·완료 조건)**: 빌드 통과. `SetIcon` 호출이 `if (_window is null)` 생성 블록 **내부**라 창 재표시(트레이→열기) 반복 시 중복 호출 없음(코드 검토).
- **사후 검수(사용자 GUI)**: 메인 창 실행 시 작업 표시줄 아이콘·Alt+Tab 미리보기가 AppIcon.ico.
- **Edge Cases**: `SetIcon` 경로가 패키지 설치 폴더에 없으면 무시/예외 — T1에서 Content 포함으로 보장. 상대경로는 패키지 앱 기준.
- **Halt Forecast**: `AppWindow.SetIcon(string)` 오버로드는 Windows App SDK 표준 — 컴파일 이슈 낮음. 경로는 상대경로(`Assets\AppIcon.ico`)로 단일 확정(D5) — 자율 실행 중 분기 없음.

### T3. MainShell.xaml — 타이틀바 아이콘 변경 + 28px (Type C)
- **Files**: `src/WorkGroup.App/Views/MainShell.xaml`
- **변경**: `<TitleBar Grid.Row="0" x:Name="AppTitleBar" Title="WorkGroup">`(L22)의 기존 `<TitleBar.IconSource>...ImageIconSource...</TitleBar.IconSource>`(L23~25) **제거** → `<TitleBar.LeftHeader>` 추가, 그 안에 `<Image Source="ms-appx:///Assets/AppIcon.ico" Width="28" Height="28" VerticalAlignment="Center" Margin="0,0,8,0" />`. `Title="WorkGroup"` 유지.
- **Acceptance (자동·완료 조건)**: 빌드 통과. IconSource 블록이 완전히 제거되고 LeftHeader에 28px Image가 추가됨(코드 검토).
- **사후 검수(사용자 GUI)**: 메인 창 타이틀바 좌측에 28px AppIcon.ico가 제목 "WorkGroup" 왼쪽에 표시.
- **Edge Cases**: LeftHeader는 IconSource보다 좌측이라 아이콘→제목 순서 유지(공식 배치 순서 확인). Margin으로 제목과 간격 확보.
- **Halt Forecast**: `ImageIconSource`/`IconSource` 잔존 시 16px 작은 아이콘이 LeftHeader 아이콘과 중복 표시 → IconSource 블록 완전 제거 확인. ms-appx 경로 오타 시 빈 이미지.

### T4. AboutPage.xaml — 정보 카드 아이콘 AppIcon.ico + 48px (Type C)
- **Files**: `src/WorkGroup.App/Views/AboutPage.xaml`
- **변경**: `SettingsCard.HeaderIcon`의 `<ImageIcon Source="ms-appx:///Assets/Square44x44Logo.scale-200.png" />`(L26)를 `<ImageIcon Source="ms-appx:///Assets/AppIcon.ico" Width="48" Height="48" />`로 변경.
- **Acceptance (자동·완료 조건)**: 빌드 통과. HeaderIcon ImageIcon의 Source가 AppIcon.ico, Width/Height=48로 변경됨(코드 검토).
- **사후 검수(사용자 GUI)**: 정보 화면 앱 이름 카드의 아이콘이 AppIcon.ico·48px로 표시.
- **Edge Cases**: SettingsCard HeaderIcon presenter(Viewbox)가 48px를 수용하는지 — 사용자 GUI에서 확인. 카드 헤더 높이가 아이콘에 맞게 자동 확장.
- **Halt Forecast**: D2 단일 확정 — 본 task는 `ImageIcon Width/Height=48` 지정으로 **완료**(자율 실행 중 분기 없음). presenter 한계로 GUI상 48px 미반영 시(R1)는 task 완료 후 사용자 GUI 확인 단계에서 드러나며, 그때 재협의.

### T5. 문서 갱신 (Type A)
- **Files**: `README.md`, `notes.md`
- **변경**: README에 앱 아이콘(실행/창/타이틀바/정보) 관련 설명이 있으면 현행화(현재 동작만, 과장 없이). 없으면 UI/아이콘 항목에 간단 반영. `notes.md` `## 최근 변경` 최상단에 본 작업 1줄 추가(`2026-06-03: 앱 아이콘(AppIcon.ico) exe/창/타이틀바28px/정보카드48px 적용`), 1개월 초과 항목 정리.
- **Acceptance**: 문서가 실제 변경과 일치.

## 위험 / 회귀
- **R1. 정보 카드 48px 미반영**: SettingsCard HeaderIcon presenter(Viewbox)가 크기를 제약하면 ImageIcon Width/Height가 무시될 수 있음 → 수동 GUI 확인 필수. 미반영 시 presenter 크기 리소스 오버라이드는 별도 협의(본 plan 범위는 표준 Width/Height 지정).
- **R2. 패키지 SetIcon 경로**: 상대경로가 설치 폴더 기준이 아닐 가능성 → T1 Content 포함으로 출력 보장. 경로는 상대경로로 단일 확정(D5, 자율 실행 중 폴백 없음). 미표시 시 사용자 GUI 확인 후 재협의.
- **R3. 회귀 없음**: 타일/스토어 로고(Package.appxmanifest)·기존 Square44x44Logo 자산은 미변경 → 시작 메뉴/스토어 표시 영향 없음. 타이틀바/정보 카드만 시각 변경.

## 검증 방법
**자동 검증(implement-task 자율 완료 조건)**:
- `dotnet build WorkGroup.slnx`(플랫폼 미지정, AGENTS.md L9) — 경고/에러 0.
- `dotnet test WorkGroup.slnx` — 기존 테스트 회귀 없음(본 변경은 테스트 대상 외, 그린 유지).
- 코드 검토: 각 task 변경이 plan대로 반영됐는지(IconSource 제거, LeftHeader/SetIcon/HeaderIcon 추가, csproj 2줄).

**사후 검수(사용자, F5 MSIX — 헤드리스 불가)**: ① 탐색기/작업관리자 exe 아이콘 ② 작업 표시줄·Alt+Tab 미리보기 아이콘 ③ 타이틀바 좌측 28px 아이콘(제목 왼쪽) ④ 정보 화면 카드 48px 아이콘. ③④가 미반영이면 R1/R2에 따라 재협의.

## 승인 필요 사항
- csproj 빌드 구성 변경(`<ApplicationIcon>`, `<Content>`)·파일 추가(Assets\AppIcon.ico)·App.xaml.cs 코드 추가 포함. 의존성 추가/공개 API 변경 없음. 승인 후 implement-task 자율 진행.

## Task 체크리스트
- [x] T1. AppIcon.ico 복사 + csproj 등록
- [x] T2. App.xaml.cs 창 아이콘 SetIcon
- [x] T3. MainShell.xaml 타이틀바 28px
- [x] T4. AboutPage.xaml 정보 카드 48px
- [x] T5. 문서 갱신

## Progress Log
- T1~T4 완료 (커밋 e992ed6/f948b2c/8c86113/5861657): AppIcon.ico를 Assets로 복사 + csproj ApplicationIcon·Content 등록(T1), App.xaml.cs AppWindow.SetIcon(T2), MainShell.xaml IconSource→LeftHeader 28px(T3), AboutPage.xaml HeaderIcon AppIcon.ico+48px(T4). 빌드 0/0, 테스트 80/80, spec-compliance OK. 작업 트리의 타일/스토어 자산(png/manifest)은 사용자 진행 작업이라 미커밋 유지(csproj는 사용자 승인하에 함께 포함).
- T5 완료: notes.md/README.md 갱신.

## Next Steps
- 권장 다음 액션: F5 MSIX 수동 GUI 검증(① exe 아이콘 ② 작업 표시줄/Alt+Tab 미리보기 ③ 타이틀바 28px ④ 정보 카드 48px). ③④ 미반영 시 plan R1/R2 참고. 이후 PR 생성.
- Suggested skills: 공식 /code-review, 공식 /security-review
