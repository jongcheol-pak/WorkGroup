# plan — 작업표시줄 핀 자동 유지·복구 (별칭 재생성으로 깨진 핀 클릭 시 "열 수 없음" 수정)

## 증상 / 근본 원인 (systematic-debugging Phase 1–3 완료)

### Symptom
작업표시줄에 핀한 그룹을 클릭하면 가끔 Windows 셸 다이얼로그 "이 항목을 열 수 없음 — 항목이 제거되었거나, 이름이 바뀌었거나, 삭제되었을 수 있습니다"가 뜬다. 저장 폴더(`Groups\{guid}\`)에는 파일이 그대로 있다.

### Phase 1 — Evidence (실측)
- 실제 핀 파일(`%APPDATA%\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\*.lnk`)을 직접 읽음:
  - TargetPath = `%LOCALAPPDATA%\Microsoft\WindowsApps\WorkGroup.exe` (별칭, 버전 무관)
  - Arguments = `--group {guid}`, IconLocation = `Groups\{guid}\Icons\{guid}.ico`
  - **버전·패키지 풀네임·설치 폴더 경로 참조는 전혀 없음** → 핀은 그룹 이름/폴더명에 의존하지 않는다.
- 별칭 `WorkGroup.exe`는 **0바이트 reparse point**(APPEXECLINK). 소유 패키지 = `JongCheol.8930A247C36`(현재 설치본). 다른 두 패키지(`54127D4E99526`, `5468CEA75CF0`)는 별칭 미선언 = 동일 개발자의 별개 앱(무관).

### Phase 2/3 — Root Cause (확정)
0바이트 앱 실행 별칭은 **MSIX 설치/업데이트마다 삭제·재생성**되어 파일 ID/셸 추적 정보가 바뀐다. WorkGroup은 핀을 **드래그로 1회 생성한 뒤 다시 손대지 않는다**. 따라서 업데이트로 별칭이 재생성되면 핀 `.lnk`에 캐시된 셸 링크/추적 정보가 stale 상태가 되어, 클릭 시 셸이 "대상이 제거/이름변경/삭제됨"으로 판단한다.
- "가끔"(=업데이트 직후), "파일은 그대로인데 안 열림"(별칭·폴더·소스는 멀쩡, 캐시만 stale)을 모두 설명.
- 사용자가 말한 "파일/폴더 이름 변경"은 실제로는 업데이트 시 **패키지 설치 폴더명이 버전과 함께 바뀌는 것**을 가리킨 것 — 같은 업데이트가 별칭 재생성도 유발.

### 검증된 참조 구현 대조 (AppGroup)
같은 개발자의 동작 검증된 MSIX 앱 **AppGroup**도 핀 타깃은 동일한 별칭 방식(`AppPaths.GetStableExePath`)이다. 차이는 단 하나 — **AppGroup은 핀을 능동 유지한다**: `TaskbarManager.UpdateTaskbarShortcutIcon()`이 `User Pinned\TaskBar`의 `.lnk`를 스캔→대상/인자/아이콘 갱신→**재저장(Save)**→셸 알림(`SHChangeNotify`). 재저장이 별칭 링크 정보를 새로 고쳐 stale를 해소한다. **WorkGroup엔 이 로직이 없다.**

> notes.md 2026-06-03의 "핀은 프로그래밍 방식 자동 변경 불가" 기록은 부정확(AppGroup이 반례). 단, 그 경고가 가리키는 **핀의 표시 라벨(=`.lnk` 파일명)** 은 본 수정 범위 밖이다(아래 OUT 참조).

## 목표
검증된 AppGroup 패턴을 이식해, **앱 시작 시 WorkGroup 소유 작업표시줄 핀을 스캔→재저장→셸 알림**으로 stale 별칭 참조를 복구한다. 이미 깨진 핀도 앱 실행만으로 자동 복구되게 한다.

## 범위
- IN:
  - 신규 Application 인터페이스 `ITaskbarPinMaintainer` (유지·복구 use case 추상화).
  - 신규 Infrastructure 구현 `TaskbarPinMaintainer` — `User Pinned\TaskBar`의 `.lnk`를 IShellLink COM으로 load→식별→대상/인자/아이콘 재설정→Save, `SHChangeNotify`로 셸 갱신.
  - DI 등록 + **앱 시작 시 1회 best-effort 호출** 배선.
  - 단위 테스트(식별·재저장 로직).
  - 문서(README/notes) 갱신.
- OUT:
  - **핀 표시 라벨(`.lnk` 파일명) 변경 / 그룹 이름변경 시 라벨 동기화** — Windows가 핀 라벨을 안전하게 바꾸려면 핀 파일 rename이 필요(AppGroup은 `File.Move`로 시도)하나 위험·별개 관심사. 본 수정은 **클릭이 다시 동작**(타깃/인자/아이콘 복구)하는 것에 집중. `GroupEditViewModel.ShowRenameWarning` InfoBar는 라벨 관점에서 여전히 유효하므로 **건드리지 않는다**.
  - 핀 타깃을 프로토콜(`workgroup://`)로 전환 — 효과 불확실·기존 핀 재핀 필요라 채택 안 함.
  - 그룹 저장/이름변경 시점 추가 트리거 — 시작 시 복구로 보고된 버그(클릭 실패)는 해소됨. 시작 트리거만으로 범위 한정(YAGNI).
  - 깨진(=삭제된 그룹) 핀의 프로그래밍 방식 unpin — Windows가 신뢰성 있게 허용하지 않음. 미대상 핀은 그대로 둔다.
  - 매니페스트 Version/StoreAssociation의 워킹트리 미커밋 변경(1.0.4→1.0.5 등) — 사용자 진행 중 작업, 본 수정과 무관하므로 손대지 않는다.

## 동작 변경 명시 (승인 대상)
- **기존**: 핀은 드래그 생성 후 앱이 절대 갱신하지 않음 → 별칭 재생성 시 stale.
- **변경 후**: 앱 시작 시, `User Pinned\TaskBar`에서 **`--group {유효 guid}` 인자 + 타깃이 별칭 경로**인 `.lnk`만 식별해 별칭/인자/아이콘을 재설정 후 Save하고 `SHChangeNotify(SHCNE_UPDATEITEM)`로 갱신. 다른 앱의 핀·미식별 핀은 **읽기만 하고 변경하지 않음**.

## 결정 사항 (확정)
- **D1 트리거 = 앱 시작 1회.** resident/메인 인스턴스 시작 시 백그라운드 Task로 best-effort 호출(UI 스레드 비차단, 실패는 로그만). 매 시작 재저장은 멱등(유효 핀 재저장은 무해)이라 안전. 근거: 업데이트 직후 첫 실행에서 깨진 핀을 복구.
- **D2 식별 기준(보수적).** `.lnk`의 Arguments에서 `GroupArgs.ParseCommandLine`로 guid 추출 → **추출된 guid가 유효 그룹 목록에 존재** AND **TargetPath가 별칭 경로와 일치(또는 파일명이 `WorkGroup.exe`)**. 둘 다 만족할 때만 우리 핀으로 보고 갱신. (다른 앱 핀 오변경 방지.)
- **D3 복구 동작 = in-place 재저장.** 핀 `.lnk`를 load → `SetPath(별칭)`·`SetArguments(--group guid)`·`SetIconLocation(현재 .ico)` 재설정 → `IPersistFile.Save(같은 경로, true)`. **삭제·재생성은 금지**(삭제하면 unpin되고 재핀은 프로그래밍 불가). COM은 `Marshal.FinalReleaseComObject`로 해제(ShortcutWriter와 동일).
- **D4 COM interop 재사용.** `ShortcutWriter.cs`에 이미 있는 `IShellLinkW`/`IPersistFile`/`ShellLink`(동일 `WorkGroup.Infrastructure.Shortcuts` 네임스페이스, internal)를 그대로 사용. 신규 COM 정의 최소화. `SHChangeNotify`만 P/Invoke 신설.
- **D5 실패 격리.** 폴더 부재/COM 실패 → 로그(Warning) 후 무동작 반환. 항목별 실패 → 로그(Debug) 후 해당 항목 skip. 전체 작업이 앱 시작을 막지 않는다(best-effort, 예외 흡수).
- **D6 아이콘 경로.** `Path.Combine(WorkGroupPaths.GroupIconsDirectory(guid), guid + ".ico")`. `.ico` 부재 시 IconLocation 재설정은 skip(타깃/인자만 복구).

## 아키텍처 / 레이어 배치
- `WorkGroup.Application/Shortcuts/ITaskbarPinMaintainer.cs` — `Result RepairPins(IReadOnlyCollection<AppGroup> validGroups)`.
- `WorkGroup.Infrastructure/Shortcuts/TaskbarPinMaintainer.cs` — 구현. 생성자 주입: `taskbarPinDirectory`(테스트 주입 가능), `aliasExePath`, `groupsDirectory`, `ILogger?`. ShortcutService의 경로·로거 주입 패턴을 따른다. (ILocalizer는 RepairPins 결과가 UI에 노출되지 않고 로그로만 쓰여 불필요 — YAGNI상 미주입. 구현 중 정정.)
- `WorkGroup.Infrastructure/Interop/`(또는 Shortcuts 내) `SHChangeNotify` P/Invoke 최소 정의.
- DI: `ServiceConfiguration.cs`에 `ITaskbarPinMaintainer` 싱글턴 등록(ShortcutService 등록부 인근).
- 트리거: `App.xaml.cs` 시작 흐름에서 `IGroupRepository`로 그룹 로드 후 `ITaskbarPinMaintainer.RepairPins(groups)`를 백그라운드 best-effort 호출.

## 영향 범위 (Impact Analysis)
- 신규 파일 4개(인터페이스/구현/P-Invoke/테스트) — 기존 공개 멤버 시그니처 변경 **없음**.
- `ServiceConfiguration.cs` — DI 등록 1건 추가(기존 등록 불변).
- `App.xaml.cs` — 시작 흐름에 호출 1건 추가(기존 로직 불변, best-effort라 실패가 시작을 막지 않음).
- 도메인/직렬화/`ShortcutService`/`ShortcutWriter`/`AppLauncher` — **불변**(ShortcutWriter의 COM 타입은 재사용만, 수정 없음).
- 기존 테스트 — 불변, 계속 통과. 신규 테스트만 추가.

## 위험
- **재저장이 실제 shell-level stale를 해소하는지**: AppGroup 검증 패턴 근거로 강하게 기대되나, 단위 테스트로는 식별·재저장 *로직*만 검증 가능(별칭 재생성 시뮬레이션은 셸 내부라 불가). **실효성은 F5 배포 후 수동 재현으로 최종 확인**(아래 검증 4).
- **타 앱 핀 오변경**: D2 이중 조건(유효 guid + 별칭 타깃)으로 차단. 테스트로 "미식별 핀 불변" 단언.
- **User Pinned\TaskBar 쓰기**: MSIX `runFullTrust`로 접근 가능(AppGroup이 동일 환경에서 검증). 쓰기 실패는 항목 skip.
- **시작 지연**: 백그라운드 Task로 비차단. 핀 수는 소수라 비용 낮음.

## 검증 방법
1. `dotnet build`(솔루션) — 경고/에러 0.
2. `dotnet test` — 기존 + 신규 단위 테스트 통과.
3. 신규 단위 테스트(임시 "taskbar" 폴더 사용, 실제 COM):
   - 우리 핀(별칭+`--group {guid}`, stale 아이콘 경로) → RepairPins 후 IconLocation=현재 `.ico`, Arguments=`--group {guid}`, TargetPath=별칭으로 갱신됨.
   - 미식별 핀(notepad 타깃, `--group` 없음) → **불변**.
   - 유효 그룹 목록에 없는 guid 핀 → **불변**(미대상).
4. **수동(F5 MSIX 배포, RED→GREEN)**: ① 그룹 핀 → ② 버전 올려 재배포(별칭 재생성, 핀 깨짐 재현) → ③ 앱 재실행(시작 복구 동작) → ④ 핀 클릭이 다시 정상 실행되는지 확인. (헤드리스 불가 — 미검증 항목으로 보고.)

## 작업 분해

### T1 — Application 인터페이스 `ITaskbarPinMaintainer` [Type 신규 공개 API]
- 파일: `src/WorkGroup.Application/Shortcuts/ITaskbarPinMaintainer.cs`
- 내용: `Result RepairPins(IReadOnlyCollection<AppGroup> validGroups)` + 한글 XML 주석(역할: 작업표시줄 핀의 stale 별칭 참조 복구).
- Acceptance: 빌드 성공. Application 레이어 의존 규칙 준수(Domain만 참조).
- [x]

### T2 — Infrastructure `TaskbarPinMaintainer` 구현 [Type 신규 + COM/P-Invoke]
- 파일: `src/WorkGroup.Infrastructure/Shortcuts/TaskbarPinMaintainer.cs`(+ 필요 시 `SHChangeNotify` P/Invoke 파일)
- 내용:
  - 생성자: `(string taskbarPinDirectory, string aliasExePath, string groupsDirectory, ILogger<TaskbarPinMaintainer>? logger = null, ILocalizer? localizer = null)` + 빈 인자 검증.
  - `RepairPins`: 폴더 부재면 Ok(무동작). `*.lnk` 열거 → 각 파일 load(IPersistFile.Load) → GetArguments로 `GroupArgs.ParseCommandLine` → guid가 유효 그룹에 있고 GetPath가 별칭/`WorkGroup.exe`면 SetPath/SetArguments/SetIconLocation 재설정 후 Save → `SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATH|SHCNF_FLUSH, path)`. 항목 finally에서 COM 해제. D5 격리.
  - 표준 taskbar 경로 헬퍼: `%APPDATA%\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar`(생성자 미지정 시 기본).
- Acceptance: 빌드 성공. `ShortcutWriter`의 기존 COM 타입 재사용(중복 정의 0). 도메인/기존 서비스 불변.
- Edge: 폴더 부재, `.lnk` 0개, Arguments 빈 값, `.ico` 부재, guid 미일치, COM 예외.
- Halt Forecast: IShellLink GetPath/GetArguments 마샬링 오류 → 빌드/런타임 로그로 식별(추측 금지).
- [x]

### T3 — DI 등록 + 앱 시작 트리거 배선 [Type 호출부 변경(승인 대상)]
- 파일: `src/WorkGroup.App/ServiceConfiguration.cs`, `src/WorkGroup.App/App.xaml.cs`
- 내용:
  - DI: `ITaskbarPinMaintainer` → `new TaskbarPinMaintainer(기본 taskbar 경로, WorkGroupPaths.AliasExePath, WorkGroupPaths.GroupsDirectory, logger, localizer)` 싱글턴 등록.
  - App 시작: 리소스/그룹 로드 후 백그라운드 Task로 `IGroupRepository`(기존 로드 경로 — 구현 시 정확한 메서드 확인) → `RepairPins(groups)` best-effort 호출. 예외 흡수, UI 비차단.
- Acceptance: 빌드 성공. 시작 실패 시에도 앱 정상 기동(best-effort). 기존 시작 로직 불변.
- [x]

### T4 — 단위 테스트 [Type 테스트]
- 파일: `tests/WorkGroup.Application.Tests/TaskbarPinMaintainerTests.cs`
- 내용: 검증 3케이스(위 "검증 방법 3"). 임시 폴더 + 실제 ShortcutWriter로 `.lnk` 생성 후 RepairPins 결과 단언. `IDisposable`로 임시 폴더 정리(ShortcutServiceTests 패턴).
- Acceptance: `dotnet test` 통과.
- [x]

### T5 — 문서 갱신 [Type 문서]
- 파일: `README.md`, `notes.md`
- 내용: README에 "작업표시줄 핀 자동 유지·복구(앱 시작 시)" 동작 1줄 추가. notes `## 최근 변경` 최상단에 이번 수정 상세(증상/근본 원인/해결) 추가. 1주일 초과 항목은 날짜 확인 후 아카이브(현재 날짜 확인 가능 시).
- Acceptance: 문서가 실제 동작과 일치(역대조).
- [x]

## 승인 필요 항목 (1단계)
- 신규 공개 API(`ITaskbarPinMaintainer`) 추가 — T1.
- 신규 Infrastructure 서비스 + COM/P-Invoke(`SHChangeNotify`) — T2.
- DI 등록 + App 시작 호출부 추가 — T3.
- 신규 단위 테스트 추가 — T4.
> 수정 방향(핀 자동 유지·복구)은 사용자 승인 완료. 위 plan 전체에 대한 최종 승인 후 T1부터 구현 착수.

## Open Questions
- (구현 시 확인) App 시작에서 그룹 목록을 얻는 정확한 경로(`IGroupRepository`의 로드 메서드명). 본 plan 승인에는 영향 없음 — 기존 로드 경로 재사용.

## Progress Log
- T1~T4 구현 + V-1/V-2: `ITaskbarPinMaintainer`(Application) + `TaskbarPinMaintainer`(Infrastructure, IShellLink COM 재사용 + SHChangeNotify) + DI 등록 + `App.BecomeResidentInstance` best-effort 호출 + 단위 테스트 5케이스. 빌드 0/0, 테스트 132/132(Domain 23 + Application 109).
- V-5/V-6 리뷰 반영: (품질-M2) `SHChangeNotify` wEventId를 Win32 LONG에 맞춰 `int`로 정정. (품질-M1/m2) App 핀 복구 래퍼에 실패 로깅 추가(Result 확인 + catch 예외 기록). (스펙-m1) 테스트에 TargetPath=별칭 단언 추가. (스펙-M1) ILocalizer는 UI 미노출이라 YAGNI상 미주입 — plan 아키텍처 줄 정정. (스펙-M2/품질-m3) T5 문서 반영; GroupEditViewModel/Dialog 15→20자 변경은 본 작업 무관(사용자 진행 중 working tree 변경)이라 커밋 비포함.
- T5: README "작업 표시줄 핀 자동 유지·복구" 1줄 + notes 2026-06-22 항목 추가.

## Next Steps
- 권장 다음 액션: 변경 커밋 승인(아래 변경 파일만 — 사용자 진행 중인 매니페스트/StoreAssociation/GroupEdit 변경은 제외) → F5 MSIX 배포로 핀 깨짐 재현→재실행→클릭 정상화 수동 검증.
- Suggested skills: 공식 /verify(배포 후 동작 확인), 공식 /code-review.
- 미처리(별도 housekeeping): notes.md `## 최근 변경`에 1주일 경과(≤2026-06-15) 항목이 누적됨 — 43KB 전면 재작성 위험 때문에 본 세션 보류. 별도 세션에서 `notes-archive/2026-06.md`로 이동 권장.
