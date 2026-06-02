# plan.md — 그룹 수정 화면: 이름 클릭-편집 + 핀 재등록 경고

> 이전 plan들은 완료(git 이력). 본 plan은 그룹 수정 다이얼로그의 이름 편집 UX 개선.

## 목표
그룹 **수정** 화면에서:
1. 그룹 이름을 처음엔 **읽기전용 표시**로 보여주고, **클릭하면 입력창(TextBox)으로 전환**한다.
2. 입력한 이름이 **원래 이름과 달라지면**, 이름 입력 UI 아래에 **핀 재등록 경고**를 표시한다.

신규 **추가** 화면은 기존과 동일(이름을 처음부터 바로 입력, 경고 없음).

## 배경 / 근본 원인 (확인된 사실)
- 작업 표시줄에 핀된 항목의 표시 이름은 핀 복사본의 **파일명으로 고정**되어, 그룹 이름을 바꿔도 자동으로 바뀌지 않는다(프로그래밍 방식 변경은 Windows 제약상 불가). 따라서 사용자가 직접 **핀을 제거하고 다시 핀**해야 한다 → 이를 경고로 안내한다.
- 현재 `GroupEditDialog.xaml`(56-58행)의 이름 입력은 항상 편집 가능한 `TextBox`(Header="그룹 이름", MaxLength=15, `EditingName` TwoWay 바인딩).
- 편집/신규 구분은 `GroupEditViewModel`의 `_editingId`(InitializeAsync에서 `group?.Id`로 설정). 편집이면 `Title="그룹 수정"`.
- `EditingName`은 `[ObservableProperty]`. `_existingNames`로 중복 검사(편집 시 자기 제외).
- 검증 메시지는 `StatusMessage` → `InfoBar`(GroupEditDialog.xaml:61, Severity=Warning)로 표시(빈 목록/중복 이름). **핀 경고는 이와 별개의 InfoBar로 추가**한다(역할 분리).

## 영향 범위 (전수 조사 결과)
- `GroupEditViewModel` 참조처: `ServiceConfiguration.cs`(DI 등록), `GroupEditDialog.xaml`(x:Bind), `GroupEditDialog.xaml.cs`(사용), `WorkGroupsPage.xaml.cs`(다이얼로그 호출). → **공개 메서드 시그니처 변경 없음**. 추가하는 것은 ViewModel의 바인딩용 속성뿐이라 기존 호출자 영향 없음.
- `WorkGroupsPage.xaml.cs`는 `GroupEditDialog`를 `Configure(group)` 후 `ShowAsync`로 호출(편집/신규 모두). → 변경 불필요(InitializeAsync 내부에서 모드 분기).
- **테스트**: `GroupEditViewModel`/`GroupEditDialog`에 대한 단위 테스트 없음. `WorkGroup.App`은 `UseWinUI=true`/`WinExe`(패키지 WinUI 실행 파일)이고 **테스트 프로젝트가 App을 참조하지 않아** ViewModel 단위 테스트가 불가하다(Application.Tests TFM은 net10.0-windows로 App과 동일하나 참조가 없음). → 테스트 추가 불가, **빌드 + 수동 GUI 검증**으로 한다(AGENTS.md: 패키지 앱 GUI는 수동).

## Out of Scope (명시)
- 핀 표시 이름의 **프로그래밍 방식 자동 변경/재등록**(Windows 작업 표시줄 핀 제약상 불가 — 조사 완료). 본 작업은 사용자에게 수동 재등록을 **안내**만 한다.
- **신규 추가 모드 UX 변경**(기존 동작 유지).
- 아이콘 변경 관련 경고/처리(이름 변경 경고만 대상).

## 결정 사항 (사용자 확정)
- **D1. 이름 편집 방식**: 읽기전용 표시 → 클릭 시 입력창 전환. (사용자 답변)
- **D2. 적용 범위**: **수정 모드에만**. 신규 추가는 기존대로 바로 입력 + 경고 없음. (사용자 답변)
- **D3. 경고 표시 조건**: 입력 이름이 **원래 이름과 실제로 달라졌을 때만**. 원래대로 되돌리면 경고 사라짐. (사용자 답변)
- **D4. 경고 문안**: **"이름을 변경하면 작업 표시줄에 핀한 기존 항목을 제거하고 다시 핀해야 합니다."** (사용자 답변)

## 결정 사항 (기술결정 — 추가 입력 불필요)
- **D5. 읽기전용 표시 컨트롤**: 읽기전용 모드는 세로 `StackPanel`로 구성 — ① 라벨 `TextBlock`("그룹 이름", `Style="{ThemeResource CaptionTextBlockStyle}"`, TextBox Header와 동일 위치/톤) + ② 투명 배경 `Button`(Click="OnEditNameClick", `HorizontalAlignment=Stretch`, `HorizontalContentAlignment=Left`) 안에 `StackPanel`(Orientation=Horizontal, Spacing=6) → 이름 `TextBlock`(`EditingName` OneWay, `TextTrimming=CharacterEllipsis`) + 편집 힌트 `FontIcon`(연필 글리프 `&#xE70F;`, FontSize=14). Button을 쓰면 클릭/키보드 포커스/접근성이 자동 확보(TextBlock+Tapped 대비 우수). 이름 칼럼 컨테이너는 기존과 동일하게 `VerticalAlignment="Bottom"` 유지(아이콘 56px와 하단 정렬). TextBox(입력창)는 자체 `Header="그룹 이름"`를 유지하므로 두 모드 모두 동일 라벨이 보인다.
- **D6. 전환 상태 모델**: ViewModel에 `IsNameEditing`(bool) 추가. 신규=초기 true(바로 입력), 수정=초기 false(읽기전용). 읽기전용 Button 클릭 시 true로 전환(되돌리지 않음 — 한 번 편집 시작하면 입력창 유지, 단순/예측가능).
- **D7. 경고 노출 모델**: 계산 속성 `ShowRenameWarning => IsEditMode && EditingName.Trim() != _originalName`(Ordinal 비교). `EditingName`/`IsEditMode` 변경 시 `NotifyPropertyChangedFor`로 통지.
- **D8. 경고 위치**: 이름 행 **바로 아래 새 Grid 행**에 전용 `InfoBar`(Severity=Warning, IsClosable=False, Message=D4 고정 문구). 기존 `StatusMessage` InfoBar(검증용)는 그 아래로 한 행 이동. → 요청("입력 UI 아래")에 정확히 부합, 전체 폭 표시.
- **D9. 포커스**: 읽기전용→입력창 전환 시 `TextBox.Focus(Programmatic)` + `SelectAll()`로 즉시 편집 가능하게. Visibility 전환 직후 포커스가 누락되지 않도록 `DispatcherQueue.TryEnqueue`로 한 틱 양보 후 Focus(Edge Case E2).

## 작업 분해

### T1. GroupEditViewModel — 편집모드/이름편집전환/이름변경경고 상태 추가 (Type C)
- **Files**: `src/WorkGroup.App/ViewModels/GroupEditViewModel.cs`
- **변경**:
  - 필드 `private string _originalName = string.Empty;`(편집 시작 시 원래 이름).
  - `[ObservableProperty] public partial bool IsEditMode { get; set; }` — `[NotifyPropertyChangedFor(nameof(ShowRenameWarning))]`.
  - `[ObservableProperty] public partial bool IsNameEditing { get; set; }` — `[NotifyPropertyChangedFor(nameof(NameDisplayVisibility))]` + `[NotifyPropertyChangedFor(nameof(NameEditVisibility))]`.
  - 계산 속성: `ShowRenameWarning`(D7), `NameDisplayVisibility`(IsNameEditing?Collapsed:Visible), `NameEditVisibility`(반대).
  - `EditingName`에 `[NotifyPropertyChangedFor(nameof(ShowRenameWarning))]` 부착.
  - `InitializeAsync` **설정 순서(중요)**: 기존 `EditingName = group?.Name ?? string.Empty;`(현재 110행) **이전에** `_originalName = group?.Name ?? string.Empty;`와 `IsEditMode = group is not null;`, `IsNameEditing = group is null;`(신규 즉시 입력)를 **먼저** 설정한다. 그래야 EditingName 설정이 유발하는 `ShowRenameWarning` 통지 시점에 `_originalName`/`IsEditMode`가 이미 올바른 값을 갖는다. 안전을 위해 위 설정 직후 `OnPropertyChanged(nameof(ShowRenameWarning))`도 1회 호출(초기 상태 보정).
- **Acceptance**: 신규 모드 초기 `IsNameEditing=true`·`IsEditMode=false`·`ShowRenameWarning=false`; 편집 모드 초기 `IsNameEditing=false`·`IsEditMode=true`·`ShowRenameWarning=false`(이름 미변경), 이름을 다른 값으로 바꾸면 `ShowRenameWarning=true`, 원복하면 다시 false. (빌드 통과 + 로직 코드 검토로 확인)
- **Edge Cases**: 빈 이름 입력(Trim 후 "" ≠ 원래이름이면 경고 표시 — 정상, 저장은 기존 검증이 막음); 공백만 입력(Trim 처리); 이름 앞뒤 공백 차이(Trim 비교라 공백만 추가 시 경고 안 뜸 — 수용).
- **Halt Forecast**: CommunityToolkit partial property/Notify 특성 사용은 기존 코드(EditingName 등)와 동일 패턴이라 컴파일 이슈 낮음. 빌드 에러 시 기존 ObservableProperty 선언 형태를 그대로 따른다.

### T2. GroupEditDialog.xaml — 이름 읽기전용↔입력 전환 UI + 경고 InfoBar (Type C)
- **Files**: `src/WorkGroup.App/Views/GroupEditDialog.xaml`
- **변경**:
  - 1행 이름 칼럼(Column1)을 컨테이너 Grid로 바꿔 두 요소를 겹쳐 둔다:
    - 읽기전용 표시(`NameDisplayVisibility`): Header 라벨 + 투명 `Button`(Click="OnEditNameClick", 좌측 정렬) 안에 이름 `TextBlock`(`EditingName` OneWay) + 연필 `FontIcon`.
    - 입력창(`NameEditVisibility`): 기존 `TextBox`에 `x:Name="NameTextBox"` 부여, MaxLength=15, `EditingName` TwoWay 유지.
  - **RowDefinitions 재구성(현재 4행 → 5행)**: 현재 `[Auto, Auto, Auto, 300]`(이름 Row0 암묵, 검증InfoBar Row1, 앱추가 Row2, 목록 Row3). 변경 후 `[Auto(이름), Auto(경고), Auto(검증InfoBar), Auto(앱추가), 300(목록)]`.
  - **Grid.Row 명시 매핑(현재값→새값, 누락 금지)**:
    | 요소 | 현재 Grid.Row | 새 Grid.Row |
    |---|---|---|
    | 이름 칼럼 Grid (xaml L25) | 미지정(0) | 미지정(0) 유지 |
    | **경고 InfoBar (신규)** | — | **1** |
    | 검증 StatusMessage InfoBar (L61) | 1 | **2** |
    | "앱 추가" Button (L65) | 2 | **3** |
    | 선택 목록 ListView (L110) | 3 | **4** |
  - 경고 `InfoBar`(Grid.Row=1, IsOpen=`ShowRenameWarning`, Severity=Warning, IsClosable=False, Message=D4 문구)를 이름 행 바로 아래에 배치.
  - **검증**: 편집 완료 후 모든 직속 자식의 Grid.Row가 위 표와 일치하는지, RowDefinitions가 5개인지 재확인(특히 마지막 300px 고정 행이 목록에 매핑되는지).
- **Acceptance**: 수정 다이얼로그 진입 시 이름이 읽기전용 표시로 보이고, 클릭하면 입력창으로 바뀐다. 이름을 바꾸면 입력 UI 아래 경고 InfoBar가 열리고, 원복하면 닫힌다. 신규 다이얼로그는 처음부터 입력창·경고 없음. (빌드 통과 + 수동 GUI 검증)
- **Edge Cases**: 긴 이름(15자)일 때 읽기전용 TextBlock 트리밍(`TextTrimming=CharacterEllipsis`); 경고 InfoBar 추가로 다이얼로그 세로 높이 증가(ContentDialog 자동 확장 — 행 높이 Auto라 미표시 시 0).
- **Halt Forecast**: Grid.Row 인덱스 누락/중복 시 레이아웃 깨짐 → 변경 후 모든 자식의 Grid.Row를 재확인. x:Bind 속성명 오타 시 빌드 에러(컴파일 바인딩).

### T3. GroupEditDialog.xaml.cs — 이름 클릭→편집 전환 핸들러 (Type C)
- **Files**: `src/WorkGroup.App/Views/GroupEditDialog.xaml.cs`
- **변경**: `OnEditNameClick(object, RoutedEventArgs)` 추가 — `ViewModel.IsNameEditing = true;` 후 `this.DispatcherQueue.TryEnqueue(() => { NameTextBox.Focus(FocusState.Programmatic); NameTextBox.SelectAll(); })`. `DispatcherQueue`는 `ContentDialog`(DependencyObject 상속)의 인스턴스 속성 `this.DispatcherQueue`로 접근(WinUI 3 표준).
- **Acceptance**: 읽기전용 이름 클릭 시 입력창으로 전환되고 즉시 텍스트가 선택되어 편집 가능. (수동 GUI 검증)
- **Edge Cases (E2)**: Visibility 전환 직후 즉시 Focus가 무시될 수 있어 DispatcherQueue로 한 틱 양보. SelectAll은 포커스 후 호출.
- **Halt Forecast**: `NameTextBox`는 T2에서 x:Name 부여 필수(없으면 컴파일 에러) → T2/T3 함께 검증.

### T4. 문서 갱신 (Type A)
- **Files**: `README.md`, `notes.md`
- **변경**: README의 그룹 수정 화면 설명에 "이름 클릭-편집 + 핀 재등록 경고" 반영(현재 기능만, 과장 없이). notes.md `## 최근 변경` 최상단에 본 작업 1줄 추가. 1개월 초과 항목 정리.
- **Acceptance**: 문서가 실제 동작과 일치.

## 위험 / 회귀
- ContentDialog 높이 변동: 경고 InfoBar는 행 높이 Auto라 미표시 시 레이아웃 영향 없음.
- 기존 검증 InfoBar(StatusMessage)와 경고 InfoBar 동시 표시 가능(중복 이름 + 이름 변경) → 의도된 동작(역할 분리). 세로로 쌓임.
- 신규 추가 흐름 회귀 없음: IsNameEditing=true로 시작해 기존과 동일하게 바로 입력.

## 검증 방법
- `dotnet build WorkGroup.slnx` — 경고/에러 0 확인.
- `dotnet test WorkGroup.slnx` — 기존 테스트 회귀 없음(이 변경은 테스트 대상 외이나 전체 그린 유지).
- 수동 GUI(사용자, F5 MSIX): ① 수정 진입 시 이름 읽기전용 표시 ② 클릭 시 입력창+포커스 ③ 이름 변경 시 경고 표시/원복 시 사라짐 ④ 신규는 바로 입력·경고 없음 ⑤ 저장 정상 ⑥ 경고 미표시 상태에서 이름~검증 InfoBar 간격이 과하지 않은지(빈 Auto 행 + RowSpacing=10 영향) 확인.

## 승인 필요 사항
- 구조/공개 API/의존성 변경 없음(ViewModel 바인딩 속성 추가뿐). 승인 후 implement-task 자율 진행.

## Task 체크리스트
- [ ] T1. GroupEditViewModel 상태 추가
- [ ] T2. GroupEditDialog.xaml 이름 전환 UI + 경고 InfoBar
- [ ] T3. GroupEditDialog.xaml.cs 클릭 핸들러
- [ ] T4. 문서 갱신
