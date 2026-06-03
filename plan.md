# Plan — 그룹 추가/수정 화면 개선

## 목표
그룹 추가/수정 다이얼로그(`GroupEditDialog`)를 개선한다.
1. 선택 앱 목록 위 왼쪽에 등록된 앱 개수 표시("앱 N개")
2. "앱 추가" 버튼을 "+" 아이콘만 표시
3. 앱 추가 버튼 왼쪽에 "팝업 이름 헤더 표시" on/off 토글 추가 — **그룹별 설정**으로 저장, 핀 팝업이 읽어 헤더 표시/숨김
4. 선택 앱 목록 항목 사이에 가로 구분선을 넣어 가독성(삭제 버튼 구분) 향상

## 범위
- **In scope**: `GroupEditDialog`(.xaml), `GroupEditViewModel`, `AppGroup` 도메인, `JsonGroupRepository` 직렬화, `GroupPopupWindow`(헤더 읽기), 관련 단위 테스트.
- **Out of scope**: 그룹 목록 화면(`WorkGroupsPage`), 팝업 아이콘 레이아웃, 테마/전역 설정 구조.

## 결정사항 (사용자 확정)
- 헤더 토글 저장 단위: **그룹별**(AppGroup 속성 + groups.json 필드).
- 토글 기본값: **ON(헤더 표시)** — 기존 동작 유지, 신규 그룹·레거시 데이터 모두 true.
- 토글 UI: **ToggleSwitch + 라벨**("팝업 이름 표시").
- 카운트 문구: **"앱 N개"**(0개 포함).
- 목록 가독성: **항목 사이 가로 구분선**.

## 위험
- `AppGroup.Create`/`Restore` 시그니처 변경 → 호출자/테스트 영향. **선택적 매개변수(기본 true)** 로 추가해 기존 호출자 무변경.
  - 호출자 전수(grep 확인 완료): 프로덕션 `GroupEditViewModel`(Create×1, Restore×1), `JsonGroupRepository`(Restore×1). 테스트 다수는 default 사용으로 무변경.
- 직렬화 호환: 기존 `groups.json`에는 새 필드 없음 → DTO를 `bool?`로 받아 `?? true` 마이그레이션.
- 팝업 헤더 숨김 시 그룹 미발견/에러 메시지는 계속 보여야 함(헤더와 분리 처리).

## Tasks

### T1 — AppGroup 도메인에 ShowPopupHeader 추가 (Type D)
- **Files**:
  - `src/WorkGroup.Domain/Groups/AppGroup.cs` (수정)
  - `tests/WorkGroup.Domain.Tests/AppGroupTests.cs` (테스트 추가)
- **변경**:
  - `public bool ShowPopupHeader { get; private set; }` 속성 추가.
  - private 생성자에 `bool showPopupHeader` 매개변수 추가.
  - `Create(string name, IconSource? icon = null, bool showPopupHeader = true)` — 선택적 매개변수.
  - `Restore(GroupId id, string name, IconSource icon, IEnumerable<AppEntry> apps, bool showPopupHeader = true)` — 선택적 매개변수.
  - `public void SetShowPopupHeader(bool value)` 추가(편집 시 변경용).
- **Decision points**:
  - 기본값 true(현행 동작 유지) → 신규/레거시 모두 헤더 표시.
  - 선택적 매개변수로 추가 → 기존 테스트 호출자 컴파일 무변경.
- **Edge cases**: 없음(단순 bool 상태).
- **Acceptance**:
  - `AppGroup.Create("x").Value.ShowPopupHeader == true`.
  - `Restore(...)` 기본 호출 시 true, 명시 false 전달 시 false.
  - `SetShowPopupHeader(false)` 후 속성 false.
  - `dotnet build` + Domain 테스트 통과.
- **Halt Forecast**:
  - 선택적 매개변수 추가 후 기존 테스트가 컴파일 실패하면 → 매개변수 기본값(true)이 빠졌는지 확인(모든 기존 호출자는 default 사용 전제).

### T2 — JsonGroupRepository 직렬화 + 레거시 마이그레이션 (Type D)
- **Files**:
  - `src/WorkGroup.Infrastructure/Persistence/JsonGroupRepository.cs` (수정)
  - `tests/WorkGroup.Application.Tests/JsonGroupRepositoryTests.cs` (테스트 추가)
- **변경**:
  - `GroupDto`(positional record, 현재 `(string Id, string Name, IconDto Icon, List<AppDto> Apps)`)에 마지막 매개변수로 `bool? ShowPopupHeader` 추가. positional record 기본 직렬화는 속성명을 그대로 쓰므로 **JSON 키는 `ShowPopupHeader`로 고정**(레거시 호환 핵심).
  - `MapToDomain`(L168-173): `AppGroup.Restore(..., dto.ShowPopupHeader ?? true)`.
  - `MapToDto`(L176-180): 인자 끝에 `group.ShowPopupHeader` 추가 — **양쪽(MapToDto/MapToDomain) 동시 수정 필수**.
  - `CurrentSchemaVersion`은 그대로 유지(nullable 하위호환이라 버전 불변 — 기존 파일도 정상 로드).
- **Decision points**:
  - 레거시(필드 없는) JSON → `?? true` 로 헤더 ON 유지.
  - 스키마 버전 유지(파괴적 변경 아님).
  - JSON 키 이름 `ShowPopupHeader` 고정(positional record 기본 동작).
- **Edge cases**:
  - 필드 없는 기존 groups.json 로드 → ShowPopupHeader true.
  - 손상 파일 → 기존 백업 로직 그대로(영향 없음).
- **테스트 구체화 (신규 작성)**:
  - 기존 `SampleGroup`(L29-35)은 `Create(name)` 기본값이라 항상 true → **별도 헬퍼/그룹으로 false 케이스 구성**.
    - 예: `var g = AppGroup.Create("업무").Value; g.SetShowPopupHeader(false);` → Save → 새 인스턴스 Load → `Assert.False(loaded.Single().ShowPopupHeader)`.
  - **레거시 JSON 직접 작성 테스트**: `ShowPopupHeader` 키를 뺀 groups.json 문자열을 `_dir`에 직접 써 로드 → `Assert.True(...ShowPopupHeader)`.
    - JSON 형태(키 없이): `{"SchemaVersion":1,"Groups":[{"Id":"<guid>","Name":"업무","Icon":{"Kind":"BuiltIn","Value":"..."},"Apps":[]}]}` — 실제 `IconDto.Value`는 `IconSource.DefaultBuiltIn.Value`를 사용해 작성.
- **Acceptance**:
  - false 라운드트립 테스트 통과(저장→로드 시 false 유지).
  - 레거시(키 없는) JSON 로드 시 ShowPopupHeader true 테스트 통과.
  - 기존 라운드트립/스키마 테스트 모두 통과.
- **Halt Forecast**:
  - 레거시 JSON 문자열의 `Icon.Value`가 실제 매핑과 안 맞아 로드 실패 시 → `IconSource.DefaultBuiltIn.Value`/`Kind` 실제 값으로 맞춰 재작성(코드 L170 `Enum.Parse<IconSourceKind>` 기준).

### T3 — GroupEditViewModel: 토글 속성 + 앱 개수 텍스트 (Type C)
- **Files**: `src/WorkGroup.App/ViewModels/GroupEditViewModel.cs` (수정)
- **변경**:
  - `[ObservableProperty] public partial bool ShowPopupHeader { get; set; }` 추가(생성자에서 true 초기화).
  - `public string AppCountText => $"앱 {SelectedApps.Count}개";` 계산 속성.
  - **생성자에서 단 1회** `SelectedApps.CollectionChanged += (_, _) => OnPropertyChanged(nameof(AppCountText));` 구독(추가/삭제/Clear 시 갱신). InitializeAsync에는 구독을 두지 않는다(재호출 시 중복 구독 방지).
  - `InitializeAsync`: `ShowPopupHeader = group?.ShowPopupHeader ?? true;` 설정 + `OnPropertyChanged(nameof(AppCountText));`(편집 멤버 복원 후).
  - `ValidateAndSaveAsync`:
    - 신규: `AppGroup.Create(name, SelectedIcon, ShowPopupHeader)`.
    - 편집: `AppGroup.Restore(_editingId, name, SelectedIcon, apps, ShowPopupHeader)`.
- **Decision points**:
  - AppCountText는 ObservableCollection 변경 구독으로 갱신(개별 Add/Remove/Clear 모두 CollectionChanged 발생).
  - **CollectionChanged 구독은 생성자에서 단 1회**(InitializeAsync 재호출 시 중복 구독 금지).
  - ShowPopupHeader 기본 true(신규 그룹).
- **Edge cases**:
  - SelectedApps 0개 → "앱 0개"(저장은 기존 검증이 0개 차단).
  - InitializeAsync 재호출(다이얼로그 재사용) 시 ShowPopupHeader는 재설정, 구독은 유지(중복 안 됨).
- **Acceptance**:
  - 앱 추가/삭제 시 AppCountText가 즉시 갱신.
  - 편집 모드 진입 시 토글이 그룹의 ShowPopupHeader 값으로 초기화.
  - 저장 시 토글 값이 AppGroup에 반영.
  - `dotnet build` 통과.
- **Halt Forecast**:
  - `[ObservableProperty]` partial 속성 생성 충돌 시 → 기존 partial 속성 패턴(L61-66) 그대로 따름.

### T4 — GroupEditDialog.xaml: 카운트/토글/"+"버튼 + 목록 구분선 (Type C)
- **Files**: `src/WorkGroup.App/Views/GroupEditDialog.xaml` (수정)
- **변경**:
  - Row 3(현재 "앱 추가" 버튼 행)을 한 줄 좌/우 영역으로 재구성:
    - 왼쪽: `TextBlock Text="{x:Bind ViewModel.AppCountText, Mode=OneWay}"`(세로 중앙).
    - 오른쪽: `StackPanel Orientation="Horizontal"`에 ① 라벨+ToggleSwitch ② "+" 버튼.
    - 토글: 라벨 `TextBlock Text="팝업 이름 표시"` + `ToggleSwitch IsOn="{x:Bind ViewModel.ShowPopupHeader, Mode=TwoWay}"`(OnContent/OffContent 비우고 컴팩트).
    - "+" 버튼: 기존 Flyout(앱 검색/추가) 유지, 콘텐츠를 `SymbolIcon Symbol="Add"` 만으로 변경, `ToolTipService.ToolTip="앱 추가"` 추가.
  - Row 4 선택 앱 목록(`ListView`) 구분선 — **방식 확정(Border 래핑)**:
    - 현재 DataTemplate 루트 `Grid`(xaml L144)를 `Border`로 감싼다: `<Border BorderThickness="0,0,0,1" BorderBrush="{ThemeResource DividerStrokeColorDefaultBrush}" Padding="0,0,0,8" Margin="0,0,0,8"> <Grid …/> </Border>`.
    - 즉 항목 콘텐츠 아래 8px 패딩 + 1px 하단선 + 항목 간 8px 마진으로 선과 간격을 분리(선이 콘텐츠에 붙지 않게).
    - ItemContainerStyle은 손대지 않는다(기본 ListViewItem 패딩 유지).
- **Decision points**:
  - 토글 라벨은 가로 배치(ToggleSwitch Header는 세로로 늘어나 한 줄 정렬 깨짐).
  - "+" 버튼 ToolTip로 의미 보존(아이콘만으로 접근성 보완).
  - 구분선은 DividerStrokeColorDefaultBrush(테마 대응), **Border 래핑 단일 방식으로 확정**(대안 분기 없음).
- **Edge cases**:
  - 앱 0개 → 목록 비어 구분선 없음, 카운트 "앱 0개".
  - 마지막 항목 하단선은 허용(목록 테두리 안쪽이라 부담 적음).
- **Acceptance**:
  - 카운트가 목록 위 왼쪽에 표시.
  - 토글+「+」버튼이 오른쪽 한 줄에 정렬, 버튼은 + 아이콘만.
  - 목록 항목 사이 가로 구분선으로 행 구분 명확.
  - `dotnet build` 통과(x64).
- **Halt Forecast**:
  - 구분선이 항목 간격과 어긋나거나 이중선처럼 보이면 → Padding/Margin 값(8/8)을 조정하되 "Border 래핑" 방식은 유지(방식 변경 금지).
  - ToggleSwitch가 너무 넓어 한 줄이 깨지면 → `MinWidth="0"` + OnContent/OffContent 빈 문자열로 컴팩트화.

### T5 — GroupPopupWindow: ShowPopupHeader 읽어 헤더 표시/숨김 (Type C)
- **Files**:
  - `src/WorkGroup.App/Views/GroupPopupWindow.xaml.cs` (수정)
  - `src/WorkGroup.App/Views/GroupPopupWindow.xaml` (수정 — RowSpacing 잔여 여백 처리)
- **변경**:
  - **xaml**: 헤더 Collapsed 시 상단 8px 잔여 여백을 막기 위해 `<Grid Padding="12,0,12,0" RowSpacing="8">`의 `RowSpacing="8"`을 제거하고, `TitleText`에 `Margin="0,8,0,8"`을 부여. Collapsed 요소는 레이아웃에서 제외되어 마진까지 사라지므로 헤더 숨김 시 잔여 여백이 없다. (헤더 표시 시 제목 위/아래 8px 간격 유지.)
  - **xaml.cs `LoadAsync`** 그룹 로드 성공 시:
    - `TitleText.Visibility = group.ShowPopupHeader ? Visibility.Visible : Visibility.Collapsed;`
    - 헤더 표시일 때만 기존 이름/("멤버 없음") 텍스트 설정.
  - 그룹 미발견/에러 분기("그룹을 찾을 수 없습니다" 등)에서는 `TitleText.Visibility = Visibility.Visible`로 강제(메시지 가시성 보장).
  - 헤더 숨김 시 콘텐츠 높이는 기존 `AdjustToContent`(SizeChanged 측정)로 자동 축소.
- **Decision points**:
  - RowSpacing 제거 + TitleText Margin 방식으로 잔여 여백 근본 차단(Collapsed 시 마진째 제외).
  - 에러 메시지는 헤더 설정과 무관하게 항상 표시.
- **Edge cases**:
  - ShowPopupHeader=false인데 멤버 0개 → 헤더 숨김(이름/멤버없음 모두 미표시), 아이콘 그리드만(빈) 표시.
  - 그룹 로드 실패 → 헤더 강제 표시 후 에러 텍스트.
- **Acceptance**:
  - ShowPopupHeader=false 그룹 핀 클릭 시 팝업에 이름 헤더 미표시 + 상단 여백 과다 없음(F5 수동 확인).
  - ShowPopupHeader=true(기존 그룹) 팝업은 이름 헤더 표시(현행 유지).
  - 미발견/에러 시 메시지 표시.
- **Halt Forecast**:
  - RowSpacing 제거 후 헤더 표시 시 제목-그리드 간격이 사라지면 → TitleText Margin 하단 값으로 보정(8 유지).
  - 사용자가 최근 수정한 Padding("12,0,12,0")과 충돌 시 → Padding은 보존하고 간격은 TitleText Margin으로만 조정.

## 의존 관계
- T1 → T2 → T3 → T4 (도메인 → 직렬화 → VM → View)
- T1 → T5 (도메인 속성 필요)

## 검증 방법
- 각 task: `dotnet build src/WorkGroup.App/WorkGroup.App.csproj -p:Platform=x64 -p:RuntimeIdentifier=win-x64` (경고/에러 0).
- 도메인/직렬화: `dotnet test`(Domain.Tests, Application.Tests) 통과.
- 시각/동작(카운트 갱신, 토글 저장→팝업 반영, 구분선, "+" 버튼)은 F5 MSIX GUI 수동 검증(자동 검증 불가 항목 명시).

## 문서 갱신 (구현 완료 시)
- `README.md`: 그룹 편집 화면 설명에 앱 개수·팝업 이름 표시 토글 반영, 팝업 헤더가 그룹 설정에 따름 명시.
- `notes.md`: 변경 내역 추가.

## 진행 체크리스트
- [x] T1 AppGroup ShowPopupHeader
- [x] T2 직렬화 + 레거시 마이그레이션
- [x] T3 GroupEditViewModel 토글/카운트
- [x] T4 GroupEditDialog.xaml 레이아웃/구분선 (시각은 F5 수동 검증)
- [x] T5 GroupPopupWindow 헤더 표시/숨김 (헤더 숨김 여백은 F5 수동 검증)

## Progress Log
- T1~T5 완료(미커밋): AppGroup.ShowPopupHeader(선택적 매개변수 기본 true) → JsonGroupRepository 직렬화(bool?, ?? true 마이그레이션) → GroupEditViewModel(ShowPopupHeader 토글 + AppCountText) → GroupEditDialog.xaml(카운트/토글/「+」버튼 + 목록 구분선) → GroupPopupWindow(헤더 표시/숨김 + RowSpacing→Margin). 빌드 0/0, 테스트 86/86. plan-completion-reviewer: 코드 BLOCKER 0(문서 갱신 후 해소).

## Next Steps
- 권장 다음 액션: F5 MSIX GUI로 토글 저장→팝업 헤더 반영·카운트 갱신·구분선·헤더 숨김 여백 확인 후, 사용자 승인 시 커밋.
- Suggested skills: 공식 /code-review (커밋 전 diff 리뷰), 공식 /security-review(해당 시)
