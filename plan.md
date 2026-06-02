# plan.md — 그룹 드래그 시 드래그 이미지를 그룹 아이콘으로

> 이전 plan들은 완료(git 이력). 본 plan은 작업 그룹 목록 항목 드래그 비주얼 개선.

## 목표
작업 그룹 목록 항목을 작업 표시줄로 드래그할 때, 커서에 따라다니는 **드래그 이미지**를
현재의 "항목 카드 스냅샷"이 아니라 **그룹 아이콘**으로 표시한다.

## 배경 / 근본 원인 (확인된 사실)
- 현재 드래그는 `ListView CanDragItems="True"` + `DragItemsStarting="OnGroupDragStarting"`(WorkGroupsPage.xaml:58-59, .xaml.cs:66-116).
- `DragItemsStartingEventArgs`에는 드래그 비주얼을 바꾸는 멤버가 없다(`Cancel`/`Data`/`Items`뿐). → 기본값(항목 카드 스냅샷) 사용.
- 드래그 비주얼 지정 API는 `DragStartingEventArgs.DragUI.SetContentFromBitmapImage(...)`이며, 이는 `UIElement.DragStarting`에서만 제공된다.
- 따라서 **항목 카드(DataTemplate의 root `Border`)에 `CanDrag="True"` + `DragStarting` 핸들러로 전환**해야 비주얼 지정이 가능하다.
- 핸들러는 기존 핀 드롭 페이로드(임시 .lnk 복사 + `e.Data.SetDataProvider(StorageItems, deferral)`)를 **그대로 이식**해야 한다.

## 결정 사항 (기술결정 — 사용자 추가 입력 불필요)
- **DG1. 드래그 전환**: ListView의 `CanDragItems`/`DragItemsStarting` 제거 → 카드 `Border`에 `CanDrag="True"` + `DragStarting="OnGroupDragStarting"`.
- **DG2. 비주얼 소스**: 목록에 **이미 로드·표시된 `GroupListItem.Icon`(BitmapImage)** 재사용 → `e.DragUI.SetContentFromBitmapImage(bmp)`. 이미 디코드된 상태라 빈 비주얼 위험이 없다.
  - 확인된 사실: `GroupListItem.Icon`은 **항상 BitmapImage 또는 null**이다. 할당 경로 전수 — `TrySetIconFromFile`(new BitmapImage), `ApplyFallbackAsync`의 CustomImage(new BitmapImage(uri))·MemberApp(`AppIconLoader.LoadAsync`)가 모두 BitmapImage 생성. `AppIconLoader.LoadAsync`(AppIconLoader.cs:14-43)는 `new BitmapImage`만 반환하거나 null. 색상 폴백 그룹만 Icon=null(IconFallback 브러시 사용).
  - 따라서 `is BitmapImage` 가드는 **아이콘이 있는 모든 그룹을 커버**하고, Icon=null(색상 폴백)인 그룹만 SetContent 생략→기본 비주얼(R4, 수용).
- **DG3. abort 처리**: `DragStartingEventArgs`에는 `Cancel`이 없다. 따라서 .lnk가 없으면 **데이터를 설정하지 않고 StatusMessage만** 띄운다(빈 페이로드 → 드롭해도 무해한 no-op). 기존 `e.Cancel=true` 대체.
- **DG4. 동작 플래그**: `e.AllowedOperations = Copy | Link` + `e.Data.RequestedOperation = Copy | Link`.
- **DG5. 데이터 공급자**: 임시 디렉터리 .lnk 복사 + `e.Data.SetText` + `e.Data.SetDataProvider(StorageItems, deferral)` 로직을 동일 이식(기존 try/catch 포함).

## 범위(Scope)
- In: `WorkGroupsPage.xaml`(드래그 트리거를 ListView→카드 Border로) + `WorkGroupsPage.xaml.cs`(핸들러 시그니처/비주얼 추가).
- Out: 드롭 페이로드 형식/핀 로직 변경, ShortcutService/IconService/경로 변경, 다른 페이지, 도메인.

## 위험 (Risks)
- **R1. 검증된 핀 드래그 회귀**: 드래그 시작 메커니즘을 바꾸므로 "작업 표시줄로 끌어 핀"이 깨질 수 있다. 데이터 공급자(.lnk StorageItems + deferral) 로직을 **동일하게 이식**해 위험 최소화. 헤드리스 GUI 실측 불가 → 사용자 수동 검증 필수(R3).
- **R2. ListView 내 per-item CanDrag 동작**: ListView(SelectionMode=None) 안의 자식 Border에 CanDrag를 줬을 때 드래그가 정상 시작되는지는 표준 지원 사항이나 GUI 실측 필요.
- **R3. 헤드리스 검증 한계**: 드래그/드롭/핀/비주얼은 빌드·정적 검토만 가능, 실제 동작은 사용자 확인.
- **R4. BitmapImage 재사용**: `Icon`이 null(아이콘 실패 그룹)이면 커스텀 비주얼 없이 기본 비주얼로 표시(수용).

## 영향 범위 전수 조사 (Impact Analysis)
### 4-A. 심볼/사용처 (grep+Read)
- `OnGroupDragStarting` 사용처: `WorkGroupsPage.xaml`(드래그 이벤트 바인딩) + `WorkGroupsPage.xaml.cs`(정의) **2곳뿐**. 시그니처를 `(object, DragItemsStartingEventArgs)` → `(UIElement, DragStartingEventArgs)`로 변경하며 XAML 바인딩도 함께 이동(ListView→Border).
- `DragItemsStarting`/`CanDragItems`: WorkGroupsPage.xaml에만 존재.
- `GroupListItem.Icon`(ImageSource, BitmapImage로 설정됨): GroupListItem.cs에서 BitmapImage 할당(TrySetIconFromFile). 드래그 비주얼에서 읽기만 함(변경 없음).
### 4-B. 계약/직렬화
- 변경 없음. 드롭 페이로드(StorageItems=.lnk) 동일. 공개 API/직렬화 불변.
### 4-C. 영향 테스트
- 해당 없음(UI 드래그는 단위 테스트 대상 아님). 빌드/정적 검토 + 수동 GUI로 검증.

## 작업 분해 (Tasks)
- [x] **T1 — 드래그 비주얼을 그룹 아이콘으로 (트리거 전환 + 핸들러)** *(~1.5h)*
  - **Type**: C
  - **Acceptance**:
    - `WorkGroupsPage.xaml`: ListView의 `CanDragItems="True"`·`DragItemsStarting="OnGroupDragStarting"` 제거. 카드 root `Border`(DataTemplate, x:DataType=GroupListItem)에 `CanDrag="True"` + `DragStarting="OnGroupDragStarting"` 추가.
    - `WorkGroupsPage.xaml.cs`: `OnGroupDragStarting` 시그니처를 `(UIElement sender, DragStartingEventArgs e)`로 변경.
      - GroupListItem은 `(sender as FrameworkElement)?.DataContext as GroupListItem`로 취득(null이면 return).
      - .lnk 경로 확인(`IShortcutService.GetShortcutPath`). 없으면 StatusMessage 설정 후 return(데이터 미설정 — DG3).
      - `e.AllowedOperations = Copy | Link`; `e.Data.RequestedOperation = Copy | Link`; 임시 .lnk 복사 + `e.Data.SetText` + `e.Data.SetDataProvider(StorageItems, deferral)` 동일 이식.
      - 비주얼: `if (item.Icon is BitmapImage bmp) e.DragUI.SetContentFromBitmapImage(bmp);` (DG2). null/비-BitmapImage면 생략.
      - 기존 try/catch 보존(예외 시 StatusMessage; e.Cancel 없음).
    - 필요한 using 추가(`Microsoft.UI.Xaml`, `Microsoft.UI.Xaml.Media.Imaging`).
    - 빌드 0/0, 테스트 회귀 없음.
  - **Files**: `src/WorkGroup.App/Views/WorkGroupsPage.xaml`, `src/WorkGroup.App/Views/WorkGroupsPage.xaml.cs`
  - **Edge Cases**: .lnk 없음(미저장)→메시지+빈 페이로드. Icon null(아이콘 실패)→기본 비주얼. deferral 내부 예외→기존 처리. SelectionMode=None이라 단일 항목. 임시 디렉터리 정리 로직 유지.
  - **Halt Forecast**: `DragStartingEventArgs`에 `DragUI`/`AllowedOperations`/`GetDeferral` 존재·`Cancel` 부재는 빌드로 검증. `Border.CanDrag`/`DragStarting` 지원도 빌드로 확인. 빌드 에러 시 시그니처/네임스페이스 조정.
  - **Depends on**: -

- [x] **T2 — 문서 갱신** *(~0.2h)*
  - **Type**: A
  - **Acceptance**: notes.md에 변경 내역 추가(드래그 비주얼=그룹 아이콘). README는 기능 설명 변화 없음(드래그-핀 동작 동일)이라 미갱신. 최종 빌드 0/0.
  - **Files**: `notes.md`
  - **Depends on**: T1

## 검증 방법
- `dotnet build WorkGroup.slnx` 0/0, `dotnet test WorkGroup.slnx` 회귀 없음.
- 수동(GUI, 자율 불가): ① 목록 항목 드래그 시 커서 이미지가 그룹 아이콘 ② 작업 표시줄로 드롭 시 기존처럼 핀 생성 ③ 미저장 그룹 드래그 시 안내 메시지.

## 승인 필요 항목
- 없음(공개 API/의존성/직렬화 불변, UI 드래그 트리거·비주얼만 변경). 단 R1(검증된 핀 드래그) 관련 GUI 수동 검증은 사용자 몫.

## Open Questions (없음 — 기술결정으로 모두 해결)

## Progress Log
<!-- implement-task가 갱신 -->
- T1-T2 완료 (커밋 831c679, 후속): T1=드래그 트리거를 ListView→카드 Border(CanDrag+DragStarting)로 전환, DragUI.SetContentFromBitmapImage(item.Icon)로 드래그 비주얼=그룹 아이콘, 데이터 공급자 동일 이식, e.Cancel 제거(DragStarting 미지원). T2=notes 갱신. 빌드 0/0, 테스트 80/80, spec OK. **plan 전체 완료.** 드래그/드롭/핀 실제 동작은 GUI 수동 검증 필요(R1/R3).

## Next Steps
- 현재 상태: ✅ 드래그 비주얼=그룹 아이콘 완료. 빌드 0/0, 테스트 80/80.
- GUI 수동 검증(필수, 자율 불가): ① 항목 드래그 시 커서가 그룹 아이콘 ② 작업 표시줄 드롭→핀 생성(회귀 없음) ③ 미저장 그룹 드래그→안내 메시지.
- 권장 다음 액션: GUI 검증 → PR 생성. Suggested skills: 공식 /code-review, /security-review.
