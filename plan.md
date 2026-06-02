# plan.md — 앱 추가 팝업에 선택(체크) 표시 + 토글

> 이전 plan(앱 아이콘 추출 개선)은 완료(git 이력). 본 plan은 "앱 추가" 팝업 UX 개선.

## 목표
"그룹 추가/수정" 다이얼로그의 **"앱 추가" 팝업**에서, 이미 추가된 앱을 목록에서 제외하지 않고 **항목 앞에 체크 아이콘**으로 표시한다.
체크된(추가된) 항목을 클릭하면 **선택 해제**, 안 된 항목을 클릭하면 **추가**(토글). (사용자 결정 D1)

## 현재 동작 (확인된 사실)
- `GroupEditViewModel.RefreshAvailable()`(GroupEditViewModel.cs:273)는 이미 추가된 앱을 팝업에서 **제외**한다(`Where(i => !selectedTargets.Contains(...))`). → 추가된 항목이 팝업에서 사라짐.
- 팝업 항목 클릭 `OnAppPickerItemClick`(GroupEditDialog.xaml.cs:90) → `ViewModel.AddApp(item.App)`.
- 선택 목록(`SelectedApps`)은 다이얼로그 본문 ListView에 표시(삭제 버튼 有, GroupEditDialog.xaml:108~).
- 편집 복원 시 `SelectedApps`는 **새 PopupAppItem 인스턴스**(GroupEditViewModel.cs:129), 팝업 `_installedItems`는 지연 로드되는 **별도 인스턴스**(:168). → 인스턴스 동일성에 의존 불가, **LaunchTarget(대소문자 무시) 기준**으로 선택 여부를 계산해야 함.

## 결정 (사용자 승인)
- **D1**: 토글 동작. 체크 안 됨→클릭→추가(체크), 체크됨→클릭→제거(해제).
- **D2**: 선택 여부는 `SelectedApps`의 LaunchTarget 집합 기준. 체크 아이콘은 팝업 항목(`_installedItems`)에 표시.
- **D3**: 본문 선택 목록(삭제 버튼 포함)은 현행 유지. 팝업 체크와 양방향 동기(둘 다 `RefreshAvailable`로 갱신).

## 범위
- In scope: 팝업 목록 전체 표시(제외 제거) + 체크 아이콘 + 클릭 토글.
- Out of scope: 본문 선택 목록 레이아웃/삭제 동작, 검색 동작, 아이콘 추출, 도메인/저장 로직, 공개 API 변경.

## 영향 범위 (전수)
- `PopupAppItem`(ViewModels): `IsSelected` 표시용 속성 추가. 사용처 = 팝업 런처/그룹목록 미니아이콘/다이얼로그 — **추가 속성은 미사용처에 무해**(기존 바인딩 불변).
- `GroupEditViewModel`: `RefreshAvailable`(제외 제거 + `IsSelected` 설정), `ToggleApp(AppEntry)` 추가. `AddApp`/`RemoveApp`는 `RefreshAvailable` 호출 유지(IsSelected 재계산).
- `GroupEditDialog.xaml`: 팝업 DataTemplate에 체크 아이콘 열 추가.
- `GroupEditDialog.xaml.cs`: `OnAppPickerItemClick` → `ToggleApp`.
- 호출처 grep: `AddApp` = xaml.cs:94(picker, 토글로 대체) + VM 내부. `RemoveApp` = xaml.cs:100(본문 삭제, 유지) + VM ToggleApp.

## Tasks
- [ ] **T1 — 팝업 선택 표시 + 토글** *(~1h)*
  - **Type**: C
  - **Acceptance**:
    - `PopupAppItem`에 `[ObservableProperty] bool IsSelected` + `Visibility SelectionGlyphVisibility => IsSelected ? Visible : Collapsed`(`[NotifyPropertyChangedFor]`로 갱신). `using Microsoft.UI.Xaml` 추가.
    - `GroupEditViewModel.RefreshAvailable`: 제외 로직 제거(전체 `_installedItems` 표시, 검색 필터 유지). 각 표시 항목에 `item.IsSelected = selectedTargets.Contains(item.App.LaunchTarget)` 설정.
    - `GroupEditViewModel.ToggleApp(AppEntry app)`: `SelectedApps`에 동일 타깃 있으면 그 항목 `RemoveApp`, 없으면 `AddApp(app)`.
    - `GroupEditDialog.xaml` 팝업 DataTemplate: 선두에 고정폭 체크 아이콘 열(FontIcon 체크 글리프, `Visibility="{x:Bind SelectionGlyphVisibility, Mode=OneWay}"`). 아이콘/이름 정렬 유지(고정폭 예약).
    - `GroupEditDialog.xaml.cs` `OnAppPickerItemClick` → `ViewModel.ToggleApp(item.App)`.
    - 빌드 0/0, 테스트 회귀 없음.
  - **Files**: `src/WorkGroup.App/ViewModels/PopupAppItem.cs`, `src/WorkGroup.App/ViewModels/GroupEditViewModel.cs`, `src/WorkGroup.App/Views/GroupEditDialog.xaml`, `src/WorkGroup.App/Views/GroupEditDialog.xaml.cs`
  - **Edge Cases**: 편집 복원 멤버(설치목록 없는 제거된 앱)는 팝업에 안 보이나 본문 목록엔 남음(현행). 검색 중 토글 → RefreshAvailable이 검색 필터 유지. 동일 타깃 중복 추가 방지(AddApp 기존 가드).
  - **Halt Forecast**: 없음.
  - **Depends on**: -

## 검증
- `dotnet build WorkGroup.slnx` 0/0, `dotnet test WorkGroup.slnx` 회귀 없음.
- 수동(GUI): 앱 추가 팝업에서 항목 클릭→체크 표시+본문 목록 추가, 체크된 항목 재클릭→해제+본문에서 제거, 검색 후에도 동일.

## 승인 필요 항목
- 없음(공개 API/직렬화/의존성 불변, UI 동작 변경만).

## Open Questions (해결됨)
- [x] 클릭 동작 → 토글(추가↔해제) (D1, 사용자).

## Progress Log
<!-- implement-task가 갱신 -->
