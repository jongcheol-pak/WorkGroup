# Plan: 핀 팝업에 "그룹 편집" 추가 버튼

## Goal
핀된 그룹 아이콘 클릭 시 뜨는 팝업(`GroupPopupWindow`)의 앱 아이콘 목록 **끝에 "+" 추가 버튼**을 한 항목으로 넣고, 클릭하면 해당 그룹의 **그룹 편집 다이얼로그**(메인 창)를 연다. 동작은 기존 우클릭 메뉴 "그룹 수정"(`OnEditGroupClick`)과 동일하다.

## Out of Scope
- 우클릭 컨텍스트 메뉴("열기 / 관리자 권한으로 실행 / 그룹 수정") 변경 — 그대로 유지.
- 메인 창 그룹 편집 다이얼로그(`GroupEditDialog`/`WorkGroupsPage`/`App.RouteEditRequest`) 흐름 변경 — 기존 경로 그대로 재사용.
- 새 다국어 리소스 키 추가 — 버튼 툴팁은 기존 `GroupPopup_EditGroup` 재사용.
- 팝업 위치/크기 측정 로직(`AdjustToContent`) 알고리즘 변경 — 버튼을 GridView **내부 항목**으로 넣어 기존 한 줄 측정에 자연히 포함시킨다.
- 폴더 바로가기 팝업(`FolderListPopupWindow`) 등 다른 팝업.

## Investigation Log
- `AGENTS.md`/`README.md` 읽음 → DDD 4레이어(App→Infrastructure→Application→Domain), 빌드 `dotnet build WorkGroup.slnx`, 테스트 `dotnet test WorkGroup.slnx`, 헤드리스에서 GUI 관찰 불가(빌드까지만 검증). 파일 UTF-8(BOM 없음), 한글 주석.
- `Views/GroupPopupWindow.xaml` 읽음 → GridView(`x:Name="AppsGrid"`)가 `ItemsSource="{x:Bind Items}"`, ItemsPanel은 비가상화 `StackPanel Orientation="Horizontal"`(한 줄 나열), `IsItemClickEnabled="True"`/`ItemClick="OnAppClick"`. ItemTemplate `x:DataType="vm:PopupAppItem"`에 호버 애니메이션(`OnIconPointerEntered/Exited`)·우클릭 ContextFlyout·48×48 아이콘 박스.
- `Views/GroupPopupWindow.xaml.cs` 읽음 →
  - `public ObservableCollection<PopupAppItem> Items { get; } = new();` (line 51) — 이 창 전용.
  - 생성자 `GroupPopupWindow(string groupId)`가 `_groupId` 보관, `LoadAsync(groupId)` 호출.
  - `LoadAsync`(line 87): `_groupService.GetAllAsync()`로 그룹 조회 → **없으면 NotFound 메시지 표시 후 `return`(Items 비움)**, 있으면 `group.Apps`를 `PopupAppItem`으로 Items에 추가. 예외 시 catch에서 LoadFailed 메시지. finally에서 `RevealAtTaskbar`.
  - `OnAppClick`(line 195): `if (e.ClickedItem is PopupAppItem item) { _launcher.Launch(item.App); Close(); }`.
  - `OnEditGroupClick`(line 226): `WorkGroupPaths.AliasExePath`를 `GroupArgs.BuildEditCommandLineArguments(_groupId)` 인자로 `Process.Start`(UseShellExecute) → single-instance 메인 앱이 편집 다이얼로그 표시. 이후 `Close()`.
  - `AdjustToContent`(line 133): `root`(콘텐츠) 전체를 무한 너비로 Measure해 한 줄 자연 너비 측정 → 작업영역 폭 상한 클램프, 초과 시 GridView 가로 스크롤. **버튼을 GridView 내부 마지막 항목으로 넣으면 이 측정에 자동 포함**되고 오버플로우 시 함께 스크롤됨.
- `ViewModels/PopupAppItem.cs` 읽음 → `sealed`, `AppEntry App`, `DisplayName`, `CanRunAsAdmin`, `Icon`, `IsSelected`/`SelectionGlyphVisibility`, `LoadIconAsync()`. **변경하지 않음**.
- grep `\.Items\b|GroupPopupWindow|PopupAppItem` (App 전체) →
  - `GroupPopupWindow.Items`는 외부 사용처 **0**(이 창 XAML x:Bind 전용) → 컬렉션 타입 변경 안전.
  - `PopupAppItem`은 `GroupEditDialog.xaml`/`WorkGroupsPage.xaml`/`GroupListItem.cs`/`GroupEditViewModel.cs` 등에서 사용 → 본 작업은 `PopupAppItem`을 **수정하지 않으므로** 영향 없음.
  - `GroupPopupWindow` 생성: `App.xaml.cs:86,221` (생성자 시그니처 변경 없음 → 영향 없음).
- grep selector/Converters/Selectors 폴더 → **없음**. 기존 `DataTemplateSelector` 미사용 → 본 작업에서 신규 도입(표준 WinUI 패턴).
- 리소스 키 `GroupPopup_EditGroup` → ko/en/ja/zh 4개 `.resw` 모두 존재(버튼 툴팁 재사용 가능, 신규 키 불필요).
- 글리프: "+" = Segoe Fluent Icons `&#xE710;`(Add) — 기존 코드베이스에서 FontIcon 글리프 직접 사용 패턴 확인됨(`E8A7`, `E7EF`, `E70F`).

## 결정 사항 (Decision Points — 사용자 확인 완료)
- **D1. 버튼 배치/스크롤**: GridView **내부 마지막 항목**으로 추가(앱과 함께 스크롤). → 측정 로직 무변경, 회귀 위험 최소. (사용자 선택)
- **D2. 표시 조건**: **정상 그룹이면 멤버 0개여도 항상 표시**, 그룹을 못 찾은 경우(NotFound)에만 숨김. 로드 예외(LoadFailed) 시에도 추가하지 않음(정상 로드 성공 경로에서만 버튼 항목 추가). (사용자 선택)
- **D3. 혼합 항목 구현**: GridView에 두 타입(앱/추가버튼)을 담기 위해 `Items` 타입을 `ObservableCollection<object>`로 바꾸고 `DataTemplateSelector`로 템플릿 분기. (`PopupAppItem` 모델 오염 방지 — 새 마커 클래스 `PopupAddButtonItem` 도입)
- **D4. 버튼 스타일**: 앱 템플릿의 **2중 구조를 그대로 복제**한다 — 외부 `StackPanel`(Width/Height 48, Spacing 4, Padding 4, `HorizontalAlignment="Center"`, `RenderTransformOrigin="0.5,0.5"`, 호버 핸들러 `OnIconPointerEntered/Exited`, 그리고 **`<StackPanel.RenderTransform><ScaleTransform/></StackPanel.RenderTransform>` 명시**[호버 확대 필수 조건 — `AnimateIconScale`는 sender에 `ScaleTransform`이 있을 때만 동작]) + 내부 `Grid`(`ControlFillColorDefaultBrush` 배경, `CornerRadius="6"`, `Padding="6"`, Stretch). 내부의 `Image`를 `<FontIcon Glyph="&#xE710;" />`(중앙 정렬)로 치환. 툴팁 = `{loc:Localize Key=GroupPopup_EditGroup}`. 우클릭 ContextFlyout 없음.
- **D5. 클릭 동작**: 좌클릭(ItemClick) 시 기존 `OnEditGroupClick` **본문 전체(메인앱 `--edit-group` 실행 + 끝의 `Close()` 포함)** 를 `EditGroup()` 공통 메서드로 추출해 우클릭 메뉴와 추가버튼이 공유. 두 진입점 모두 실행 후 팝업이 닫힌다.

## Tasks

### T1 — 마커 클래스 + DataTemplateSelector 추가  [Type C]
- **Files**:
  - 신규 `src/WorkGroup.App/ViewModels/PopupAddButtonItem.cs` — 빈 마커 클래스(`sealed`, 인스턴스 식별용). 한글 주석으로 용도 명시.
  - 신규 `src/WorkGroup.App/Views/PopupGridItemTemplateSelector.cs` — `DataTemplateSelector` 파생. `public DataTemplate? AppTemplate`, `public DataTemplate? AddButtonTemplate` 프로퍼티. `SelectTemplateCore(object item)` 및 `SelectTemplateCore(object item, DependencyObject container)`에서 `item is PopupAddButtonItem ? AddButtonTemplate : AppTemplate` 반환.
- **Acceptance**: 두 파일이 컴파일되고, selector가 `PopupAddButtonItem`→`AddButtonTemplate`, 그 외→`AppTemplate`를 반환한다(빌드 통과로 확인).
- **Edge Cases**: `item`이 null이거나 두 타입 모두 아님 → `AppTemplate` 반환(기본). `AddButtonTemplate`/`AppTemplate`가 null이면 selector는 null 반환(GridView 기본 처리) — XAML에서 항상 StaticResource로 주입하므로 실제로는 비-null.
- **Halt Forecast**: WinUI `DataTemplateSelector`의 `using Microsoft.UI.Xaml.Controls;` 네임스페이스 확인 필요(WPF 아님). → 동일 네임스페이스 사용.

### T2 — GroupPopupWindow.xaml: 템플릿 분리 + selector + "+" 템플릿  [Type D]
- **Files**: `src/WorkGroup.App/Views/GroupPopupWindow.xaml`
- **변경**:
  1. `xmlns:views="using:WorkGroup.App.Views"` 네임스페이스 추가(selector 참조용).
  2. 기존 인라인 `GridView.ItemTemplate`(앱 항목 DataTemplate)을 `GridView.Resources` 안의 `<DataTemplate x:Key="AppItemTemplate" x:DataType="vm:PopupAppItem">`로 이동(내용 동일 — 호버 핸들러·ContextFlyout·아이콘 박스 유지).
  3. `GridView.Resources`에 `<DataTemplate x:Key="AddButtonTemplate" x:DataType="vm:PopupAddButtonItem">` 추가 — **D4의 2중 구조를 그대로 복제**: 외부 StackPanel(48×48, Spacing 4, Padding 4, `RenderTransformOrigin="0.5,0.5"`, `OnIconPointerEntered/Exited` 핸들러, **`<StackPanel.RenderTransform><ScaleTransform/>` 명시**) + 내부 Grid(배경 `ControlFillColorDefaultBrush`, `CornerRadius="6"`, `Padding="6"`) 중앙에 `<FontIcon Glyph="&#xE710;" />`. 툴팁 `{loc:Localize Key=GroupPopup_EditGroup}`. ContextFlyout 없음.
  4. `GridView.Resources`에 `<views:PopupGridItemTemplateSelector x:Key="GridItemSelector" AppTemplate="{StaticResource AppItemTemplate}" AddButtonTemplate="{StaticResource AddButtonTemplate}" />`.
  5. `GridView`에서 `ItemTemplate` 제거하고 `ItemTemplateSelector="{StaticResource GridItemSelector}"` 지정.
  - 기존 `GridViewItemBackgroundPointerOver/Pressed` 투명 브러시 리소스는 Resources에 유지.
- **Acceptance**: XAML 컴파일 통과. (GUI 수동 확인은 사용자 몫 — 헤드리스 한계 명시)
- **Edge Cases**: selector 사용 시 두 DataTemplate 모두 `x:DataType` 지정으로 x:Bind 컴파일 보장. 추가버튼 템플릿엔 `x:Bind` 데이터 바인딩 없음(정적 글리프).
- **Halt Forecast**: `GridView.Resources`에 `DataTemplate`과 selector 인스턴스를 함께 둘 때 `x:Key` 충돌/StaticResource 전방참조 주의 — selector를 DataTemplate들 **뒤**에 선언.

### T3 — GroupPopupWindow.xaml.cs: Items 타입 + 추가버튼 주입 + 클릭 분기  [Type D]
- **Files**: `src/WorkGroup.App/Views/GroupPopupWindow.xaml.cs`
- **변경**:
  1. `public ObservableCollection<PopupAppItem> Items` → `public ObservableCollection<object> Items`(앱 항목 + 추가버튼 혼합). 한글 주석 갱신.
  2. `LoadAsync` 정상 그룹 경로에서 `group.Apps` 항목들을 모두 추가한 **뒤**, `Items.Add(new PopupAddButtonItem());`로 마지막에 추가버튼 항목을 넣는다(D2: NotFound `return`/예외 catch 경로에는 추가하지 않음). 멤버 0개여도 정상 그룹이면 버튼만 단독 추가.
  3. `OnAppClick`: `if (e.ClickedItem is PopupAppItem item) { _launcher.Launch(item.App); Close(); } else if (e.ClickedItem is PopupAddButtonItem) { EditGroup(); }` 형태로 분기.
  4. `OnEditGroupClick` 본문 **전체(try-catch Process.Start + 끝의 `Close()` 포함)** 를 `private void EditGroup()`(공통 메서드)로 추출하고, 기존 `OnEditGroupClick`은 `EditGroup()` 호출만 남긴다(우클릭 메뉴/추가버튼 공유 — D5). 추가버튼 클릭 시에도 실행 후 팝업이 닫힌다.
- **Acceptance**: 빌드 통과. 코드 경로상 NotFound/예외 시 Items에 추가버튼 미포함, 정상 시 마지막에 1개 포함(코드 리뷰로 확인).
- **Edge Cases**:
  - 멤버 0개 정상 그룹: Items에 추가버튼 1개만 → `AdjustToContent`가 버튼 1개 너비로 측정(정상).
  - 오버플로우(앱 다수): 추가버튼이 마지막 항목이라 가로 스크롤에 함께 포함.
  - 추가버튼 호버 애니메이션: 앱 항목과 동일 핸들러 재사용. **`AnimateIconScale`는 sender에 `ScaleTransform`이 있을 때만 동작**하므로 추가버튼 템플릿 외부 StackPanel에 `RenderTransform=ScaleTransform`을 반드시 명시(D4) — 누락 시 조용히 무반응(헤드리스 미검출).
- **Halt Forecast**: `Items` 타입 변경 후 `OnAppClick`의 `is PopupAppItem` 패턴이 object 컬렉션에서도 정상(이미 패턴 매칭). 컴파일 경고 없을 것.

### T4 — 빌드 검증 + 문서 갱신  [Type A]
- **Files**: `README.md`, `notes.md`
- **변경**:
  - `dotnet build WorkGroup.slnx`로 경고/에러 0 확인.
  - `README.md` "그룹 팝업 런처" 항목에 "팝업 앱 목록 끝의 '+' 버튼으로 해당 그룹 편집 다이얼로그를 연다(우클릭 '그룹 수정'과 동일)" 한 줄 보강.
  - `notes.md` `## 최근 변경` 최상단에 `- 2026-06-04: 핀 팝업 앱 목록 끝에 '+' 그룹 편집 버튼 추가` 추가, 1개월 초과 항목 정리.
- **Acceptance**: 빌드 성공(경고 0), 문서에 신규 동작 반영.

## 의존 관계
- T1 → T2(selector를 XAML이 참조) → T3(혼합 컬렉션·클릭 분기) → T4(검증/문서). 순차 진행.

## 검증 방법
- `dotnet build WorkGroup.slnx` (경고/에러 0).
- 코드 리뷰로 D2(표시 조건)·D5(클릭 동작 공유) 충족 확인.
- GUI 실제 동작은 MSIX 배포(F5)가 필요해 헤드리스 자율 실행에서는 확인 불가 → **사용자 수동 확인 항목**으로 보고:
  - 핀 팝업 앱 목록 끝에 "+" 버튼 표시(멤버 0개 그룹도 버튼 단독 표시).
  - "+" 버튼 마우스 오버 시 호버 확대 동작.
  - "+" 버튼 클릭 → 메인 창의 해당 그룹 편집 다이얼로그 표시.

## 승인 필요 사항
- 신규 파일 2개(`PopupAddButtonItem.cs`, `PopupGridItemTemplateSelector.cs`) 추가 및 `GroupPopupWindow.Items` 공개 프로퍼티 타입 변경(`<PopupAppItem>`→`<object>`, 외부 사용처 없음). → 본 plan 승인으로 갈음.

## 자율 실행 준비도
- 구현 중 사용자 결정 분기 남음? **없음**(D1~D5 확정).
- 다른 사람이 이 plan만으로 끝낼 수 있는가? **예**.
- 검증 가능한 acceptance 모두 존재? **예**.
