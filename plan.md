# plan — 순서 변경 드래그에 항목 카드 비주얼 표시

> 요구: `intent/2026-09-08-reorder-drag-visual.md`

## Goal

두 목록 페이지(작업 그룹·트레이 메뉴)에서 순서 변경 핸들을 끄는 동안, 마우스를 따라다니는 그림을 핸들 아이콘(≡)에서 **그 항목의 카드 전체 스냅샷**으로 바꾼다. 재정렬 조작법·삽입 표시선·검색 중 비활성·작업 표시줄 핀 드래그 비주얼은 그대로 둔다. 카드 스냅샷을 만드는 코드는 두 페이지가 이미 공유하는 `ReorderDrop` 어댑터 한 곳에만 둔다.

| # | 기준 | 측정 방법 |
|---|---|---|
| G1 | 솔루션이 경고 0 · 오류 0으로 빌드된다 | `dotnet build WorkGroup.slnx` |
| G2 | 전체 테스트가 실패 0으로 통과한다(회귀 없음, 작성 시점 Domain 23 + Application 150 = 173) | `dotnet test WorkGroup.slnx` |
| G3 | 두 페이지의 재정렬 드래그가 카드 스냅샷을 드래그 비주얼로 지정한다 | `grep -c "SetDragVisualFromItemAsync" src/WorkGroup.App/Views/WorkGroupsPage.xaml.cs src/WorkGroup.App/Views/TrayMenuPage.xaml.cs` → 작성 시점 각 0건 → 수정 후 각 1건 |
| G4 | 스냅샷 렌더 코드가 공유 어댑터 한 곳에만 있다 | `grep -c "RenderTargetBitmap" src/WorkGroup.App/Views/ReorderDrop.cs src/WorkGroup.App/Views/WorkGroupsPage.xaml.cs src/WorkGroup.App/Views/TrayMenuPage.xaml.cs` → 작성 시점 0·0·0 → 수정 후 `ReorderDrop.cs`만 1건 이상, 두 페이지 0건 |
| G5 | 두 핸들러가 비동기 렌더를 deferral로 기다린다 | `grep -c "GetDeferral" src/WorkGroup.App/Views/WorkGroupsPage.xaml.cs src/WorkGroup.App/Views/TrayMenuPage.xaml.cs` → 작성 시점 2·0 → 수정 후 3·1 |
| G6 | 카드 드래그(작업 표시줄 핀)의 비주얼 경로가 그대로다 | `grep -c "SetDragVisualFromIconAsync" src/WorkGroup.App/Views/WorkGroupsPage.xaml.cs` → 작성 시점 2건 → 수정 후 2건 |

## Out of Scope

- 재정렬 조작법 자체를 그룹 수정 화면의 `CanReorderItems` 내장 방식으로 바꾸는 것 — 작업 그룹 카드는 본체 드래그가 작업 표시줄 핀에 이미 쓰여 제스처가 겹친다(직전 회차가 핸들을 도입한 이유). 사용자가 "마우스에 항목 UI를 표시하도록만"으로 범위를 좁혔다
- 작업 표시줄 핀 드래그의 비주얼(그룹 아이콘 128px) — 2026-06-02에 카드 스냅샷에서 아이콘으로 의도적으로 바꾼 경로다(`notes.md:66`)
- 드래그 비주얼의 커서 기준 앵커 좌표(`SetContentFromSoftwareBitmap`의 `Point` 오버로드) — 기존 핀 드래그도 앵커 없이 쓰고, 요구에 없다
- 워킹트리에 미커밋 상태인 패키지 버전 상향(`WorkGroup.App.csproj` — WindowsAppSDK 2.2.0→2.4.0 등) — 이번 회차가 만든 변경이 아니며 건드리지 않는다

## Decisions

| # | 결정 | 근거 |
|---|---|---|
| D1 | 스냅샷 렌더 헬퍼를 `ReorderDrop`에 둔다(신규 파일 없음) | 그 파일이 "두 목록 페이지가 이 코드를 공유해 조작 결과가 갈리지 않게 한다"고 스스로 선언한 재정렬 전용 어댑터다. Source: `src/WorkGroup.App/Views/ReorderDrop.cs:9-14` |
| D2 | `BitmapImage`가 아니라 `SoftwareBitmap`(BGRA8 Premultiplied)으로 넘긴다 | 라이브 `BitmapImage`를 `SetContentFromBitmapImage`에 넘기면 드래그 표면에 빈 이미지로 렌더되는 것이 이 앱에서 실측된 함정이다. Source: `notes.md:65`, `WorkGroupsPage.xaml.cs:151-152,194` |
| D3 | 렌더 대상은 `ListViewItem` 컨테이너다 | 카드 `Border`는 `DataTemplate` 안이라 코드에서 직접 잡을 수단이 없고, 컨테이너 접근은 `ReorderDrop`이 이미 쓰는 방식이다. Source: `ReorderDrop.cs:42` (`list.ContainerFromIndex(i) is not ListViewItem`) |
| D4 | 드래그 비주얼 최대 폭을 상수로 두고, 넘으면 종횡비를 보존해 축소한다 | 핀 드래그가 `const uint dragSize = 128`을 "필요 시 이 값만 조정"으로 둔 선례와 같은 형태. Source: `WorkGroupsPage.xaml.cs:156-157`. **최종 리뷰 후 값 정정: 320 → 1024** — 320은 "커서 그림으로 과대하다"는 미실측 추정에서 나온 값이었고, 실측하니 카드 폭이 최대 1024라 31%로 줄어 이름이 4px가 된다. `ContentMaxWidth`와 같은 1024로 두어 현재 레이아웃에서는 원본 크기로 따라오게 하고, 상수는 향후 레이아웃 확대용 안전판으로 남긴다 |
| D5 | 렌더 실패는 무음으로 삼킨다(드래그 자체는 진행) | 비주얼은 부가 표시이고, 렌더 실패로 순서 변경을 막으면 기능이 퇴행한다. 재정렬 데이터(`ReorderDrop.IndexFormat`)는 렌더보다 먼저 설정한다 |

## Investigation Log

| 주장 | 실행한 명령 | 출력 요지 |
|---|---|---|
| 두 페이지의 재정렬 `DragStarting`은 `DragUI`를 전혀 지정하지 않는다 | `grep -n "OnReorderDragStarting" -A 20 <두 파일>` | 둘 다 `e.Data.RequestedOperation` + `e.Data.SetData(ReorderDrop.IndexFormat, ...)` 2줄뿐 (`WorkGroupsPage.xaml.cs:207-218`, `TrayMenuPage.xaml.cs:77-88`) |
| 앱의 `DragUI` 지정은 핀 드래그 1곳뿐이다 | `grep -rn "DragUI" --include=*.cs src` | 2건 — 모두 `WorkGroupsPage.xaml.cs`(주석 1 + `SetContentFromSoftwareBitmap` 1) |
| `RenderTargetBitmap`을 쓰는 코드가 없다 — 신규 도입이다 | `grep -rn "RenderTargetBitmap" --include=*.cs src` | 0건 |
| 그룹 수정 화면은 `ListView` 내장 재정렬이라 코드비하인드가 없다 | `sed -n '162,166p' GroupEditDialog.xaml` · `grep -n "Reorder\|Drag\|Drop" GroupEditDialog.xaml.cs` | XAML에 `CanDragItems="True" CanReorderItems="True" AllowDrop="True"` · 코드비하인드 0건 |
| 두 페이지의 재정렬 핸들러 이름·구조가 동일하다(같은 수정이 두 번 필요) | `grep -rln "OnReorderDragStarting" --include=*.xaml --include=*.cs src` (obj 제외) | `WorkGroupsPage.xaml(.cs)` · `TrayMenuPage.xaml(.cs)` 4파일 |
| 현재 deferral 사용 분포 | `grep -c "GetDeferral" <두 파일>` | `WorkGroupsPage.xaml.cs` 2 · `TrayMenuPage.xaml.cs` 0 |
| 현재 빌드·테스트 기준선 | `dotnet build WorkGroup.slnx` · `dotnet test WorkGroup.slnx` | 경고 0 / 오류 0 · Domain 23 통과 + Application 150 통과 (실패 0) |
| `WorkGroup.App`을 참조하는 테스트 프로젝트가 없다(면제 ④의 근거) | `grep -rn "WorkGroup.App" tests/*/*.csproj` | 0건 |
| 이 레포에는 Deferred 대장(`docs/plans/deferred.md`)이 없다 | `ls -d docs` | `No such file or directory` — 확인했더니 없음 |
| `intent/` 폴더가 없었다 | `ls -d intent` | `No such file or directory` → 이번 회차에 생성 |
| 워킹트리에 미커밋 변경이 하나 있다 | `git diff --stat` | `src/WorkGroup.App/WorkGroup.App.csproj` 4+/4- (패키지 버전 상향) — 이번 회차 범위 밖 |
| (추정) `RenderTargetBitmap.GetPixelsAsync`의 픽셀이 BGRA8 Premultiplied다 | 문서 기억 — 미실행 | T1이 빌드·수동 확인으로 확정한다. 어긋나면 알파가 검게 뭉친 스냅샷으로 드러난다 |
| 위키 참조 | `AGENTS.md`에 `## 위키` 절이 있는지 확인 | 절 없음 — 이 레포는 위키 허브를 지목하지 않는다. 세션 훅이 "WorkGroup 위키 18커밋 미반영"을 알렸으므로 기능 서술은 지도로만 쓰고 코드를 1차 출처로 삼았다 |
| 드래그 비주얼 축소율의 실제 범위(최종 리뷰 지적으로 추가 실측) | `grep -n "ContentMaxWidth\|PageContentPadding" src/WorkGroup.App/Resources/Spacing.xaml` · `grep -n "MinWidth" src/WorkGroup.App/App.xaml.cs` | `ContentMaxWidth` 1024 · `PageContentPadding` 36,24,40,24 · 창 `MinWidth` 800 → 카드 폭은 444(최소 창)~948(넓은 창 — `MaxWidth=1024` 안에 좌우 패딩 76이 포함된다. 948은 최종 리뷰어가 보정한 값). 상한 320이면 넓은 창에서 34%로 줄어 14px 이름이 5px 아래로 떨어진다 → 상한을 1024로 정정(현재 레이아웃에서는 축소가 걸리지 않는다) |

## 작업 단계

### T1. `ReorderDrop`에 카드 스냅샷 드래그 비주얼 헬퍼 추가

- [x] **T1-1** `ReorderDrop`에 `SetDragVisualFromItemAsync(ListView list, int index, DragStartingEventArgs e)` 추가 — `ContainerFromIndex(index)`로 `ListViewItem`을 잡아 `RenderTargetBitmap.RenderAsync`로 렌더하고, `GetPixelsAsync` 결과를 `SoftwareBitmap.CreateCopyFromBuffer(..., Bgra8, ..., Premultiplied)`로 감싸 `e.DragUI.SetContentFromSoftwareBitmap`에 넘긴다
- [x] **T1-2** 최대 폭 상수(`MaxDragVisualWidth`)를 두고, 카드가 그보다 넓으면 종횡비를 보존해 축소 렌더한다(D4)
- [x] **T1-3** 컨테이너를 못 잡거나 렌더가 실패하면 아무것도 지정하지 않고 조용히 반환한다(D5) — 예외를 페이지로 던지지 않는다
- **Files**: `src/WorkGroup.App/Views/ReorderDrop.cs`
- **구조**: 신규 파일을 만들지 않는다 — `ReorderDrop`은 "`ListView` 컨테이너에서 항목 경계를 뽑아 순수 계산에 넘기는 얇은 어댑터"로 선언된 재정렬 전용 파일이고(D1), 이번 코드도 같은 성격(컨테이너 접근 + WinUI 타입 의존)이라 나누면 재정렬 로직이 두 파일로 흩어진다. 레이어는 App(뷰) — `RenderTargetBitmap`·`DragUI`가 WinUI 타입이라 Infrastructure로 내릴 수 없다. 크기 상수는 핀 드래그의 `const uint dragSize` 형태를 그대로 따른다
- **Acceptance**: `grep -c "RenderTargetBitmap" src/WorkGroup.App/Views/ReorderDrop.cs` → 작성 시점 0건 → 1건 이상. [면제 ④] `tests/`의 두 프로젝트 모두 `WorkGroup.App`을 참조하지 않아(실측 0건) 이 코드에는 러너가 닿지 않는다. **변이 실증**: 헬퍼 본문을 즉시 `return`으로 바꾸면 드래그 비주얼이 다시 핸들 아이콘으로 돌아가는 것을 F5 실행으로 확인한다(**T2-4**가 그 자리다)
- **검증**: `dotnet build WorkGroup.slnx` → 경고 0 / 오류 0 · `grep -c "RenderTargetBitmap" src/WorkGroup.App/Views/ReorderDrop.cs` → 1건 이상

### T2. 두 페이지의 재정렬 핸들러에서 호출

- [x] **T2-1** `WorkGroupsPage.OnReorderDragStarting`을 `async void`로 바꾸고, `e.Data` 설정 뒤 deferral을 잡아 `ReorderDrop.SetDragVisualFromItemAsync(GroupsList, index, e)`를 await한다
- [x] **T2-2** `TrayMenuPage.OnReorderDragStarting`에 같은 변경을 적용한다(`FoldersList`)
- [x] **T2-3** 두 핸들러의 주석을 새 동작에 맞게 갱신한다 — 지금 주석은 데이터 포맷 구분만 설명한다
- [x] **T2-4** `HUMAN-VERIFY` — 헤드리스 세션에서 GUI를 띄울 수 없어 **미검증**. 사용자 F5 MSIX 실행으로 확인할 항목: ① 두 페이지에서 핸들을 끌면 카드 스냅샷이 커서를 따라온다(이 관측이 면제 ④의 변이 실증을 대신한다 — 헬퍼가 죽으면 핸들 아이콘으로 돌아간다) ② 알파가 검게 뭉치지 않는다(Investigation Log의 `(추정)` 행을 확정하는 자리다) ③ 고DPI에서 스냅샷 크기가 과대하지 않다 ④ 작업 그룹 카드 본체 드래그는 여전히 그룹 아이콘 비주얼로 작업 표시줄에 핀된다 ⑤ 삽입 표시선·검색 중 핸들 비활성이 그대로다 ⑥ **창을 넓힌 상태(카드 폭 ≈1024)에서 카드의 이름·아이콘을 알아볼 수 있다** — 최종 리뷰가 지적한 축소율 결함의 확인 지점
- **Files**: `src/WorkGroup.App/Views/WorkGroupsPage.xaml.cs` · `src/WorkGroup.App/Views/TrayMenuPage.xaml.cs`
- **Acceptance**: `grep -c "SetDragVisualFromItemAsync" <두 파일>` → 각 0건 → 각 1건 · `grep -c "GetDeferral" <두 파일>` → 2·0 → 3·1 · `grep -c "SetDragVisualFromIconAsync" src/WorkGroup.App/Views/WorkGroupsPage.xaml.cs` → 2건 유지(핀 경로 불변) · `grep -c "RenderTargetBitmap" <두 파일>` → 각 0건 유지(렌더 코드가 페이지로 새지 않음). [면제 ④] 위와 같은 사유 — 기계 검증이 닿지 않는 부분을 T2-4의 수동 확인이 받는다
- **검증**: `dotnet build WorkGroup.slnx` → 경고 0 / 오류 0 · `dotnet test WorkGroup.slnx` → 실패 0, 173건 · 위 4개 grep

### T3. 문서 갱신

- [x] **T3-1** `README.md`의 두 목록 화면 설명(35·36행)에 "핸들을 끄는 동안 항목 카드가 마우스를 따라다닌다"를 반영한다
- [x] **T3-2** `notes.md` 「최근 변경」 맨 위에 이번 회차 항목을 추가한다 — 무엇을·왜·수동 검증 대상(드래그 비주얼 모양·고DPI 크기·렌더 지연)을 함께 적는다
- **Files**: `README.md` · `notes.md`
- **Acceptance**: `grep -c "2026-09-08" notes.md` → 작성 시점 0건 → 1건 이상. [면제 ①] 실행 경로를 바꾸지 않는 문서 수정
- **검증**: `grep -n "2026-09-08" notes.md`

## 검증 방법

| task | 명령 | 판정 |
|---|---|---|
| T1 | `dotnet build WorkGroup.slnx` · `grep -c "RenderTargetBitmap" src/WorkGroup.App/Views/ReorderDrop.cs` | 경고 0 / 오류 0 · 1건 이상 |
| T2 | `dotnet build WorkGroup.slnx` · `dotnet test WorkGroup.slnx` · `grep -c "SetDragVisualFromItemAsync\|GetDeferral\|RenderTargetBitmap\|SetDragVisualFromIconAsync" <두 페이지>` · F5 수동 확인(T2-4) | 경고 0 / 오류 0 · 실패 0, 173건 · 호출 각 1건, deferral 3·1, `RenderTargetBitmap` 각 0건(G4), 핀 헬퍼 2건 유지(G6) · T2-4 ①~⑥을 보고에 기재 |
| T3 | `grep -n "2026-09-08" notes.md` | 1건 이상 |
| 전체 | `dotnet build WorkGroup.slnx` + `dotnet test WorkGroup.slnx` | 경고 0 / 오류 0, 실패 0, 173건(신규 케이스 없음 — 면제 ④) |

## 승인 필요 항목

- **`ReorderDrop` 공개 API 추가** — `SetDragVisualFromItemAsync` 1개. 기존 `IndexFormat`·`ResolveDropTarget` 시그니처는 그대로다. 영향 범위: 호출자는 두 페이지뿐(실측). 되돌리기: 커밋 전이면 `git restore src/WorkGroup.App`, 커밋 후면 `git revert`
- **두 페이지 `OnReorderDragStarting`의 `void` → `async void` 전환** — XAML 이벤트 핸들러라 외부 호출자는 없다(실측: 참조는 각 XAML 1곳). 되돌리기: 위와 같음
- **push·태그·릴리즈는 이번 계획에 없다** — 필요해지면 그 시점에 별도 승인
- 총 5파일(신규 0) — 파일 5개 기준선에 걸려 명시한다. T1 1 · T2 2 · T3 2

## Deferred / Follow-up

- [다음 회차] `WorkGroup.App`의 ViewModel 테스트 프로젝트 신설 — 직전 회차에서 이월. `tests/`의 두 프로젝트 모두 `WorkGroup.App`을 참조하지 않아 App 레이어 코드에 러너가 닿지 않는다(이번 회차의 면제 ④가 바로 그 자리다). 구조 변경이라 착수 전 별도 승인 필요
- [미등재:범위 밖] 재정렬 조작법을 그룹 수정 화면의 `CanReorderItems` 내장 방식으로 통일 — 작업 그룹 카드의 작업 표시줄 핀 드래그와 제스처가 겹쳐, 핀 드래그를 그룹 아이콘 영역으로 옮기는 별도 설계가 선행돼야 한다. 사용자가 이번 범위를 비주얼로 좁혔다
- [미등재:원리상 불가] 실화면 확인 항목 — 드래그 비주얼이 카드 모양으로 뜨는지, **알파가 검게 뭉치지 않는지**(픽셀 포맷 `(추정)` 행의 확정), 고DPI에서 크기가 적절한지, 긴 목록에서 렌더 지연이 없는지. 헤드리스로 확인할 수 없어 T2-4의 F5 MSIX 수동 검증으로 받고 그 결과를 완료 보고에 적는다

## Progress Log

- T1~T3 완료 — `ReorderDrop.SetDragVisualFromItemAsync`(카드 컨테이너 → `RenderTargetBitmap` → `SoftwareBitmap` BGRA8 Premultiplied → `DragUI`, 최대 폭 상한 1024px — ContentMaxWidth와 같은 값이라 현재 레이아웃(카드 폭 ≤948)에서는 축소가 걸리지 않는다, 실패 무음)를 두 페이지가 deferral로 호출하도록 연결하고 README·notes 갱신. 최종 검증에서 Goal 6개 전부 통과 — 빌드 경고 0/오류 0, 테스트 173/173, G3 각 1건, G4 `ReorderDrop.cs`만 1건·두 페이지 0건, G5 3·1, G6 2건 유지.
- 최종 리뷰 후속 — MAJOR 1건(`MaxDragVisualWidth` 320 → 1024, 커밋 `c1eeec1`)과 MINOR 4건(T2-4 마커 `[x]`·변이 실증 연결·위키 참조 행·축소율 실측 행) 반영. 재검토에서 남은 MINOR는 plan 안 낡은 수치 3곳뿐이었고 이 커밋으로 정정했다(320→1024, T2-4 항목 5→6개).
- T2-4(F5 실화면 확인 6항목)가 `HUMAN-VERIFY`로 남았다 — 헤드리스에서 GUI를 띄울 수 없다. 특히 픽셀 포맷 `(추정)` 행(알파 뭉침 여부)이 이 확인으로 확정된다.

## Next Steps
