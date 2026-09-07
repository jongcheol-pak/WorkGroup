# plan — 목록 드래그 순서 변경(작업 그룹 · 트레이 메뉴)

## 요구 이해

**원문 요청**: "작업 그룹, 트레이 메뉴 목록도 드래그해서 순서 변경 되도록 수정해줘"
(직전 요청: "그룹 수정 화면의 목록에서 드래그해서 목록의 순서를 변경할 수 있게 수정" — 이미 반영됨)

**확정된 요구** (인터뷰 결과):

| 항목 | 내용 |
|---|---|
| 결과 | 작업 그룹 페이지의 그룹 카드와 트레이 메뉴 페이지의 폴더 카드 각각에 왼쪽 `≡` 드래그 핸들을 추가하고, 핸들을 끌어 순서를 바꾸면 `groups.json`·`folders.json` 배열 순서로 즉시 저장한다 |
| 사용자 | 그룹·폴더를 여러 개 등록해 자주 쓰는 것을 위로 올리려는 본인 |
| 지금 하는 이유 | 직전 회차에 그룹 편집 다이얼로그의 앱 목록만 드래그 재정렬이 가능해져, 상위 목록 두 곳이 비대칭으로 남았다 |
| 성공의 모습 | 핸들 드래그로 순서가 바뀌고 앱 재시작 후에도 유지되며, 트레이 좌클릭 폴더 팝업도 바뀐 순서로 나온다. 그룹 카드 본체 드래그의 작업 표시줄 핀은 그대로 동작한다 |
| 제약조건 | DDD 의존 방향 유지(App→Infrastructure→Application→Domain) · 새 리소스 키는 4개 언어 resw 모두에 추가 · 검색어가 입력된 상태에서는 핸들을 숨겨 재정렬을 막는다 |

## Goal

두 목록 페이지에 드래그 핸들 기반 순서 변경을 추가하고, 바뀐 순서를 각 JSON 파일의 배열 순서로 영속화한다. 배열 순서가 곧 표시 순서이므로 별도 Order 필드는 도입하지 않고, 전체 컬렉션을 순서대로 다시 쓰는 `ReorderAsync`를 두 리포지토리에 추가한다.

| # | 기준 | 측정 방법 |
|---|---|---|
| G1 | 솔루션이 경고 0 · 오류 0으로 빌드된다 | `dotnet build WorkGroup.slnx` |
| G2 | 전체 테스트가 실패 0으로 통과한다 (작성 시점 Domain 23 + Application 117 = 140 → 신규 케이스 11건 이상 추가되어 151 이상) | `dotnet test WorkGroup.slnx` |
| G3 | 4개 언어 resw 키 집합이 동일하고 빈 값이 없다 | `dotnet test WorkGroup.slnx` 내 `ResourceParityTests` 2건 통과 |
| G4 | 두 페이지의 재정렬 후 저장 경로가 리포지토리 계약으로 덮인다 — 순서 저장·복원 케이스가 `JsonGroupRepositoryTests`·`JsonFolderShortcutRepositoryTests`에 존재 | `grep -c "Reorder" tests/WorkGroup.Application.Tests/Json*RepositoryTests.cs` → 작성 시점 0건 → 수정 후 각 1건 이상 |
| G5 | 그룹 카드 본체의 작업 표시줄 핀 드래그 경로가 유지된다 — `OnGroupDragStarting`과 카드 `CanDrag`가 남아 있다 | `grep -n "CanDrag=\"True\" DragStarting=\"OnGroupDragStarting\"" src/WorkGroup.App/Views/WorkGroupsPage.xaml` → 1건 |

## Out of Scope

- 그룹 편집 다이얼로그의 앱 목록 — 직전 회차에 항목 전체 드래그로 이미 적용됨(핀 드래그 충돌이 없어 핸들 불요)
- 이름순·최근순 등 자동 정렬 기능 — 요청은 수동 순서 지정이다
- 폴더 팝업의 2차(하위 폴더/파일) 목록 순서 — 파일 시스템 열거 결과라 사용자가 지정할 대상이 아니다
- 검색 필터가 걸린 상태에서의 재정렬 — 보이지 않는 항목과의 상대 순서가 임의 규칙이 되어 사용자 확인으로 제외

## Decisions

| # | 결정 | 근거 |
|---|---|---|
| D1 | 순서는 JSON 배열 순서로 저장하고 `Order` 필드를 추가하지 않는다 | 두 리포지토리가 로드 시 정렬 없이 배열 순서를 그대로 반환한다. Source: `JsonGroupRepository.cs:109`, `JsonFolderShortcutRepository.cs:143-150` |
| D2 | 기존 `SaveAsync`/`UpdateAsync`를 고치지 않고 `ReorderAsync`를 새로 추가한다 | `SaveAsync`는 `FindIndex`로 제자리 갱신, 신규는 append라 순서를 표현할 수단이 없다. 기존 호출부(저장·편집)의 동작을 건드리지 않는다. Source: `JsonGroupRepository.cs:62-69` |
| D3 | 폴더 카드에도 그룹 카드와 같은 `≡` 핸들을 둔다(폴더는 핀 드래그 충돌이 없어 항목 전체 드래그도 가능하지만 쓰지 않는다) | 두 화면의 조작법이 갈리면 화면마다 다르게 배워야 한다. 인터뷰에서 사용자 확인 |
| D4 | 좌표→삽입 인덱스 계산은 WinUI 타입에 의존하지 않는 순수 함수(`Infrastructure/Ui/ListInsertionPoint.cs`)로 분리해 단위 테스트하고, `ListView`에서 항목 경계를 뽑아 넘기는 얇은 어댑터만 `App/Views/ReorderDrop.cs`에 둔다 | 두 페이지가 같은 계산을 쓰고 D3이 동작 일치를 요구한다 — 복제하면 한쪽만 고쳤을 때 두 화면이 갈린다. 순수 계산을 하위 레이어로 내려 테스트하는 선례가 이미 있다. Source: `src/WorkGroup.Infrastructure/Interop/TaskbarPopupPositioner.cs`(순수 좌표 계산) + `tests/WorkGroup.Application.Tests/TaskbarPopupPositionerTests.cs` |
| D5 | 검색 중 재정렬 차단은 항목 VM의 `CanReorder`로 핸들을 숨겨 처리한다 | `DataTemplate` 안 `x:Bind`는 항목 타입(`GroupListItem`) 컨텍스트라 페이지 VM을 볼 수 없다. 드래그 시작만 무음으로 막으면 사용자가 이유를 알 수 없고, `TrayMenuPage`에는 안내용 InfoBar가 없다. Source: `TrayMenuPage.xaml`(InfoBar 없음), `WorkGroupsViewModel.cs:92` |
| D6 | 핸들 글리프는 `&#xE700;`(GlobalNavButton, ≡)을 쓴다 | 기존 화면이 `SymbolIcon` 대신 Segoe Fluent 글리프를 직접 지정하는 관례를 따른다. Source: `GroupEditDialog.xaml:74`의 `FontIcon Glyph="&#xE70F;"` |

## Investigation Log

| 주장 | 실행한 명령 | 출력 요지 |
|---|---|---|
| 두 저장소 모두 로드 시 정렬하지 않아 배열 순서가 곧 표시 순서다 | `grep -rn "LoadAllAsync\|OrderBy\|Sort(" --include=*.cs src tests` | `OrderBy`는 `ResourceIconCatalog`·`DirectoryBrowser`·`IconService`·`InstalledAppInventory` 4곳뿐 — 두 리포지토리와 두 목록 VM에는 없음 |
| 트레이 폴더 팝업은 저장 순서를 그대로 표시한다 | 위와 동일 | `FolderListPopupWindow.xaml.cs:91`이 `_repository.LoadAllAsync()` 결과를 정렬 없이 사용 |
| 그룹 카드 드래그가 `.lnk`를 외부에 제공하는 핀 경로다 | `grep -n "DragStarting\|CanDrag\|DataPackage" -A 12 src/WorkGroup.App/Views/WorkGroupsPage.xaml.cs` | `OnGroupDragStarting`(107행)이 임시 `.lnk`를 `StorageItems` 데이터 제공자로 등록 |
| 그 드래그 소스는 카드 `Border` 자신이라 항목 전체 재정렬과 제스처가 겹친다 | `grep -n "CanDrag" src/WorkGroup.App/Views/WorkGroupsPage.xaml` | `80: <Border CanDrag="True" DragStarting="OnGroupDragStarting"` — 1건 |
| 폴더 카드에는 드래그 소스가 없어 충돌이 없다 | `cat src/WorkGroup.App/Views/TrayMenuPage.xaml` | `DataTemplate` 안에 `CanDrag`·`DragStarting` 없음 |
| 두 목록 VM의 표시 컬렉션은 `_all`의 검색 필터 결과다 | `cat -n src/WorkGroup.App/ViewModels/WorkGroupsViewModel.cs src/WorkGroup.App/ViewModels/FolderShortcutsViewModel.cs` | `ApplyFilter()`가 `Groups`/`Folders`를 `Clear` 후 재구성(각 92행·69행) |
| 신규 리소스 키는 4개 언어에 모두 넣어야 한다 | `sed -n 1,50p tests/WorkGroup.Application.Tests/ResourceParityTests.cs` | `Languages = ["ko-KR","en-US","ja-JP","zh-Hans"]` 키 집합 동일성 + 빈 값 부재를 검사 |
| 현재 ko-KR resw 키는 136개다 | `grep -c "<data name=" src/WorkGroup.App/Strings/ko-KR/Resources.resw` | `136` |
| 현재 테스트는 Domain 23 + Application 117이 모두 통과한다 | `dotnet test WorkGroup.slnx` | `통과! - 실패: 0, 통과: 23` / `통과! - 실패: 0, 통과: 117` |
| `GroupAppServiceTests`에 `IGroupRepository` 가짜 구현이 있어 인터페이스 확장 시 함께 고쳐야 한다 | `grep -rn "LoadAllAsync" tests/` | `GroupAppServiceTests.cs:198`이 `IGroupRepository`를 직접 구현 |

## 작업 단계

### T1. 그룹 순서 저장 API

- [x] **T1-1** `IGroupRepository`에 `Task<Result> ReorderAsync(IReadOnlyList<GroupId> orderedIds, CancellationToken)` 추가
- [x] **T1-2** `JsonGroupRepository`에 구현 — 저장된 그룹을 `orderedIds` 순서로 재배열해 원자적 쓰기. `orderedIds`에 없는 기존 그룹은 원래 상대 순서로 뒤에 붙이고, 저장에 없는 id는 무시한다(다른 인스턴스가 지운 경우에도 유실 없음)
- [x] **T1-3** `IGroupAppService`·`GroupAppService`에 `ReorderAsync` 위임 추가(아이콘·.lnk는 건드리지 않으므로 리포지토리로 바로 위임)
- [x] **T1-4** `GroupAppServiceTests`의 가짜 리포지토리에 `ReorderAsync` 구현 추가(컴파일 유지)
- [x] **T1-5** `JsonGroupRepositoryTests`에 케이스 3건 추가 — ① 재정렬 후 `LoadAllAsync` 순서가 지정 순서와 같다 ② `orderedIds`에서 빠진 기존 그룹이 유실되지 않는다 ③ 저장에 없는 id를 섞어 넣어도 예외 없이 무시되고 나머지 순서가 적용된다(T1-2의 세 분기 전수)
- **Files**: `src/WorkGroup.Application/Persistence/IGroupRepository.cs` · `src/WorkGroup.Application/Groups/IGroupAppService.cs` · `src/WorkGroup.Application/Groups/GroupAppService.cs` · `src/WorkGroup.Infrastructure/Persistence/JsonGroupRepository.cs` · `tests/WorkGroup.Application.Tests/JsonGroupRepositoryTests.cs` · `tests/WorkGroup.Application.Tests/GroupAppServiceTests.cs`
- **Acceptance**: 신규 테스트 3건이 통과하고 기존 Application 117건이 그대로 통과한다. 케이스는 구현이 쓰는 재배열 코드가 아니라 **새 리포지토리 인스턴스로 파일을 다시 로드한 순서**로 판정한다(직렬화까지 통과해야 참)
- **검증**: `dotnet test WorkGroup.slnx` → 실패 0, Application 통과 120 이상

### T2. 폴더 순서 저장 API

- [x] **T2-1** `IFolderShortcutRepository`에 `Task<Result> ReorderAsync(IReadOnlyList<int> orderedIds, CancellationToken)` 추가
- [x] **T2-2** `JsonFolderShortcutRepository`에 구현 — T1-2와 같은 규칙(누락 id 뒤에 유지, 미존재 id 무시)
- [x] **T2-3** `JsonFolderShortcutRepositoryTests`에 케이스 3건 추가 — T1-5와 같은 세 분기(순서 일치 / 빠진 id 유실 없음 / 미존재 id 무시)
- **Files**: `src/WorkGroup.Application/Folders/IFolderShortcutRepository.cs` · `src/WorkGroup.Infrastructure/Persistence/JsonFolderShortcutRepository.cs` · `tests/WorkGroup.Application.Tests/JsonFolderShortcutRepositoryTests.cs`
- **Acceptance**: 신규 테스트 3건 통과. `_gate` 세마포어 안에서 로드-쓰기가 이뤄져 기존 동시성 정책을 따른다
- **검증**: `dotnet test WorkGroup.slnx` → 실패 0, Application 통과 123 이상

### T3. 드래그 핸들 리소스 키

- [x] **T3-1** 4개 언어 resw에 `Common_ReorderTooltip` 추가(ko: "드래그하여 순서 변경", en/ja/zh-Hans 각 번역)
- **Files**: `src/WorkGroup.App/Strings/ko-KR/Resources.resw` · `src/WorkGroup.App/Strings/en-US/Resources.resw` · `src/WorkGroup.App/Strings/ja-JP/Resources.resw` · `src/WorkGroup.App/Strings/zh-Hans/Resources.resw`
- **Acceptance**: 각 파일 키 수 136 → 137, `ResourceParityTests` 2건 통과
- **검증**: `grep -c "<data name=" src/WorkGroup.App/Strings/*/Resources.resw` → 4파일 모두 137

### T4. 항목 VM에 재정렬 가능 플래그, 목록 VM에 이동+저장

- [x] **T4-1** `GroupListItem`·`FolderShortcutItem`에 `CanReorder`(ObservableProperty, 기본 true)와 그에 연동된 `ReorderHandleVisibility` 추가
- [x] **T4-2** 두 목록 VM의 `ApplyFilter`에서 검색어 유무로 각 항목의 `CanReorder`를 갱신
- [x] **T4-3** 두 목록 VM에 `MoveAsync(int fromIndex, int toIndex)` 추가 — `_all`과 표시 컬렉션을 함께 이동시킨 뒤 T1/T2의 `ReorderAsync`로 저장. 검색 중이면 아무것도 하지 않는다(방어). 저장이 실패해도 목록은 되돌리지 않고, 그룹 페이지는 기존 `StatusMessage` InfoBar 경로로 실패를 표시한다(다음 `LoadAsync`에서 저장된 순서로 복원)
- **Files**: `src/WorkGroup.App/ViewModels/GroupListItem.cs` · `src/WorkGroup.App/ViewModels/FolderShortcutItem.cs` · `src/WorkGroup.App/ViewModels/WorkGroupsViewModel.cs` · `src/WorkGroup.App/ViewModels/FolderShortcutsViewModel.cs`
- **Acceptance**: 빌드 경고 0. `MoveAsync`가 넘기는 순서대로 `ReorderAsync`가 저장하는 계약은 [면제 ②] `tests/WorkGroup.Application.Tests/JsonGroupRepositoryTests.cs`·`tests/WorkGroup.Application.Tests/JsonFolderShortcutRepositoryTests.cs`의 T1-5·T2-3 케이스가 덮는다. **저장 실패 시의 표시·복원 동작은 acceptance 대상이 아니다** — 그 경로를 재는 케이스가 없으므로 Deferred에 남기고 수동 확인 항목으로 보고한다
- **검증**: `dotnet build WorkGroup.slnx` → 경고 0 / 오류 0

### T5. 드롭 지점 계산 — 순수 함수 + ListView 어댑터

- [x] **T5-1** `src/WorkGroup.Infrastructure/Ui/ListInsertionPoint.cs` 신규 — WinUI 타입에 의존하지 않는 순수 계산. `Resolve(IReadOnlyList<ItemBounds> items, double y)`가 각 항목의 세로 중점을 기준으로 삽입 인덱스를 돌려주고, `IndicatorOffset(IReadOnlyList<ItemBounds> items, int insertionIndex)`가 인디케이터 Y 오프셋을 돌려준다(`ItemBounds`는 `(double Top, double Height)` readonly record struct)
- [x] **T5-2** `tests/WorkGroup.Application.Tests/ListInsertionPointTests.cs` 신규 — 빈 목록 / 첫 항목 위쪽 / 항목 중점 바로 위·아래(경계) / 마지막 항목 아래(끝 인덱스) / 인디케이터 오프셋이 삽입 지점의 위쪽 경계와 같다. 기대값은 구현식을 옮기지 않고 **손으로 계산한 좌표 상수**로 적는다
- [x] **T5-3** `src/WorkGroup.App/Views/ReorderDrop.cs` 신규 — `ListView`의 컨테이너에서 `ItemBounds` 목록을 뽑아 T5-1에 넘기는 어댑터 + 드래그 데이터 포맷 상수
- **Files**: `src/WorkGroup.Infrastructure/Ui/ListInsertionPoint.cs` · `tests/WorkGroup.Application.Tests/ListInsertionPointTests.cs` · `src/WorkGroup.App/Views/ReorderDrop.cs`
- **구조**: 계산과 UI 접근을 가른다 — 순수 계산은 `WorkGroup.Infrastructure`(`TaskbarPopupPositioner`와 같은 자리, Application.Tests가 참조하므로 테스트 가능), `ListView` 컨테이너 순회처럼 WinUI 타입이 필요한 부분만 `WorkGroup.App/Views`. 두 페이지가 같은 어댑터를 호출하므로 한쪽만 고쳐 화면이 갈리는 일이 없다. 새 스타일 토큰을 만들지 않고 인디케이터 색은 `AccentFillColorDefaultBrush` 테마 리소스를 각 페이지 XAML에서 쓴다
- **Acceptance**: T5-2 케이스 5건이 통과한다. 두 페이지가 어댑터만 호출하고 좌표 계산을 자체 복제하지 않는다 — `grep -rn "ListInsertionPoint\.\|ReorderDrop\." src/WorkGroup.App/Views/` 에서 두 페이지 코드비하인드가 `ReorderDrop`만 호출하고 `Math` 기반 좌표 계산 코드를 갖지 않는다
- **검증**: `dotnet test WorkGroup.slnx` → 실패 0, Application 통과 128 이상 · `dotnet build WorkGroup.slnx` → 경고 0 / 오류 0

### T6. 작업 그룹 페이지 UI

- [ ] **T6-1** `WorkGroupsPage.xaml` 카드에 핸들 열 추가 — `≡` FontIcon, `CanDrag`·`Visibility`를 `CanReorder`에 바인딩, 툴팁 `Common_ReorderTooltip`
- [ ] **T6-2** `GroupsList`에 `AllowDrop="True"` + `DragOver`/`Drop`/`DragLeave` 핸들러, 목록 위에 삽입 인디케이터(`Rectangle`, `IsHitTestVisible="False"`) 배치
- [ ] **T6-3** `WorkGroupsPage.xaml.cs`에 `OnReorderDragStarting`(커스텀 포맷에 인덱스 기록, `RequestedOperation = Move`)과 드롭 핸들러 추가 — 커스텀 포맷이 없는 드래그(외부 파일 등)는 `AcceptedOperation = None`으로 무시해 핀 드래그 경로와 섞이지 않게 한다
- **Files**: `src/WorkGroup.App/Views/WorkGroupsPage.xaml` · `src/WorkGroup.App/Views/WorkGroupsPage.xaml.cs`
- **Acceptance**: G5의 grep이 1건을 유지(핀 드래그 보존)하고 빌드 경고 0 — [면제 ②] 드래그/드롭은 GUI 상호작용이라 자동 테스트 대상이 아니며, 순서 저장 계약은 `tests/WorkGroup.Application.Tests/JsonGroupRepositoryTests.cs`가 덮는다
- **검증**: `dotnet build WorkGroup.slnx` → 경고 0 / 오류 0 · `grep -n "OnGroupDragStarting" src/WorkGroup.App/Views/WorkGroupsPage.xaml` → 1건

### T7. 트레이 메뉴 페이지 UI

- [ ] **T7-1** `TrayMenuPage.xaml`에 T6-1·T6-2와 동일한 핸들 열·인디케이터 추가
- [ ] **T7-2** `TrayMenuPage.xaml.cs`에 T6-3과 동일한 드래그/드롭 핸들러 추가(`ReorderDrop` 재사용)
- **Files**: `src/WorkGroup.App/Views/TrayMenuPage.xaml` · `src/WorkGroup.App/Views/TrayMenuPage.xaml.cs`
- **Acceptance**: 빌드 경고 0 — [면제 ②] T6과 같은 사유, 저장 계약은 `tests/WorkGroup.Application.Tests/JsonFolderShortcutRepositoryTests.cs`가 덮는다
- **검증**: `dotnet build WorkGroup.slnx` → 경고 0 / 오류 0

### T8. 문서 갱신

- [ ] **T8-1** `README.md`의 작업 그룹·트레이 메뉴 기능 서술에 드래그 순서 변경 추가
- [ ] **T8-2** `help.md`(앱 내 도움말)에 조작 방법 한 줄 추가 — 핸들에서 끌어야 하고 검색 중에는 불가함을 명시
- **Files**: `README.md` · `help.md`
- **Acceptance**: 두 파일에 순서 변경 서술이 존재 — `grep -c "순서 변경" README.md help.md` → 각 1건 이상. [면제 ①] 실행 경로를 바꾸지 않는 문서 수정
- **검증**: `grep -n "순서 변경" README.md help.md`

## 검증 방법

| task | 명령 | 판정 |
|---|---|---|
| T1 | `dotnet test WorkGroup.slnx` | 실패 0, Application 120건 이상 통과 |
| T2 | `dotnet test WorkGroup.slnx` | 실패 0, Application 123건 이상 통과 |
| T3 | `grep -c "<data name=" src/WorkGroup.App/Strings/*/Resources.resw` | 4파일 모두 137 · `ResourceParityTests` 통과 |
| T4 | `dotnet build WorkGroup.slnx` | 경고 0 / 오류 0 |
| T5 | `dotnet test WorkGroup.slnx` · `grep -rn "ReorderDrop\." src/WorkGroup.App/Views/` | 실패 0, Application 128건 이상 · 두 페이지 코드비하인드가 어댑터만 호출 |
| T6 | `dotnet build WorkGroup.slnx` · `grep -n "OnGroupDragStarting" src/WorkGroup.App/Views/WorkGroupsPage.xaml` | 빌드 경고 0 · 핀 드래그 1건 유지 |
| T7 | `dotnet build WorkGroup.slnx` | 경고 0 / 오류 0 |
| T8 | `grep -n "순서 변경" README.md help.md` | 각 1건 이상 |
| 전체 | `dotnet build WorkGroup.slnx` + `dotnet test WorkGroup.slnx` | 경고 0 / 오류 0, 실패 0 |

## 승인 필요 항목

- **공개 API 변경 3건** — `IGroupRepository`·`IGroupAppService`·`IFolderShortcutRepository`에 각 `ReorderAsync` 추가. 기존 메서드 시그니처는 건드리지 않으므로 기존 호출부 동작은 그대로다. 되돌리기: `git checkout -- src/WorkGroup.Application src/WorkGroup.Infrastructure tests` (커밋 전) 또는 해당 커밋 `git revert`
- **변경 규모** — 총 **26파일**(신규 3: `ListInsertionPoint.cs`·`ListInsertionPointTests.cs`·`ReorderDrop.cs`). 내역: T1 6 · T2 3 · T3 4 · T4 4 · T5 3 · T6 2 · T7 2 · T8 2. 요청이 두 페이지 + 그 저장 경로 전체를 가리키므로 범위 안이지만 파일 5개 기준을 크게 넘어 명시한다. 되돌리기: 회차 커밋 하나를 `git revert`
- **push·태그·릴리즈는 이번 계획에 없다** — 필요해지면 그 시점에 별도 승인

## Deferred / Follow-up

- [미등재:이번 회차가 처리] 그룹 편집 다이얼로그 앱 목록의 드래그 재정렬 — 직전 요청으로 이미 적용됨(`GroupEditDialog.xaml`)
- [미등재:범위 밖] 드래그로 순서를 바꾼 뒤 저장에 실패했을 때의 처리 — 롤백 없이 실패 메시지만 표시하고 다음 `LoadAsync`에서 저장된 순서로 복원된다. **이 경로를 재는 케이스는 이번 회차에 만들지 않는다**(T4 acceptance에서 제외, 수동 확인 항목으로 보고). 실패 자체가 디스크 쓰기 실패라 드문 경로이고, 롤백을 넣으려면 두 VM의 실패 처리 정책을 함께 정해야 한다
- [미판정] 트레이 메뉴 페이지에 상태 안내 InfoBar가 없어 저장 실패를 사용자에게 알릴 자리가 없다 — 이번에는 실패 시 무음(다음 로드에서 복원)

## Progress Log

- T1·T2 완료 — 두 리포지토리에 `ReorderAsync`(지정 순서 앞 배치 + 누락분 뒤 유지 + 미존재 id 무시) 추가. 구현 변이로 신규 6건의 red를 각각 확인. Application 테스트 117 → 123.

## Next Steps
