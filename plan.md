# plan.md — 데이터 저장 폴더를 그룹별 폴더 구조로 개선

> 이전 plan들은 완료(git 이력). 본 plan은 그룹 데이터 저장 경로를 그룹별 폴더로 재구성.

## 목표
그룹 데이터를 그룹별 폴더로 모은다.
- 그룹 생성/저장 시 `%USERPROFILE%\WorkGroup\Groups\{groupId}\` 폴더에 저장.
  - 아이콘: `Groups\{groupId}\Icons\{groupId}.ico`, `Groups\{groupId}\Icons\{groupId}.png`
  - 바로가기: `Groups\{groupId}\{그룹이름}.lnk`
- 그룹 삭제 시 `Groups\{groupId}\` 폴더 전체 삭제.
- `groups.json`은 전체 인덱스이므로 루트(`%USERPROFILE%\WorkGroup\`) 유지.

## 결정 사항 (사용자 승인)
- **D1. 폴더 키 = 그룹 id(GUID)**. 이름 변경에도 폴더 유지(이동 불필요). 폴더명은 `{groupId}.Value`.
- **D2. 마이그레이션 없음**. 새 구조는 신규/재저장부터 적용. 기존 flat `Icons\`·`Shortcuts\` 파일은 그대로 둔다(수동 정리).
- **D3(기술결정). .lnk 파일명**: 그룹 폴더 안에서 `{SanitizeFileName(이름)}.lnk` 유지(작업표시줄 라벨용). 이름 변경 시 같은 id 폴더 안에서 파일명만 바뀌므로, CreateOrUpdate가 폴더 내 기존 `*.lnk`를 먼저 제거해 잔여 .lnk를 정리.
- **D4(기술결정). 아이콘 파일명**: `{groupId}.ico/.png` 유지(폴더가 id라 중복이지만 IconService/Loader 단순성 유지).
- **D5(기술결정). 삭제/롤백 단위**: 그룹 폴더(`Groups\{id}`) 단위로 정리(아이콘+lnk 동시). 부분 저장 실패 롤백도 폴더 삭제.

## 범위(Scope)
- In: 경로 스킴을 그룹별 폴더로 변경(WorkGroupPaths/ShortcutService/GroupAppService/GroupIconLoader/DI) + 관련 테스트 갱신.
- Out: 기존 데이터 마이그레이션, 작업표시줄 핀 자동 재등록, IconService 알고리즘/IIconService·IShortcutService·IGroupAppService 인터페이스 시그니처 변경, groups.json 위치/형식 변경, 도메인 변경.

## 위험 (Risks)
- **R1. 기존 핀(.lnk) 경로 변경**: 기존 작업표시줄 핀은 옛 `Shortcuts\{이름}.lnk`를 가리켜, 새 구조로 저장하면 핀이 깨진다(재등록 필요). 마이그레이션 없음 결정에 따라 수용. 신규 핀은 새 경로 사용.
- **R2. 기존 그룹 표시**: 기존 그룹은 새 경로에 아이콘이 없어 폴백 표시. 한 번 재저장하면 새 폴더로 생성. (D2 수용)
- **R3. 폴더 삭제 실패**: 아이콘/lnk가 셸·작업표시줄에 사용 중이면 삭제 실패 가능 → try/catch best-effort(현행 파일 삭제와 동일 정책).
- **R4. 생성자 의미 변경**: ShortcutService/GroupAppService의 디렉터리 인자 의미가 "flat 디렉터리"→"Groups 루트"로 바뀜(타입은 string 동일). DI에서 `GroupsDirectory` 주입으로 최종 정합. 인터페이스 시그니처는 불변.
- **R5. GroupId.Value 폴더 안전성**: 폴더명으로 사용. GroupId.cs 확인 결과 `Guid.NewGuid().ToString("N")`(하이픈 없는 hex) → 폴더명 안전.
- **R6. 백그라운드 cleanup vs 저장 경합(수용)**: `CleanupOrphansAsync`는 App 시작 시 fire-and-forget 1회 호출(App.xaml.cs:59). 폴더 재귀 삭제는 flat 파일 삭제보다 영향이 크다. 단 (1) 시작 시 1회만 실행, (2) repo 스냅샷을 한 번 읽어 그 기준으로만 삭제, (3) 사용자 그룹 저장은 이후 사용자 액션 시점이라 실제 경합은 거의 없음 → 단일 사용자 데스크톱 전제로 **수용**(현행 fire-and-forget 정리 정책과 동일, blast radius만 폴더 단위로 커짐). 추가 가드는 도입하지 않는다.

## Investigation Log
- WorkGroupPaths.cs: `IconsDirectory`=Root\Icons, `ShortcutsDirectory`=Root\Shortcuts, `ConfigDirectory`=Root. (Read)
- ShortcutService.cs: `_shortcutsDirectory` 주입, `{Sanitize(name)}.lnk` 생성, CleanupOrphans는 flat 디렉터리 열거. (Read)
- GroupAppService.cs: `_iconsDirectory` 주입, SaveAsync(아이콘→lnk→json) 순서·롤백, DeleteAsync(shortcut.Delete + TryDeleteIcon), CleanupOrphansAsync(shortcut.CleanupOrphans + flat .ico/.png 고아 제거). (Read)
- GroupIconLoader.cs: `WorkGroupPaths.IconsDirectory`+`{id}.ico/.png`. 사용처 = GroupListItem.cs(:61/:65), GroupEditViewModel.cs(:311 .ico 미리보기). (grep+Read)
- ServiceConfiguration.cs: ShortcutService(ShortcutsDirectory), GroupAppService(IconsDirectory) 주입. (Read)
- IconService.CreateGroupIconAsync는 `outputDirectory` 인자를 그대로 사용 → 시그니처 불변, 호출자(GroupAppService)가 per-group Icons 폴더를 넘기면 됨. (Read)
- 호출처 grep(전수): `IconsDirectory`/`ShortcutsDirectory` = ServiceConfiguration·GroupIconLoader·WorkGroupPaths 자기 자신뿐. `GetShortcutPath` = WorkGroupsPage.xaml.cs:75(드래그 핀), IShortcutService. `CleanupOrphansAsync` 호출 = App.xaml.cs:59(시작 시 1회).
- `GroupId.Value` 포맷: **확인 필요(T1 P단계)** — GUID "N"(하이픈 없는 hex)면 폴더명 안전. 확인 후 진행.
- 테스트: GroupAppServiceTests(flat 아이콘 경로 단정 + Fake들), ShortcutServiceTests(flat .lnk 경로 단정), IconServiceTests(outputDirectory 직접 — 영향 없음).

## 영향 범위 전수 조사 (Impact Analysis)
### 4-A. 변경 심볼/사용처
| 심볼/대상 | 사용처(전수) | 처리 |
|---|---|---|
| `WorkGroupPaths.IconsDirectory`/`ShortcutsDirectory` | ServiceConfiguration, GroupIconLoader | 제거 → `GroupsDirectory`/`GroupIconsDirectory(id)`로 대체(T5에서 제거, 그 전 task가 신규 멤버로 전환) |
| `ShortcutService` ctor(dir) + 경로 로직 | DI(ServiceConfiguration), ShortcutServiceTests | per-group 폴더화(T2) + 테스트 갱신 |
| `GroupAppService` ctor(iconsDir) + 저장/삭제/정리 | DI, GroupAppServiceTests | per-group화(T3) + 테스트 갱신 |
| `GroupIconLoader.GetIconPath/GetPngPath` | GroupListItem, GroupEditViewModel | per-group 경로(T4). 시그니처 불변(GroupId 인자) → 호출자 무수정 |
| DI 주입 경로 | ServiceConfiguration | GroupsDirectory 주입(T5) |
### 4-B. 계약/직렬화
- 인터페이스(IIconService/IShortcutService/IGroupAppService) 시그니처 **불변**. groups.json 형식/위치 **불변**. 저장 경로(디스크 레이아웃)만 변경.
### 4-C. 영향 테스트
- GroupAppServiceTests(경로 단정·고아 테스트 갱신), ShortcutServiceTests(경로·고아 테스트 갱신). IconServiceTests 영향 없음. Domain.Tests 무관.

---

## 작업 분해 (Tasks)
> 공통: 한글 주석, UTF-8(BOM 없음), `dotnet build WorkGroup.slnx` 0/0, `dotnet test WorkGroup.slnx` 회귀 없음. 각 task는 컴파일 가능 상태 유지(런타임 정합은 T5 DI 완료 시점).

- [ ] **T1 — WorkGroupPaths 그룹별 경로 추가** *(~0.5h)*
  - **Type**: C
  - **P단계 확인**: `GroupId.Value` 포맷 Read로 확인(GUID "N" 형식이어야 폴더명 안전). 다르면 Halt.
  - **Acceptance**: 신규 멤버 추가(기존 `IconsDirectory`/`ShortcutsDirectory`는 T5까지 유지):
    - `GroupsDirectory => Path.Combine(RootDirectory, "Groups")` (public — DI 사용)
    - `GroupIconsDirectory(string groupId) => Path.Combine(GroupsDirectory, groupId, "Icons")` (public — GroupIconLoader 사용)
    - 별도 public `GroupDirectory`는 두지 않는다(미사용 멤버 금지 — YAGNI). 그룹 폴더 경로는 ShortcutService/GroupAppService가 주입받은 루트로 자체 계산.
    - 클래스 doc 주석을 새 구조(그룹별 폴더)에 맞게 갱신.
    - 빌드 0/0.
  - **Files**: `src/WorkGroup.Infrastructure/WorkGroupPaths.cs`
  - **Edge Cases**: groupId 빈 문자열은 호출 없음(항상 유효 id). 한글 주석.
  - **Halt Forecast**: GroupId.Value가 경로 부적합 문자 포함 시 → Halt(설계 재확인).
  - **Depends on**: -

- [ ] **T2 — ShortcutService 그룹별 폴더화 + 테스트** *(~1.5h)*
  - **Type**: D
  - **Acceptance**:
    - ctor 인자 의미를 "Groups 루트"로(필드 `_groupsDirectory`로 명명, 빈 인자 검증 유지). 타입·검증 동일.
    - `GroupFolder(group) => Path.Combine(_groupsDirectory, group.Id.Value)`, `ShortcutPathFor(group) => Path.Combine(GroupFolder(group), Sanitize(name)+".lnk")`.
    - `CreateOrUpdate`: `Directory.CreateDirectory(GroupFolder)`; 폴더 내 기존 `*.lnk` 중 대상과 다른 것 제거(이름 변경 잔여 정리) 후 생성; 경로 반환.
    - `Delete`: `ShortcutPathFor(group)` 삭제(없어도 성공).
    - `GetShortcutPath`: `ShortcutPathFor`.
    - `CleanupOrphans(validGroups)`: **각 validGroup에 대해** `folder = Path.Combine(_groupsDirectory, group.Id.Value)`; 폴더가 있으면 그 안의 `*.lnk` 중 `Sanitize(group.Name)+".lnk"`만 남기고 나머지 제거(이름 변경 잔여 정리). 폴더 자체 고아(삭제된 그룹)는 GroupAppService가 폴더째 삭제하므로 여기선 다루지 않는다.
    - 클래스/메서드 주석을 새 구조(그룹별 폴더)에 맞게 갱신(예: ":11 Shortcuts 등" 문구).
    - ShortcutServiceTests를 per-group 경로로 갱신: 생성 경로 `Path.Combine(_dir, group.Id.Value, name+".lnk")` 단정, 금지문자/삭제/없는삭제/빈인자/라이터예외 유지. CleanupOrphans 테스트는 "유효 그룹 폴더 안에 현재 .lnk + 잔여 .lnk를 두고, cleanup 후 현재 것만 남는지"로 재작성.
    - 빌드 0/0, 테스트 통과.
  - **Files**: `src/WorkGroup.Infrastructure/Shortcuts/ShortcutService.cs`, `tests/WorkGroup.Application.Tests/ShortcutServiceTests.cs`
  - **Edge Cases**: 폴더 없음→생성. 같은 폴더 다중 .lnk(이름 변경 잔여)→현재 이름만 유지. 삭제 실패→경고 후 정책 현행 유지. 핀 사용 중 삭제 실패→try/catch. CleanupOrphans 시 폴더↔그룹은 `group.Id.Value`로 매핑.
  - **Halt Forecast**: CleanupOrphans에서 폴더 내 .lnk와 그룹 매핑이 모호하면(예: 폴더명이 id가 아님) → 해당 폴더 건너뜀(삭제 안 함, 안전측). 구현 막힘 시 매핑 규칙(폴더명=id) 재확인.
  - **Depends on**: -

- [ ] **T3 — GroupAppService 그룹별 폴더화 + 테스트** *(~2h)*
  - **Type**: D
  - **Acceptance**:
    - ctor 4번째 인자 의미를 "Groups 루트"로(필드 `_groupsDirectory`). helper `GroupFolder(id)`, `IconsFolder(id)=GroupFolder(id)\Icons`.
    - `SaveAsync`: `iconsDir = IconsFolder(group.Id)` 생성 후 `CreateGroupIconAsync(group.Id, group.Icon, group.Apps, iconsDir)`. lnk→json 순서 유지.
      - lnk 실패 롤백: `TryDeleteGroupFolder(group.Id)`.
      - json 실패 롤백: `_shortcutService.Delete(group)` + `TryDeleteGroupFolder(group.Id)`.
    - `DeleteAsync(id)`: 그룹 조회 후 있으면 `_shortcutService.Delete(group)`(계약 유지) + `TryDeleteGroupFolder(id)` + `repository.DeleteAsync(id)`.
    - `CleanupOrphansAsync`: 유효 id 집합 계산 → `GroupsDirectory`(=_groupsDirectory) 하위 폴더 중 이름이 유효 id가 아닌 폴더 재귀 삭제 + `_shortcutService.CleanupOrphans(groups)` 호출(폴더 내 잔여 .lnk 정리 위임).
    - 신규 `TryDeleteGroupFolder(id)`(recursive, try/catch). 기존 `TryDeleteIcon`/Icon·Png 상수 제거.
    - GroupAppServiceTests 갱신: `_iconsDir`→`_groupsDir` 의미, 아이콘 경로 단정을 `Path.Combine(_groupsDir, id, "Icons", id+".ico")`로, DeleteAsync는 그룹 폴더 삭제 확인, CleanupOrphans 테스트는 고아 "폴더"(`_groupsDir\orphan-id\`) 생성→삭제 확인 + 유효 그룹 폴더 보존 + `_shortcuts.CleanedOrphans` 유지. FakeIconService는 outputDirectory에 파일 생성하므로 그대로 동작.
    - 빌드 0/0, 테스트 통과.
  - **Files**: `src/WorkGroup.Application/Groups/GroupAppService.cs`, `tests/WorkGroup.Application.Tests/GroupAppServiceTests.cs`
  - **Edge Cases**: 그룹이 repo에 없음(Delete)→폴더는 id로 삭제 가능. 폴더 없음(이미 삭제)→무시. 편집 저장 실패 롤백 시 기존 폴더 삭제(직전 덮어쓴 아이콘 손실 — 수용, 현행도 동일 리스크). 고아 폴더 삭제 실패→경고. **시작 시 cleanup vs 사용자 저장 경합(R6) — repo 스냅샷 1회 기준으로만 삭제, 단일 사용자·시작 1회 전제로 수용(추가 가드 없음).**
  - **Halt Forecast**: 없음.
  - **Depends on**: -

- [ ] **T4 — GroupIconLoader 그룹별 경로** *(~0.3h)*
  - **Type**: C
  - **Acceptance**: `GetIconPath(id) => Path.Combine(WorkGroupPaths.GroupIconsDirectory(id.Value), $"{id.Value}.ico")`, `GetPngPath(id)`는 `.png`. 시그니처 불변(GroupId) → 호출자(GroupListItem/GroupEditViewModel) 무수정. 클래스 주석(":12 %USERPROFILE%\WorkGroup\Icons" 등)을 새 경로로 갱신. 빌드 0/0.
  - **Files**: `src/WorkGroup.App/Services/GroupIconLoader.cs`
  - **Edge Cases**: 파일 부재 → 호출자 폴백(현행).
  - **Halt Forecast**: 없음.
  - **Depends on**: T1

- [ ] **T5 — DI 경로 주입 + WorkGroupPaths 정리** *(~0.5h)*
  - **Type**: C
  - **Acceptance**: ServiceConfiguration에서 `ShortcutService(WorkGroupPaths.GroupsDirectory, AliasExePath, ...)`, `GroupAppService(..., WorkGroupPaths.GroupsDirectory, ...)`로 주입. WorkGroupPaths의 `IconsDirectory`/`ShortcutsDirectory` 제거(잔존 참조 0 — grep 확인). 빌드 0/0, 전체 테스트 통과.
  - **Files**: `src/WorkGroup.App/ServiceConfiguration.cs`, `src/WorkGroup.Infrastructure/WorkGroupPaths.cs`
  - **Edge Cases**: 제거 후 잔존 참조 → 빌드 에러로 검출(V-7 grep).
  - **Halt Forecast**: 없음.
  - **Depends on**: T1, T2, T3, T4

- [ ] **T6 — 문서 갱신** *(~0.3h)*
  - **Type**: A
  - **Acceptance**: README 데이터 위치를 그룹별 폴더 구조로 갱신, notes.md 항목 추가. 최종 빌드 0/0 + 테스트 통과 확인.
  - **Files**: `README.md`, `notes.md`
  - **Depends on**: T1~T5

## 의존 관계
- T1 → T4, T5. T2·T3 독립. T5 ← (T1~T4). T6 ← 전체.

## 검증 방법
- `dotnet build WorkGroup.slnx` 0/0, `dotnet test WorkGroup.slnx` 회귀 없음.
- 정적 경로 검토(헤드리스 GUI 실측 불가). 수동(GUI): 그룹 추가 시 `WorkGroup\Groups\{id}\{Icons\*.ico,.png, 이름.lnk}` 생성, 삭제 시 폴더 제거, 드래그 핀 동작.

## 승인 필요 항목
- 없음(공개 인터페이스/직렬화 불변, 의존성 추가 없음). 저장 경로(디스크 레이아웃) 변경은 사용자 요청·승인 완료. 기존 데이터 비마이그레이션 승인 완료.

## Open Questions (모두 해결됨)
- [x] 폴더 키 → 그룹 id(GUID) (D1, 사용자).
- [x] 기존 데이터 → 마이그레이션 없음(신규/재저장부터) (D2, 사용자).

## Progress Log
<!-- implement-task가 갱신 -->
