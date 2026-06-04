# Plan: 작업 표시줄 위치별 핀 팝업 방향(세로/가로) 전환

## Goal
핀된 그룹 아이콘 클릭 시 뜨는 팝업(`GroupPopupWindow`)을 작업 표시줄이 붙은 변에 맞춰 표시한다.
- **하단**(기본): 작업 표시줄 위, 아이콘 **가로 1행** — 현행 유지.
- **상단**: 작업 표시줄 아래, 아이콘 **가로 1행** — 위치는 이미 정상 배치되므로 가로 유지(요청 명세 그대로).
- **좌측**: 작업 표시줄 오른쪽, 아이콘 **세로 1열**.
- **우측**: 작업 표시줄 왼쪽, 아이콘 **세로 1열**.

핵심 작업은 **좌/우 작업 표시줄일 때 팝업 아이콘 배치를 세로(1열)로 전환**하고, 그에 맞춰 크기 측정·오버플로 스크롤 방향을 분기하는 것이다. 위치 계산(`TaskbarPopupPositioner.Compute`)은 이미 4변을 모두 처리하므로 변경하지 않는다.

## Out of Scope
- `TaskbarPopupPositioner`(위치 계산) 알고리즘 변경 — 이미 상/하/좌/우 4변을 모두 계산(단위 테스트 4건 통과). 변경 없음.
- 폴더 바로가기 팝업(`FolderListPopupWindow`/`FolderContentsPopupWindow`) — 사용자 결정(그룹 팝업만). 현행 유지.
- 우클릭 컨텍스트 메뉴, "+" 그룹 편집 버튼, 헤더 표시 토글, 다국어 리소스 — 기능 변경 없음(세로 배치에서도 그대로 동작).
- 생성자 시그니처(`GroupPopupWindow(string groupId)`) — 불변. 호출처(App.xaml.cs) 무영향.
- 호버 애니메이션·아이콘 박스 크기(48×48) 등 항목 템플릿 — 변경 없음.

## Investigation Log
- `AGENTS.md`/`README.md` 읽음 → DDD 4레이어, 빌드 `dotnet build WorkGroup.slnx`, 테스트 `dotnet test WorkGroup.slnx`, 헤드리스에서 GUI 관찰 불가(빌드/테스트까지만 자동 검증). 파일 UTF-8(BOM 없음), 한글 주석.
- `Infrastructure/Interop/TaskbarPopupPositioner.cs` 읽음 → `enum TaskbarEdge { Bottom, Top, Left, Right }`, `DetectEdge(monitor, work)`(public static), `Compute(...)`가 4변 모두 좌상단 배치 계산. 좌=`work.Left`, 우=`work.Right-width`, 상=`work.Top`, 하=`work.Bottom-height`. 세로 변일 때 커서 Y를 세로 중심으로 클램프. **위치는 이미 완성.**
- `tests/.../TaskbarPopupPositionerTests.cs` 읽음 → 4변 `DetectEdge` + 하단/좌측 `Compute` + 클램프/포함 테스트 존재. 변경 불필요.
- `Infrastructure/Interop/ScreenMetricsProvider.cs` 읽음 → `Metrics(int CursorX, int CursorY, ScreenRect Monitor, ScreenRect Work)`를 핀 클릭 시점에 1회 캡처. `GroupPopupWindow`가 `_metrics` 필드로 보관. → 생성자에서 `DetectEdge(_metrics.Monitor, _metrics.Work)`로 변 판정 가능.
- `Views/GroupPopupWindow.xaml` 읽음 → `GridView x:Name="AppsGrid"`, `ItemsPanel`은 비가상화 `StackPanel Orientation="Horizontal"`(한 줄 나열). 4방향 스크롤 모드 모두 초기 `Disabled`. 항목 템플릿은 `Grid.Resources`에 `AppItemTemplate`/`AddButtonTemplate` + `PopupGridItemTemplateSelector`.
- `Views/GroupPopupWindow.xaml.cs` 읽음 → 생성자에서 `_metrics` 캡처→화면 밖 배치→`LoadAsync`. `AdjustToContent()`가 **가로 1행 전제**로 측정: ① 무한 너비/높이 Measure로 자연 너비 산출 → ② `maxWidth = Work.Width - WorkAreaMargin(24)` 상한 클램프(초과 시 가로 스크롤 Auto) → ③ 확정 너비로 높이 재측정 → ④ chrome(테두리) 보정 후 Resize. `MoveToTaskbar`가 `TaskbarPopupPositioner.Compute`로 정위치 이동. `_lastAppliedWidth/Height` 가드로 SizeChanged 무한 루프 차단.
- `GroupPopupWindow(string groupId)` 호출처 grep → App.xaml.cs:86, 221 (둘 다 `new GroupPopupWindow(groupId)`). 시그니처 불변이라 무영향.
- `DetectEdge` 사용처 grep → Positioner 내부 + 테스트만. 신규 호출 추가는 안전(공개 static).

## Impact Analysis
### 4-A. 심볼/타입 추적
- 변경 대상: `GroupPopupWindow.xaml`, `GroupPopupWindow.xaml.cs` 2개 파일 한정.
- `GroupPopupWindow` 생성자: 시그니처 불변 → App.xaml.cs 2개 호출처 **변경 없음**(확인 완료).
- `TaskbarPopupPositioner.DetectEdge`: public static, 신규 호출만 추가(시그니처/동작 불변) → 기존 사용처·테스트 무영향.
- `TaskbarEdge` enum: 참조만 추가, 정의 불변.
- `AppsGrid`(GridView) `ItemsPanel`/스크롤 속성: 코드비하인드에서만 접근. 외부 바인딩 없음.
### 4-B. 계약·직렬화
- 직렬화·이벤트 페이로드·공개 API 변경 없음. groups.json 스키마 무관.
### 4-C. 영향 받는 테스트
- `TaskbarPopupPositionerTests`(Application.Tests) — 위치 계산 불변이므로 그대로 통과해야 함(회귀 가드).
- 측정/방향 분기는 UI 코드비하인드라 단위 테스트 대상 아님(헤드리스 GUI 관찰 불가). 신규 테스트 없음 → F5 MSIX GUI 수동 검증으로 보완.

## Risks
- **회귀(하단 가로)**: 측정 로직을 방향 분기로 재구성하면서 기존 하단/상단 가로 1행 동작이 깨질 위험. → 가로 경로는 현행 로직을 그대로 보존하고 세로 경로만 대칭으로 신설(공통 헬퍼로 중복 최소화하되 가로 동작 보존 우선).
- **ItemsPanel 교체 시점**: 아이템이 추가된 뒤 `ItemsPanel`을 바꾸면 재생성·깜빡임 우려. → 생성자에서 `_metrics` 캡처 직후(아이템 로드 `LoadAsync` 시작 전) 1회 결정·적용.
- **세로 오버플로 측정 부정확**: 가로에서 겪은 "스크롤 활성 상태 측정 시 자연 길이 오보고" 문제가 세로(VerticalScrollMode)에서도 동일 발생 가능. → 측정 동안 해당 방향 스크롤을 Disabled로 끄고 측정, 초과 시에만 Auto로 켜는 기존 패턴을 세로에 대칭 적용.
- **헤드리스 검증 한계**: 좌/우 작업 표시줄은 GUI에서만 실측 가능. 자동 검증은 빌드+기존 테스트 회귀까지.

## Tasks

### T1. 작업 표시줄 변 판정 + 세로/가로 ItemsPanel·스크롤 전환 (Type D)
- **Files**:
  - `src/WorkGroup.App/Views/GroupPopupWindow.xaml` — `Grid.Resources`에 두 `ItemsPanelTemplate`을 `x:Key`로 추가: `HorizontalItemsPanel`(StackPanel Orientation=Horizontal, 현행 인라인과 동일), `VerticalItemsPanel`(StackPanel Orientation=Vertical). GridView 인라인 `<GridView.ItemsPanel>`은 제거하고 기본을 코드에서 일괄 지정(또는 인라인 가로 유지 후 세로일 때만 교체). 세로 스크롤 초기값도 명시적으로 Disabled 유지(이미 4축 Disabled).
  - `src/WorkGroup.App/Views/GroupPopupWindow.xaml.cs` — **삽입 위치 명시**: 생성자에서 `_metrics = new ScreenMetricsProvider().Capture();`(현 L79) **직후, `_ = LoadAsync(groupId);`(현 L85) 전**에 다음을 수행 — ① `var edge = TaskbarPopupPositioner.DetectEdge(_metrics.Monitor, _metrics.Work);` ② `_isVertical = edge is TaskbarEdge.Left or TaskbarEdge.Right;`(신규 `private readonly bool _isVertical` 필드) ③ `AppsGrid.ItemsPanel = (ItemsPanelTemplate)((FrameworkElement)Content).Resources[_isVertical ? "VerticalItemsPanel" : "HorizontalItemsPanel"];`. 아이템 로드(`LoadAsync`) **전**이라 ItemsHost 재생성 비용·깜빡임 없음(set-after-load 우려는 시점 통제로 회피).
- **구현 메모**: `using WorkGroup.Infrastructure.Interop;`는 이미 존재. `AppsGrid`는 `InitializeComponent()` 이후 접근 가능(현 생성자에서 이미 `root`/서비스 접근). 리소스는 `Grid.Resources`(루트 Grid = `Content`)에 두므로 `((FrameworkElement)Content).Resources[키]`로 조회.
- **Acceptance**: 좌/우 변 모니터 환경에서 핀 클릭 시 아이콘이 세로 1열로 나열되고, 상/하 변에서는 가로 1행을 유지한다. 빌드 0 경고/0 에러, 기존 테스트 전부 통과.
- **Edge Cases**:
  - 멤버 0개 그룹(앱 없이 "+" 버튼만): 세로에서도 "+" 1개만 세로로 표시(StackPanel 단일 항목).
  - 판정 불가(`DetectEdge` 기본 Bottom): 가로로 폴백(현행과 동일).
  - **AddButton 박스 정렬**: 세로 StackPanel(Orientation=Vertical)에서 48×48 박스(AppItemTemplate/AddButtonTemplate 모두 `HorizontalAlignment="Center"`)가 가로 중앙 정렬로 동일하게 보이는지 — 템플릿 변경 없음 전제이나 방향 전환 영향이므로 GUI 검증 항목에 포함.
- **Halt Forecast**: ItemsPanel을 코드에서 교체하는 API가 동작하지 않으면(예: 리소스 조회 실패) Halt 대신 XAML에 두 GridView를 두는 대안 검토 — 그러나 우선 리소스 교체 방식으로 진행(`ItemsControl.ItemsPanel`은 set 가능한 의존 속성, 아이템 로드 전 1회 적용).

### T2. AdjustToContent 측정·오버플로 스크롤을 방향에 따라 분기 (Type D)
- **Files**: `src/WorkGroup.App/Views/GroupPopupWindow.xaml.cs` — `AdjustToContent()`를 `_isVertical` 분기. **가로 경로는 현행 코드 그대로 보존**하고, 세로 경로를 너비·높이 **양축 대칭**으로 신설한다.
- **세로 경로 의사코드(양축 상한·정합성 명시)**:
  ```
  scale = RasterizationScale
  // 측정 동안 양축 스크롤 모두 Disabled (정확한 자연 길이 측정)
  SetVerticalScrollMode(AppsGrid, Disabled); SetVerticalScrollBarVisibility(Disabled);
  SetHorizontalScrollMode(AppsGrid, Disabled); SetHorizontalScrollBarVisibility(Disabled);
  root.UpdateLayout();

  // 1) 무제한 측정 → 세로 1열 자연 높이/너비
  root.Measure(∞, ∞)
  desiredHeight = ceil(DesiredSize.Height * scale)  // ≤0이면 InitialPopupHeight
  // 2) 높이 상한(작업영역 높이) 클램프 → 초과 시 세로 스크롤 Auto
  maxHeight = max(InitialPopupHeight, Work.Height - WorkAreaMargin)
  finalHeight = min(desiredHeight, maxHeight)
  if (desiredHeight > maxHeight) { SetVerticalScrollMode(Auto); SetVerticalScrollBarVisibility(Auto); }
  // 3) 확정 높이로 너비 재측정(세로 스크롤바 폭 반영)
  root.Measure(∞, finalHeight/scale)
  desiredWidth = ceil(DesiredSize.Width * scale)    // ≤0이면 InitialPopupWidth
  // 4) 너비도 상한 클램프 (긴 그룹명/NotFound 메시지로 좁은 작업영역 초과 방지) — B2
  maxWidth = max(InitialPopupWidth, Work.Width - WorkAreaMargin)
  finalWidth = min(desiredWidth, maxWidth)
  // 5) chrome 보정(양축) 후 Resize, _popupWidth 갱신(우측 정렬 필수) — B1
  windowWidth = finalWidth + hChrome;  windowHeight = finalHeight + chrome  // windowHeight는 현행 가로 경로의 total과 동일 의미
  if (windowWidth==_lastAppliedWidth && windowHeight==_lastAppliedHeight) return;
  _lastAppliedWidth = windowWidth; _lastAppliedHeight = windowHeight; _popupWidth = windowWidth;
  AppWindow.Resize(windowWidth, windowHeight);
  if (_positioned) MoveToTaskbar(windowHeight);
  ```
- **정합성 근거(B1)**: `MoveToTaskbar`는 `TaskbarPopupPositioner.Compute(... _popupWidth, height)`를 호출하고, `Right` 변은 `work.Right - popupWidth`로 우측 정렬한다 → 세로(우측 작업 표시줄)에서 `_popupWidth`가 실제 창 너비여야 변에 정확히 접한다. 따라서 세로 경로도 `_popupWidth = windowWidth` 갱신 + `MoveToTaskbar(windowHeight)` 전달 필수.
- **축별 스크롤 리셋(M2)**: 가로 경로는 시작 시 가로축만 Disabled 리셋(현행), 세로축은 항상 Disabled. 세로 경로는 시작 시 양축 Disabled 리셋 후 세로축만 초과 시 Auto. → 재측정(SizeChanged 재진입) 때마다 분기에 맞는 축만 켜져 자연 길이 오보고 재발 차단.
- **공통 유지**: `_lastAppliedWidth/Height` 가드, `chrome = Size.Height - ClientSize.Height`(음수 0 보정), `hChrome = Size.Width - ClientSize.Width`(음수 0 보정)는 양 경로 공통.
- **Acceptance**: 세로 표시 시 창이 아이콘 열 콘텐츠에 꼭 맞고(빈 여백 없음), 아이콘이 작업영역 높이를 넘으면 세로 스크롤이 생겨 잘리지 않으며, 너비가 작업영역을 넘으면 너비 상한으로 클램프되어 화면 밖으로 나가지 않는다. 우측 작업 표시줄에서 팝업 오른쪽 변이 작업영역 우변에 접한다. 가로 표시는 기존과 동일(회귀 없음). 빌드 0/0, 기존 테스트 통과.
- **Edge Cases**:
  - 세로 콘텐츠가 작업영역 높이 초과: 세로 스크롤 Auto, 너비 재측정에 스크롤바 폭 반영(잘림 방지).
  - **세로 너비가 작업영역 폭 초과(긴 그룹명/NotFound)**: `maxWidth` 클램프로 화면 밖 이탈 방지(B2). 클램프 시 헤더는 기존 TextBlock 동작(줄바꿈/잘림)에 맡김(템플릿 무변경).
  - 콘텐츠 측정값 0 이하: 초기값(`InitialPopupWidth/Height`)으로 폴백(현행 가드 유지).
  - SizeChanged 재진입: `_lastAppliedWidth/Height` 동일값이면 조기 반환(무한 루프 차단, 현행 유지).
- **Halt Forecast**: 세로 측정에서 StackPanel(Vertical) 자연 높이가 부정확 보고되면(비가상화라 가능성 낮음) 측정 전 양축 스크롤 Disabled 보장으로 해소. 그래도 불가 시 Halt하지 말고 가로의 "측정 동안 스크롤 끄기" 패턴을 재확인.

### T3. 문서 갱신 (Type A)
- **Files**: `README.md`(핵심 기능 "그룹 팝업 런처" 설명에 좌/우 작업 표시줄 시 세로 표시 반영), `notes.md`(`## 최근 변경` 최상단에 항목 추가, 1개월 초과 항목 정리).
- **Acceptance**: README의 그룹 팝업 설명이 현재 동작(상/하 가로, 좌/우 세로)과 일치. notes에 변경 내역 1건 추가.

## Decision Points (결정 완료)
- **D1. 세로 전환 트리거** = `TaskbarEdge.Left` 또는 `Right`일 때만. (사용자 결정: 좌/우만 세로, 상/하 가로) ★
- **D2. 적용 범위** = `GroupPopupWindow`만. 폴더 팝업 제외. (사용자 결정) ★
- **D3. 세로 레이아웃 세부** = 헤더(그룹 이름)는 상단 유지, "+" 버튼은 목록 끝(아래), 오버플로는 세로 스크롤. (사용자 결정: 추천안) ★
- **D4. ItemsPanel 전환 방식** = XAML에 가로/세로 `ItemsPanelTemplate`을 리소스로 두고 생성자에서 `_isVertical`에 따라 `AppsGrid.ItemsPanel` 지정. (코드 1회 결정, 깜빡임/재생성 최소화) ★ 근거: 아이템 로드 전 1회 적용.
- **D5. 위치 계산** = `TaskbarPopupPositioner` 무변경(이미 4변 처리). ★ 근거: 단위 테스트 4건 통과 확인.
- **D6. 측정 분기 구조** = 가로 경로는 현행 로직 보존, 세로는 대칭 신설. ★ 근거: 회귀 위험 최소화.
- **D7. 신규 테스트** = 없음(UI 코드비하인드, 헤드리스 GUI 관찰 불가). 기존 `TaskbarPopupPositionerTests` 회귀 가드. ★
- **D8. 다국어** = 신규 리소스 키 없음(텍스트 변경 없음). ★
- **D9. 에러/폴백** = `DetectEdge` 판정 불가 시 Bottom(가로) 폴백, 측정값 0 이하 시 초기값 폴백 — 현행 가드 재사용. ★
- **D10. chrome 보정** = 세로 경로도 너비/높이 양쪽 chrome 보정 유지(잘림 방지). ★
- **D11. WorkAreaMargin 재사용** = 동일 상수(24)를 세로에선 높이 여백으로 재사용. ★ 근거: 좌우/상하 여백 대칭, 신규 상수 불필요.
- **D12. 세로 너비 상한** = 세로 경로도 `maxWidth = Work.Width - WorkAreaMargin`로 너비 클램프(B2). ★ 근거: 긴 그룹명/NotFound 메시지로 좁은 좌/우 작업영역에서 화면 밖 이탈·잘림 방지. 너비 초과 시 헤더는 기존 TextBlock 동작(줄바꿈/잘림)에 맡김(템플릿 무변경, 신규 스크롤 도입 안 함).
- **D13. _popupWidth 정합성** = 세로 경로도 `_popupWidth = windowWidth` 갱신 + `MoveToTaskbar(windowHeight)` 전달(B1). ★ 근거: `Compute`의 `Right` 변 우측 정렬(`work.Right - popupWidth`)이 정확한 창 너비를 요구.
- **D14. 축별 스크롤 리셋** = 측정 시작 시 분기에 맞는 축만 Disabled 리셋, 반대 축은 항상 Disabled 유지(M2). ★ 근거: SizeChanged 재진입 시 자연 길이 오보고(가로에서 겪은 문제) 재발 차단.

## 검증 방법
1. `dotnet build WorkGroup.slnx` → 경고 0 / 에러 0.
2. `dotnet test WorkGroup.slnx` → 기존 테스트 전부 통과(`TaskbarPopupPositionerTests` 포함 회귀 없음).
3. 수정 후 변경 파일 재확인(누락·UTF-8/BOM·한글 주석).
4. **F5 MSIX GUI 수동 검증**(헤드리스 불가): 작업 표시줄을 좌/우/상/하로 옮겨가며 핀 클릭 → 좌/우는 세로 1열·해당 변에 접함, 상/하는 가로 1행, 오버플로 스크롤 방향, 빈 여백 없음, 헤더/"+" 위치 확인.

## 승인 필요 항목
- 없음(공개 API·구조·의존성·스키마 변경 없음, 단일 View 2파일 + 문서). 사용자 승인 게이트(ExitPlanMode)만.

## Open Questions (모두 해결)
- 세로 전환 범위 → 좌/우만(D1). 적용 대상 → 그룹 팝업만(D2). 세로 레이아웃 → 추천안(D3). 모두 사용자 답변 반영 완료.
