# plan.md — 작업 그룹 화면 그룹 검색 기능 추가

## 목표
트레이 메뉴 화면(`TrayMenuPage` + `FolderShortcutsViewModel`)의 **폴더 검색 UI/동작**을
작업 그룹 화면(`WorkGroupsPage` + `WorkGroupsViewModel`)에 **동일한 방식**으로 이식한다.
검색은 **그룹 이름 + 멤버 앱 이름**을 대상으로 부분일치(대소문자 무시)한다.

## 범위
### In scope
- `WorkGroupsPage.xaml` 헤더에 검색 `TextBox` 추가(트레이 메뉴와 동일 위치/스타일).
- `WorkGroupsViewModel`에 검색 필터(`SearchText` + `_all` 원본 + `ApplyFilter`) 추가.
- 검색 대상: 그룹 이름(`GroupListItem.Name`) **또는** 멤버 앱 이름(`Group.Apps[].DisplayName`).
- 신규 리소스 키 `WorkGroups_SearchPlaceholder`(4개 언어).
- `WorkGroupsPage.xaml.cs`의 외부 편집 라우팅이 검색 필터와 무관하게 동작하도록 보정(연관 파일).

### Out of scope
- 트레이 메뉴(`TrayMenuPage`/`FolderShortcutsViewModel`) 변경 없음.
- 그룹 검색 정렬/하이라이트/히스토리 등 추가 기능 없음(폴더 검색에 없는 것은 추가 안 함).
- 검색 결과 0건 전용 안내 문구 없음(폴더 검색과 동일하게, 전체 0개일 때만 빈 상태 표시).

## 현황 조사 (Investigation Log)
- 폴더 검색 패턴(`FolderShortcutsViewModel.cs:17,27-84`): `_all`(원본 List) 유지 → `SearchText`
  `OnSearchTextChanged`→`ApplyFilter`가 `Name`/`Path` 부분일치로 `Folders`(ObservableCollection) 재구성.
  `FolderCountText`는 `_all.Count`(검색 무관 전체), `IsEmpty = _all.Count == 0`(검색 무매치는 빈 상태 아님).
- 폴더 검색 UI(`TrayMenuPage.xaml:29-30`): 헤더 StackPanel 안에 `TextBox`(PlaceholderText=`TrayMenu_SearchPlaceholder`,
  `Text="{x:Bind ViewModel.SearchText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"`).
- 작업 그룹 현황(`WorkGroupsViewModel.cs:25,42,48-60`): `Groups`를 서비스에서 직접 채움(원본 분리 없음).
  `GroupCountText => _loc.Get("WorkGroups_CountFormat", Groups.Count)`. 검색 필터 없음.
- 그룹 이름: `GroupListItem.Name`(`GroupListItem.cs:26`). 멤버 앱 이름: `GroupListItem.Group.Apps[].DisplayName`
  (`AppEntry.DisplayName` 존재 확인 `AppEntry.cs:32`).
- 교차 영향(`WorkGroupsPage.xaml.cs:50-53`): `EditGroupByIdAsync`가 `ViewModel.Groups.FirstOrDefault(...)`로 조회.
  → 검색 필터가 켜진 상태(상주 중 직접 호출)에서는 대상 그룹이 `Groups`에서 빠져 편집이 무시될 수 있음 ⇒ 보정 필요.
- 리소스 키 grep: `WorkGroups_SearchPlaceholder` 미존재(신규). `WorkGroups_CountFormat`/`_Title`/`_AddTooltip` 존재.
- 검색 placeholder 다국어 기존값(폴더): ko"폴더 검색...", en"Search folders...", ja"フォルダーを検索...", zh"搜索文件夹...".
- 참조 grep `GroupCountText|WorkGroupsViewModel`: `ServiceConfiguration.cs`(DI 등록), `WorkGroupsPage.xaml`(바인딩),
  `WorkGroupsPage.xaml.cs`(사용), VM 자신. App 레이어라 단위 테스트 없음(테스트는 Domain/Application만).

## 위험
- (낮음) `GroupCountText` 기준을 `Groups.Count`→`_all.Count`로 변경: 검색 중에도 전체 개수 유지(폴더와 동일, 의도된 변경).
- (낮음) 외부 편집 라우팅이 필터된 `Groups`를 보던 문제 → 전체 원본(`_all`) 기준 조회로 보정(연관 파일 수정).
- (낮음) resw 신규 키 추가 → **클린 빌드 필요**(증분 빌드는 PRI에 신규 키 누락 가능, notes.md i18n 항목 경고).

## Impact Analysis (전수 조사)
### 4-A 심볼/타입
- `WorkGroupsViewModel` 참조처(grep 전수): `ServiceConfiguration.cs`(Transient 등록, 시그니처 무변경 → 영향 없음),
  `WorkGroupsPage.xaml`(`ViewModel.Groups`/`GroupCountText`/`EmptyVisibility`/`StatusMessage` 바인딩 — 기존 멤버 유지),
  `WorkGroupsPage.xaml.cs`(`LoadAsync`/`Groups`/`DeleteAsync`/`StatusMessage` 사용).
- `Groups`(ObservableCollection) 공개 멤버는 **유지**(XAML 바인딩 대상). 내부적으로 채우는 소스만 `_all`→필터로 변경.
- 신규 공개 멤버: `SearchText`(ObservableProperty), `AllGroups`(IReadOnlyList, 코드비하인드 조회용). 제거/시그니처 변경 없음.
### 4-B 계약·직렬화
- 직렬화/저장 형식 변경 없음(groups.json 무관). 도메인/리포지토리 무변경.
### 4-C 영향 테스트
- App 레이어 ViewModel 단위 테스트 없음(`tests/`는 Domain/Application). 신규 테스트 불필요(기존 컨벤션 일치).
- 회귀: Domain 23 / Application 92 그대로 통과해야 함(변경이 App 레이어 한정).

## 작업 분해

### T1. WorkGroupsViewModel 검색 필터 추가 — Type C
**파일**: `src/WorkGroup.App/ViewModels/WorkGroupsViewModel.cs`
**내용**(폴더 VM 패턴 이식):
- `private readonly List<GroupListItem> _all = new();` 추가.
- 생성자에서 `SearchText = string.Empty;` 초기화.
- `[ObservableProperty] public partial string SearchText { get; set; }` 추가.
- `partial void OnSearchTextChanged(string value) => ApplyFilter();` 추가.
- `LoadAsync` 변경: 서비스 결과로 `_all`을 재구성(각 항목 생성 + `_ = item.LoadAsync()` 아이콘 1회 로드) →
  `ApplyFilter()` 호출 → `OnPropertyChanged(nameof(GroupCountText))`. (현행처럼 `Groups`에 직접 add 하지 않음.)
- `ApplyFilter()` 신규(private): `SearchText.Trim()` 쿼리로 `Groups.Clear()` 후 `_all`에서
  `query.Length == 0 || item.Name.Contains(query, OrdinalIgnoreCase)
   || item.Group.Apps.Any(a => a.DisplayName.Contains(query, OrdinalIgnoreCase))` 인 항목만 add.
  끝에 `IsEmpty = _all.Count == 0;`.
- `GroupCountText`를 `_all.Count` 기준으로 변경: `_loc.Get("WorkGroups_CountFormat", _all.Count)`.
- `public async Task<GroupListItem?> FindByIdAsync(string groupId)` 추가 — `_all`(원본 전체)에서 id 조회,
  미로드면 1회 `LoadAsync`. (내부 상태 `_all`을 노출하지 않도록 메서드로 캡슐화 — 품질 리뷰 M1 반영.)
- `using System;`(StringComparison)/`using System.Linq;` 필요 시 추가(기존 using 확인 후).

**Acceptance**: 빌드 0 error. 검색어 입력 시 `Groups`가 이름/멤버앱이름 부분일치 항목만 포함하고,
빈 검색어면 전체 표시. `GroupCountText`는 검색과 무관하게 전체 개수.

**Edge Cases**:
- 빈/공백 검색어(`Trim()` 후 길이 0) → 전체 표시.
- `_all` 0개(그룹 없음) → `Groups` 0개 + `IsEmpty=true`(빈 상태 안내).
- 검색 무매치(그룹은 있으나 일치 0) → `Groups` 0개 + `IsEmpty=false`(빈 목록만, 안내 미표시 — 폴더와 동일).
- 멤버 앱 0개 그룹 → `Any(...)`가 false, 이름만으로 매칭.

**Halt Forecast**:
- 기존 using에 `System.Linq` 없으면 `Any`/`Contains(string)` 사용을 위해 추가(빌드 에러로 즉시 식별).
- `ApplyFilter`가 호출될 때마다 `IsEmpty = _all.Count == 0`을 재설정하므로(검색 입력·로드 모두 경유),
  `[NotifyPropertyChangedFor(nameof(EmptyVisibility))]`(기존)로 빈 상태 통지가 항상 정상 — 폴더 VM과 100% 동일 흐름.

### T2. WorkGroupsPage.xaml 검색 TextBox 추가 — Type C
**파일**: `src/WorkGroup.App/Views/WorkGroupsPage.xaml`
**내용**: 헤더 StackPanel(`Grid.Row=0`) 안, 제목/부제 StackPanel과 [개수·추가] Grid **사이**에
트레이 메뉴와 동일한 검색 TextBox 삽입:
```xml
<TextBox x:Name="SearchBox" PlaceholderText="{loc:Localize Key=WorkGroups_SearchPlaceholder}"
         Text="{x:Bind ViewModel.SearchText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
```
(헤더 StackPanel `Spacing="8"`은 **의도적으로 유지** — 트레이 메뉴는 12지만 작업 그룹 기존 토큰을 바꾸지 않는다(범위 밖).
개수·추가 Grid의 기존 `Margin="0,16,0,0"` 유지. 검색창↔개수줄 간격은 수동 GUI 검증에서 확인.)

**Acceptance**: 빌드 0 error. 페이지에 검색창이 제목 아래·목록 위에 표시되고 입력이 `SearchText`에 양방향 바인딩.

**Edge Cases**: 없음(정적 마크업). `x:Bind`가 신규 `ViewModel.SearchText`를 찾으므로 T1 선행 필요.

### T3. 외부 편집 라우팅 보정 — Type C
**파일**: `src/WorkGroup.App/Views/WorkGroupsPage.xaml.cs`
**내용**: `EditGroupByIdAsync`가 필터된 `Groups` 대신 원본 전체를 조회하도록 VM 메서드로 위임:
- 기존 로드 가드 + `ViewModel.Groups.FirstOrDefault(...)` 2줄을 `var item = await ViewModel.FindByIdAsync(groupId);` 1줄로 교체.
(검색 필터가 켜진 상태에서도 "그룹 수정" 외부 요청이 대상 그룹을 찾도록. 로드 가드는 VM이 담당.)

**Acceptance**: 빌드 0 error. 검색어가 입력된 상태에서 외부 편집 라우팅이 들어와도 대상 그룹 편집 다이얼로그가 열림(코드 경로상).

**Edge Cases**:
- 대상 그룹이 실제로 삭제됨 → `FirstOrDefault` null → 기존대로 메인 창만 표시(return).
- 상주 직접 호출 시 `_all` 미로드(0개) → `LoadAsync()` 1회(기존 가드 유지, 기준만 `AllGroups`).

**Halt Forecast**: 없음(2줄 치환).

### T4. 리소스 키 WorkGroups_SearchPlaceholder 추가(4개 언어) — Type A
**파일**: `Strings/ko-KR`, `en-US`, `ja-JP`, `zh-Hans`/`Resources.resw`
**내용**: 각 파일에 신규 `<data name="WorkGroups_SearchPlaceholder" xml:space="preserve">` 추가:
- ko-KR: `그룹 검색...`
- en-US: `Search groups...`
- ja-JP: `グループを検索...`
- zh-Hans: `搜索群组...`

**Acceptance**: 4개 파일 모두 키 추가, 값 비어있지 않음(ResourceParityTests 통과 — 4언어 키 패리티/빈값 검사).

**Edge Cases**: 4언어 모두 추가(누락 시 ResourceParityTests 실패).

## Decision Points (해결됨)
- D1. 검색 대상 필드 = **그룹 이름 + 멤버 앱 이름**(사용자 확정). 폴더의 이름+경로에 대응.
- D2. 검색 매칭 = 부분일치 + `StringComparison.OrdinalIgnoreCase`(폴더 검색과 동일).
- D3. 개수 표시 = 전체 기준(`_all.Count`, 검색 무관) — 폴더 `FolderCountText`와 동일 정책.
- D4. 빈 상태 = 전체 0개일 때만 안내, 검색 무매치는 빈 목록만 — 폴더와 동일.
- D5. 검색 UI 위치/스타일 = 제목 아래·목록 위 `TextBox`, TwoWay+PropertyChanged — 트레이 메뉴와 동일.
- D6. `Groups`(ObservableCollection) 공개 멤버 유지(XAML 바인딩 호환) — 내부 소스만 `_all`→필터로 분리.

## 검증 방법
1. `dotnet build WorkGroup.slnx` → 0 error / 경고 확인(신규 resw 반영 위해 클린 빌드 권장).
2. `dotnet test WorkGroup.slnx` → Domain 23 / Application 92 + ResourceParityTests 통과(회귀 없음).
3. (수동, F5 MSIX) 작업 그룹 화면 검색창 표시 / 그룹 이름·멤버 앱 이름으로 필터 / 개수 유지 / 빈 검색 복원 GUI 검증.

## 승인 필요 사항
- 없음. 공개 API 시그니처 변경/구조 변경/의존성 추가/직렬화 변경 없음(공개 멤버 추가만, 제거·변경 없음).

## Task 목록 체크
- [x] T1. WorkGroupsViewModel 검색 필터 추가
- [x] T2. WorkGroupsPage.xaml 검색 TextBox 추가
- [x] T3. 외부 편집 라우팅 보정(WorkGroupsPage.xaml.cs)
- [x] T4. WorkGroups_SearchPlaceholder 4개 언어 추가

## Progress Log
- T1~T4 완료: WorkGroupsViewModel 검색 필터(_all+SearchText+ApplyFilter, 이름·멤버앱이름 매칭) + WorkGroupsPage.xaml 검색 TextBox + 외부 편집 라우팅을 FindByIdAsync로 캡슐화(품질 M1 반영) + 4개 언어 resw 키. 빌드 0/0, 테스트 23+94. spec OK, quality M1 반영 완료.

## Next Steps
- 권장 다음 액션: F5 MSIX GUI 수동 검증(검색창 표시·이름/멤버앱이름 필터·개수 유지·빈 검색 복원) 후 PR/머지.
- Suggested skills: 공식 /code-review, 공식 /verify(수동 GUI)
