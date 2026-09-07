# plan — 드래그 순서 변경 후속 3건(통지 · 흔들림 · 인덱스 매핑 검증)

## 요구 이해

**원문 요청**: "다음 회차 인계 (3건) 새 세션에서 진행 할 수 있도록 계획"

직전 회차(목록 드래그 순서 변경, 커밋 12개·push 완료, HEAD `94ac782`)가 `[다음 회차]`로 남긴 3건을 한 회차로 처리한다.

**확정된 요구** (인터뷰 결과):

| 항목 | 내용 |
|---|---|
| 결과 | ① 트레이 메뉴 페이지에 `InfoBar` + `StatusMessage`를 두어 순서 저장 실패를 알린다 ② 검색 중 핸들이 사라져도 카드 내용이 밀리지 않게 자리를 유지한다 ③ 슬롯→실제 인덱스 매핑을 순수 계산으로 내려 앞뒤 경계를 케이스로 잰다 |
| 사용자 | 두 목록 페이지에서 순서를 바꾸고 검색으로 항목을 찾는 본인 |
| 지금 하는 이유 | 직전 회차가 설계 선택(①)·실화면 확인 필요(②)·테스트 불가 위치(③)를 이유로 미뤘고, 그 판단이 이번에 내려졌다 |
| 성공의 모습 | 트레이 페이지에서 저장이 실패하면 화면에 이유가 뜨고, 검색어를 칠 때 목록이 좌우로 움직이지 않으며, 가상화된 목록의 앞뒤 드롭 경계가 단위 테스트로 덮인다 |
| 제약조건 | DDD 의존 방향 유지(App→Infrastructure→Application→Domain) · 새 리소스 키를 만들지 않는다(실패 메시지는 리포지토리가 이미 현지화해 담아 준다) · 두 목록 페이지의 조작법과 코드 구조를 같게 유지한다 |

## Goal

직전 회차가 남긴 3건을 닫는다. ①은 그룹 페이지에 이미 있는 상태 표시 패턴을 트레이 페이지에 이식하고, ②는 핸들을 `Visibility` 대신 `Opacity`로 숨겨 열 자리를 유지하며, ③은 `ReorderDrop`에 남아 있던 인덱스 매핑을 `ListInsertionPoint`로 내려 경계를 케이스로 잰다.

| # | 기준 | 측정 방법 |
|---|---|---|
| G1 | 솔루션이 경고 0 · 오류 0으로 빌드된다 | `dotnet build WorkGroup.slnx` |
| G2 | 전체 테스트가 실패 0으로 통과하고 케이스가 늘어난다 (작성 시점 Domain 23 + Application 142 = 165 → 매핑 케이스 추가로 170 이상) | `dotnet test WorkGroup.slnx` |
| G3 | 슬롯→실제 인덱스 매핑이 순수 계산 층에 있고 케이스로 덮인다 | `grep -c "ResolveActualIndex" src/WorkGroup.Infrastructure/Ui/ListInsertionPoint.cs tests/WorkGroup.Application.Tests/ListInsertionPointTests.cs` → 작성 시점 0건 → 수정 후 각 1건 이상 |
| G4 | `ReorderDrop`에 인덱스 산술이 남지 않는다 — 슬롯 보정식이 사라진다 | `grep -c "Indexes\[\^1\]" src/WorkGroup.App/Views/ReorderDrop.cs` → 작성 시점 1건 → 수정 후 0건 |
| G5 | 두 페이지의 핸들이 자리를 유지하며 숨는다 | `grep -c "ReorderHandleVisibility" src/WorkGroup.App/Views/WorkGroupsPage.xaml src/WorkGroup.App/Views/TrayMenuPage.xaml` (작성 시점 각 1건 → 각 0건) · `grep -c "ReorderHandleOpacity" <같은 두 파일>` (각 0건 → 각 1건) |
| G7 | 숨긴 핸들이 접근성 트리에서 빠진다 | `grep -c "AccessibilityView" src/WorkGroup.App/ViewModels/GroupListItem.cs src/WorkGroup.App/ViewModels/FolderShortcutItem.cs src/WorkGroup.App/Views/WorkGroupsPage.xaml src/WorkGroup.App/Views/TrayMenuPage.xaml` → 작성 시점 4파일 모두 0건 → 수정 후 각 1건 이상 |
| G8 | 폴더 저장 실패 경로가 케이스로 덮인다 — InfoBar가 표시할 바로 그 시나리오다 | `grep -c "SaveFailed\|쓰기_실패" tests/WorkGroup.Application.Tests/JsonFolderShortcutRepositoryTests.cs` → 작성 시점 0건 → 수정 후 1건 이상 |
| G6 | 트레이 페이지에 상태 표시 경로가 생긴다 | `grep -c "InfoBar" src/WorkGroup.App/Views/TrayMenuPage.xaml` (작성 시점 0 → 1 이상) · `grep -c "StatusMessage" src/WorkGroup.App/ViewModels/FolderShortcutsViewModel.cs` (작성 시점 0 → 2 이상) |

## Out of Scope

- 그룹 페이지의 저장 실패 처리 — 이미 `StatusMessage`로 표시한다(`WorkGroupsViewModel.cs:110`)
- 토스트/`AppNotification` 도입 — 앱에 구현이 0건이라 신규 인프라 + 매니페스트 변경이 필요하고, 인터뷰에서 제외됨
- 저장 실패 시 목록을 원래 순서로 되돌리는 롤백 — 다음 `LoadAsync`가 저장된 순서로 복원하는 현재 정책을 유지한다
- 실화면 드래그 조작감·삽입 표시선 위치 검증 — 헤드리스로 불가하며, 이번 회차도 `HUMAN-VERIFY`로 보고한다

## Decisions

| # | 결정 | 근거 |
|---|---|---|
| D1 | 저장 실패 통지는 `InfoBar` + `StatusMessage`로 한다 | 인터뷰에서 사용자 확인. 앱에 이미 3개 화면(`GroupEditDialog`·`SettingsPage`·`WorkGroupsPage`)이 쓰는 패턴이고, 토스트는 구현이 0건이라 신규 인프라가 된다 |
| D2 | 검색 중 핸들은 `Opacity`로 숨겨 열 자리를 유지한다 | 인터뷰에서 사용자 확인. `Grid.ColumnSpacing`은 폭 0인 열에도 간격을 적용하므로 열을 접어도 12px는 남는다 — 자리 유지만이 완전한 해법이다 |
| D3 | 새 리소스 키를 만들지 않는다 | 실패 메시지는 `JsonFolderShortcutRepository`가 `Infra_Folder_SaveFailed`로 현지화해 `Result.Error`에 담는다. Source: 그 파일의 `WriteUnlockedAsync`, 4개 resw에 키 존재(각 1건) |
| D4 | 슬롯→실제 인덱스 매핑을 `ListInsertionPoint`로 내린다 | 직전 회차 D4가 정한 방향(순수 계산은 하위 레이어, `ListView` 접근만 App)에서 이 한 조각만 남아 있었다. Source: `ReorderDrop.cs`의 `ResolveDropTarget` 삼항식 |
| D5 | 숨긴 핸들은 접근성 트리에서도 뺀다 | `Opacity=0`은 시각적으로만 숨기므로 스크린 리더가 조작할 수 없는 핸들을 읽는다. 항목 VM이 `AccessibilityView`를 함께 내보낸다 |

## Investigation Log

| 주장 | 실행한 명령 | 출력 요지 |
|---|---|---|
| 트레이 페이지·그 VM에는 상태 표시 경로가 전혀 없다 | `grep -cn "InfoBar\|StatusMessage" src/WorkGroup.App/Views/TrayMenuPage.xaml src/WorkGroup.App/ViewModels/FolderShortcutsViewModel.cs` | 두 파일 모두 `0` |
| `InfoBar`는 앱의 확립된 패턴이다 | `grep -rln "InfoBar" src/WorkGroup.App --include=*.xaml` | `GroupEditDialog.xaml` · `SettingsPage.xaml` · `WorkGroupsPage.xaml` 3건 |
| 토스트/알림 구현이 없다 | `grep -rn "ToastNotification\|AppNotification\|Notification" --include=*.cs src` | 0건 |
| ViewModel에 `ILogger`가 주입된 곳이 없다 | `grep -rn "ILogger" --include=*.cs src/WorkGroup.App/ViewModels` | 0건 |
| 그룹 쪽 상태 패턴은 3속성 + InfoBar 1개다 | `grep -n "StatusMessage\|HasStatus\|StatusVisibility" src/WorkGroup.App/ViewModels/WorkGroupsViewModel.cs` · `grep -n "InfoBar" -A 4 src/WorkGroup.App/Views/WorkGroupsPage.xaml` | VM 40~47행에 `StatusMessage`(ObservableProperty) + `HasStatus` + `StatusVisibility`, XAML 60~63행에 `InfoBar`(Visibility·IsOpen·Message 바인딩, `IsClosable=False`) |
| 실패 메시지 리소스 키가 4개 언어에 있다 | `grep -c "Infra_Folder_SaveFailed" src/WorkGroup.App/Strings/*/Resources.resw` | 4파일 모두 `1` |
| 핸들 숨김은 두 XAML에서 각 1건씩 `Visibility` 바인딩이다 | `grep -rn "ReorderHandleVisibility" --include=*.xaml --include=*.cs src` | VM 2곳(정의) + XAML 2곳(`WorkGroupsPage.xaml:99`, `TrayMenuPage.xaml:86`) |
| 인덱스 매핑 삼항식이 `ReorderDrop`에 남아 있다 | `sed -n 22,36p src/WorkGroup.App/Views/ReorderDrop.cs` | `slot < realized.Indexes.Count`이면 `realized.Indexes[slot]`, 아니면 마지막 실현 인덱스 + 1, 실현이 없으면 `list.Items.Count` |
| `ListInsertionPoint`의 공개 정적 메서드는 3개다 | `grep -n "public static" src/WorkGroup.Infrastructure/Ui/ListInsertionPoint.cs` | `Resolve` · `IndicatorOffset` · `ResolveMoveTarget`(+ 클래스 선언 1행) |
| `TrayMenuPage`는 헤더 StackPanel(Row 0) + 목록 Grid(Row 1) 구조다 | `sed -n 12,24p src/WorkGroup.App/Views/TrayMenuPage.xaml` | 바깥 Grid에 `RowDefinition Auto/*` 2개, Row 0이 제목·검색·개수 StackPanel |
| 현재 테스트는 Domain 23 + Application 142가 통과한다 | `dotnet test WorkGroup.slnx` | `통과! - 실패: 0, 통과: 23` / `통과! - 실패: 0, 통과: 142` |

## 작업 단계

### T1. 슬롯→실제 인덱스 매핑을 순수 계산으로 내리고 케이스로 잰다

- [x] **T1-1** `ListInsertionPoint`에 `ResolveActualIndex(IReadOnlyList<int> realizedIndexes, int slot, int totalCount)` 추가 — 슬롯이 실현 목록 안이면 그 실제 인덱스, 마지막 실현 항목보다 아래면 그 항목의 바로 뒷자리, 실현된 항목이 하나도 없으면 `totalCount`
- [x] **T1-2** `ReorderDrop.ResolveDropTarget`이 그 메서드를 호출하도록 바꾸고 삼항식을 제거한다(어댑터에는 `ListView` 접근만 남는다)
- [x] **T1-3** `ListInsertionPointTests`에 케이스 5건 추가 — ① 실현 목록이 전체와 같을 때 슬롯이 그대로 인덱스가 된다 ② 앞쪽이 재활용돼 실현 목록이 `[3,4,5]`일 때 슬롯 0이 3이 된다 ③ 마지막 실현 항목 아래(슬롯 == 실현 수)면 그 항목 + 1이다(전체 끝이 아니다) ④ 실현 항목이 없으면 `totalCount` ⑤ 실현 목록이 전체 끝까지 갈 때 마지막 아래는 `totalCount`와 같다
- **Files**: `src/WorkGroup.Infrastructure/Ui/ListInsertionPoint.cs` · `src/WorkGroup.App/Views/ReorderDrop.cs` · `tests/WorkGroup.Application.Tests/ListInsertionPointTests.cs`
- **Acceptance**: 신규 5건 통과. 기대값은 구현식을 옮기지 않고 **손으로 계산한 인덱스 상수**로 적는다(예: 실현 `[3,4,5]`·전체 8일 때 슬롯 3 → 6). 케이스 ③과 ⑤가 갈리는지 확인한다 — 둘이 같은 값이면 뒤쪽 경계를 재지 못한 것이다
- **검증**: `dotnet test WorkGroup.slnx` → 실패 0, Application 통과 147 이상

### T2. 트레이 메뉴 페이지에 저장 실패 통지

- [x] **T2-1** `FolderShortcutsViewModel`에 `StatusMessage`(ObservableProperty) + `HasStatus` + `StatusVisibility`를 그룹 쪽과 같은 형태로 추가하고 생성자에서 빈 문자열로 초기화
- [x] **T2-2** `MoveAsync`가 `ReorderAsync`의 `Result`를 받아 실패 시 `StatusMessage`에 담고 성공 시 비운다(`WorkGroupsViewModel.cs:110`과 같은 형태). 실패 메시지는 리포지토리가 현지화해 주므로 새 키를 만들지 않는다
- [x] **T2-3** `TrayMenuPage.xaml`의 목록 Grid에 행을 나눠 `InfoBar`를 목록 위에 놓는다 — `WorkGroupsPage.xaml:60-63`과 같은 속성(Visibility·IsOpen·`IsClosable=False`·`Severity=Informational`·Message). 메시지가 없으면 접혀 간격을 차지하지 않아야 한다
- [x] **T2-4** `DropIndicator`·`FoldersList`·빈 상태 `StackPanel` 셋 모두에 목록 행의 `Grid.Row`를 지정한다 — 지금 이 Grid에는 `RowDefinitions`가 없어 셋이 암묵적으로 Row 0에 겹쳐 있고, 행을 나누면서 하나라도 빠지면 삽입 표시선 좌표가 어긋나거나 "폴더 0개" 안내가 InfoBar 행에 갇힌다(참조 패턴 `WorkGroupsPage.xaml:156,162`는 셋 다 `Grid.Row="1"`)
- [x] **T2-5** `JsonFolderShortcutRepositoryTests`에 쓰기 실패 케이스 1건 추가 — 임시 파일 경로와 같은 이름의 **디렉터리**를 만들어 `WriteUnlockedAsync`의 `File.Create(tempPath)`를 실패시키고, `ReorderAsync`가 `Result.Fail`을 돌려주는지 잰다. InfoBar가 표시해야 할 바로 그 경로이며 현재 이를 재는 케이스가 0건이다
- **Files**: `src/WorkGroup.App/ViewModels/FolderShortcutsViewModel.cs` · `src/WorkGroup.App/Views/TrayMenuPage.xaml` · `tests/WorkGroup.Application.Tests/JsonFolderShortcutRepositoryTests.cs`
- **Acceptance**: `grep -c "InfoBar" src/WorkGroup.App/Views/TrayMenuPage.xaml` → 0건 → 1건 이상. `grep -c "StatusMessage" src/WorkGroup.App/ViewModels/FolderShortcutsViewModel.cs` → 0건 → 2건 이상. `DropIndicator`·`FoldersList`·빈 상태 `StackPanel`의 `Grid.Row` 값이 모두 같다. T2-5 신규 1건 통과. 빌드 경고 0. **실제 InfoBar 표시 여부는 `HUMAN-VERIFY`** — VM 속성이 InfoBar에 반영되는 것은 GUI 경로이고, 그 앞단인 "쓰기 실패 → `Result.Fail`"만 T2-5가 잰다
- **검증**: `dotnet build WorkGroup.slnx` → 경고 0 / 오류 0 · `dotnet test WorkGroup.slnx` → 실패 0

### T3. 검색 중 핸들이 자리를 유지하며 숨도록 전환

- [x] **T3-1** `GroupListItem`·`FolderShortcutItem`의 `ReorderHandleVisibility`를 `ReorderHandleOpacity`(double, 1.0/0.0)로 바꾸고, 숨겼을 때 접근성 트리에서 빠지도록 `ReorderHandleAccessibilityView`를 함께 노출한다(`CanReorder`의 `NotifyPropertyChangedFor` 대상도 갱신)
- [x] **T3-2** 두 XAML의 핸들 `FontIcon`에서 `Visibility` 바인딩을 `Opacity`·`AutomationProperties.AccessibilityView`로 바꾸고, `IsHitTestVisible`을 `CanReorder`에 바인딩해 숨은 핸들이 마우스를 먹지 않게 한다
- **Files**: `src/WorkGroup.App/ViewModels/GroupListItem.cs` · `src/WorkGroup.App/ViewModels/FolderShortcutItem.cs` · `src/WorkGroup.App/Views/WorkGroupsPage.xaml` · `src/WorkGroup.App/Views/TrayMenuPage.xaml`
- **Acceptance**: 두 XAML에서 `ReorderHandleVisibility` 각 1건 → 0건, `ReorderHandleOpacity` 각 0건 → 1건. `grep -c "AccessibilityView"`가 항목 VM 2개와 XAML 2개에서 각 0건 → 1건 이상. 빌드 경고 0.
  **[면제 ④] `WorkGroup.App` 서브트리에 테스트 러너가 없다** — `tests/`에 Domain·Application 두 프로젝트뿐이고 `grep -rn "MoveAsync\|IsFiltered\|WorkGroupsViewModel\|FolderShortcutsViewModel" tests/` → 0건이다. **변이 실증은 이 task에서 불가능하다** — 바꾸는 것이 시각 표현(`Opacity`)과 접근성 노출이라 실행 경로에 관측 지점이 없다. 이 사실을 근거로 사용자가 수동 확인을 선택했다(인터뷰). 따라서 **흔들림 해소·접근성 트리 제외는 `HUMAN-VERIFY`이고, 이 task에는 케이스가 없다**
  이 task가 재정렬 차단 동작 자체를 바꾸지 않는다는 것은 별도로 확인한다 — `MoveAsync`의 `IsFiltered` 방어와 `CanDrag` 바인딩은 손대지 않는다(T3 Files에 그 파일들이 없다)
- **검증**: `dotnet build WorkGroup.slnx` → 경고 0 / 오류 0
- **의존**: T2와 같은 파일(`TrayMenuPage.xaml`)을 건드리므로 T2 뒤에 수행한다

### T4. 문서 갱신

- [x] **T4-1** `README.md`의 트레이 메뉴 절에 저장 실패 안내 표시를 한 구 추가
- [x] **T4-2** `notes.md` 「최근 변경」에 이번 회차 항목 추가(레포 관례 — 최근 기능·수정 커밋이 모두 이 파일을 함께 갱신했다)
- **Files**: `README.md` · `notes.md`
- **Acceptance**: `grep -c "2026-09" notes.md` → 작성 시점 1건 → 2건. [면제 ①] 실행 경로를 바꾸지 않는 문서 수정
- **검증**: `grep -n "2026-09" notes.md`

## 검증 방법

| task | 명령 | 판정 |
|---|---|---|
| T1 | `dotnet test WorkGroup.slnx` · `grep -c "Indexes\[\^1\]" src/WorkGroup.App/Views/ReorderDrop.cs` | 실패 0, Application 147건 이상 · 1건 → 0건 |
| T2 | `dotnet build WorkGroup.slnx` · `dotnet test WorkGroup.slnx` · `grep -c "InfoBar" src/WorkGroup.App/Views/TrayMenuPage.xaml` | 경고 0 / 오류 0 · 실패 0, Application 148건 이상 · InfoBar 1건 이상 |
| T3 | `dotnet build WorkGroup.slnx` · `grep -c "ReorderHandleVisibility\|ReorderHandleOpacity\|AccessibilityView" <두 XAML + 두 항목 VM>` | 경고 0 / 오류 0 · Visibility 0건, Opacity·AccessibilityView 각 1건 이상 |
| T4 | `grep -n "2026-09" notes.md` | 2건 |
| 전체 | `dotnet build WorkGroup.slnx` + `dotnet test WorkGroup.slnx` | 경고 0 / 오류 0, 실패 0, 합계 171건 이상(작성 시점 165 + T1 5건 + T2 1건) |

## 승인 필요 항목

- **항목 VM의 공개 속성 이름 변경** — `ReorderHandleVisibility` → `ReorderHandleOpacity`. 두 XAML 바인딩이 유일한 소비자라(실측 2건) 영향은 그 안에 닫힌다. 되돌리기: 커밋 전이면 `git restore src/WorkGroup.App`, 커밋 후면 `git revert`
- **`ListInsertionPoint` 공개 API 추가** — `ResolveActualIndex` 1개. 기존 3개 메서드 시그니처는 그대로다
- **`TrayMenuPage` 레이아웃 변경** — 목록 Grid에 행을 추가한다. `DropIndicator`가 목록과 같은 셀에 남지 않으면 삽입 표시선 좌표가 어긋나므로 T2-4에서 함께 확인한다
- **push·태그·릴리즈는 이번 계획에 없다** — 필요해지면 그 시점에 별도 승인
- 총 11파일(신규 0) — T1 3 · T2 3 · T3 4 · T4 2에서 `TrayMenuPage.xaml` 중복 제거. 파일 5개 기준을 넘어 명시한다. 되돌리기: 회차 커밋들을 `git revert`

## Deferred / Follow-up

- [미등재:이번 회차가 처리] 직전 회차의 `[다음 회차]` 3건 — 이 plan의 T1~T3이 받는다
- [미등재:범위 밖] 저장 실패 시 목록을 원래 순서로 되돌리는 롤백 — 직전 회차와 같은 판단. 실패는 디스크 쓰기 실패라 드물고, 롤백을 넣으려면 두 VM의 실패 처리 정책을 함께 정해야 한다
- [다음 회차] `WorkGroup.App`의 ViewModel 테스트 프로젝트 신설 — `ApplyFilter`가 `CanReorder`를 갱신하는지, 검색 중 `MoveAsync`가 무동작인지를 재는 자리가 지금 없다. 이번 회차에서는 회차 범위를 3건으로 유지하기 위해 사용자가 수동 확인을 선택했다(구조 변경이라 별도 승인 대상이기도 하다)
- [미등재:원리상 불가] 실화면 확인이 필요한 항목이 이번에도 남는다(드래그 조작감·삽입 표시선 위치·핸들 히트테스트·흔들림 해소·InfoBar 표시). 헤드리스로는 확인할 수 없어 사용자 수동 검증으로 보고한다

## Progress Log

- T1~T3 완료 — 가상화 인덱스 매핑을 ListInsertionPoint.ResolveActualIndex로 내려 케이스 7건 추가(변이 2회로 red 확인), 트레이 페이지에 InfoBar 상태 경로 + 쓰기 실패 케이스 1건, 두 페이지 핸들을 Opacity·IsHitTestVisible·AccessibilityView 바인딩으로 전환. Application 테스트 142 → 150.

## Next Steps

다음 회차 인계:
1. `WorkGroup.App`의 ViewModel 테스트 프로젝트 신설 — `tests/`의 두 프로젝트 모두 `WorkGroup.App`을 ProjectReference하지 않아, ApplyFilter가 CanReorder를 갱신하는지·검색 중 MoveAsync가 무동작인지·실패가 StatusMessage에 담기는지를 재는 자리가 없다(T3이 면제 ④로 닫힌 이유). 구조 변경이라 착수 전 별도 승인이 필요하고, WinUI 타입 의존 때문에 windows TFM + App 참조 가능 여부를 먼저 실측해야 한다.
