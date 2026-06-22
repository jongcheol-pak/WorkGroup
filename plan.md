# plan — 작업 표시줄(explorer) 재시작 후 트레이 아이콘 복구

## 증상 / 근본 원인 (systematic-debugging 완료)

### Symptom
작업 관리자에서 작업 표시줄(`explorer.exe`)을 강제 종료한 뒤 다시 실행하면, 알림 영역(트레이) 아이콘 목록에 WorkGroup 트레이 아이콘이 더 이상 보이지 않는다. 앱은 계속 떠 있지만 트레이에서 사라진다.

### Root Cause (확정 — 코드·공식 문서로 검증)
1. 트레이(알림 영역)는 `explorer.exe`가 소유한다. explorer가 죽으면 거기 등록된 모든 `Shell_NotifyIcon` 아이콘이 함께 사라진다(셸 정상 동작).
2. explorer가 재시작되면 셸은 `RegisterWindowMessage("TaskbarCreated")`로 등록된 **broadcast 메시지**를 모든 **top-level 창**에 보낸다. 앱은 이 신호를 받으면 `Shell_NotifyIcon(NIM_ADD)`로 아이콘을 **재등록**해야 한다(앱 책임, 셸이 자동으로 안 해줌).
3. 현재 `TrayIconService`는 이 `TaskbarCreated`를 **수신·처리하지 않는다**. `WindowProc`(`TrayIconService.cs:103-125`)는 `WM_APP_TRAY`·`WM_COMMAND`만 처리하고 나머지는 `DefWindowProc`로 넘긴다. `NIM_ADD`는 `Initialize()`에서 **단 한 번만** 호출된다(`TrayIconService.cs:69`).
4. **추가 근본 원인 (검증)**: 현재 창은 `HWND_MESSAGE`(`TrayIconService.cs:30, 61`) 부모의 **message-only window**다. MSDN 공식 문서(Window Features): *"A message-only window ... does not receive broadcast messages."* 따라서 `WindowProc`에 `TaskbarCreated` 처리를 추가하기만 해서는 **메시지 자체가 도달하지 않는다.** 창을 hidden top-level window로 바꿔야 broadcast를 받는다.

> 검증 출처: MSDN「Window Features」(message-only window는 broadcast 미수신), MSDN「The Taskbar」/Raymond Chen 등 — TaskbarCreated는 top-level 창에 broadcast되며 수신 후 NIM_ADD 재등록이 표준 패턴.

## 목표
explorer 재시작 후에도 트레이 아이콘이 자동 복구되도록, `TrayIconService`가 `TaskbarCreated` broadcast를 수신해 `Shell_NotifyIcon(NIM_ADD)`로 아이콘을 재등록한다. 이를 위해 트레이 메시지 창을 message-only → hidden top-level로 전환한다.

## 범위
- IN:
  - `TrayIconService`의 메시지 창을 **hidden top-level window**(부모 `NULL`)로 전환. 화면/작업 표시줄/Alt+Tab 비노출 유지(`WS_EX_TOOLWINDOW`, `WS_VISIBLE` 미부여, 0 크기).
  - `Initialize()`에서 `RegisterWindowMessage("TaskbarCreated")`로 메시지 ID 확보.
  - `WindowProc`에서 해당 메시지 수신 시 아이콘 재등록.
  - 아이콘 등록 로직을 작은 private 헬퍼(`AddIcon`)로 추출해 최초 등록과 재등록에서 재사용(아이콘 핸들 재로드 없이 보관본 재사용 — 핸들 누수 방지).
  - 문서(notes/README 해당 시) 갱신.
- OUT:
  - 트레이 아이콘 외 기능(좌클릭 팝업·우클릭 메뉴·열기/종료)의 동작 변경 — 본 수정과 무관, 불변.
  - explorer가 완전히 기동하기 전 NIM_ADD 실패에 대한 재시도/지연 로직 — TaskbarCreated 자체가 "트레이 준비됨" 신호라 그 시점 NIM_ADD가 표준. 추가 재시도는 YAGNI(미채택).
  - 워킹트리에 미커밋된 사용자 진행 중 변경(`Package.appxmanifest`, `Package.StoreAssociation.xml`, `GroupEditViewModel.cs`, `GroupEditDialog.xaml`, `help.md`) — 본 수정과 무관, 손대지 않는다.

## 동작 변경 명시 (승인 대상)
- **기존**: 트레이 창은 message-only(`HWND_MESSAGE`). 아이콘은 `Initialize()`에서 1회만 NIM_ADD. explorer 재시작 시 영구 소실(앱 재시작 전까지).
- **변경 후**: 트레이 창은 hidden top-level(부모 `NULL` + `WS_EX_TOOLWINDOW`, 비표시). `TaskbarCreated` broadcast 수신 시 NIM_ADD로 아이콘 재등록 → explorer 재시작 후 자동 복구. 창은 여전히 화면/작업 표시줄/Alt+Tab에 보이지 않는다.

## 결정 사항 (전부 확정 — 미해결 결정 분기 없음)
- **D1 창 종류 = hidden top-level.** `CreateWindowEx`의 부모를 `HWND_MESSAGE`(-3) → `IntPtr.Zero`(top-level)로 변경. 근거: message-only는 broadcast 미수신(MSDN 검증). top-level만 `TaskbarCreated`를 받는다.
- **D2 top-level 비노출 = `WS_EX_TOOLWINDOW`(0x80) 부여.** (Q1 확정) 작업 표시줄/Alt+Tab 비노출 보장. `WS_VISIBLE` 미부여 + 0 크기와 합쳐 이중 비노출.
- **D3 아이콘 핸들 보관·재사용.** 표시 중 아이콘 핸들을 `_hIcon` 필드에 보관하고 재등록 시 그대로 재사용한다. `LoadTrayIcon`을 매 explorer 재시작마다 재호출하지 않는다(핸들 누수 방지). 소유/해제는 기존 `_ownedIcon`(owned일 때만 `DestroyIcon`) 유지 — `_hIcon`은 표시용 참조(owned면 `_ownedIcon`과 동일 핸들, 폴백이면 shared 핸들).
- **D4 재등록 분기 위치.** `RegisterWindowMessage` 반환값은 런타임 값(컴파일 상수 아님)이라 `switch case`에 못 넣는다. `WindowProc` 진입부에서 `if (!_disposed && msg == _taskbarCreatedMsg && _taskbarCreatedMsg != 0) { AddIcon(); return IntPtr.Zero; }`로 처리한 뒤 기존 `switch`로 진행.
- **D5 재등록 전략 = 선 NIM_DELETE(조건부) → NIM_ADD.** (reviewer B2 반영) `AddIcon()`은 `_added`가 true면 먼저 `Shell_NotifyIcon(NIM_DELETE)`를 best-effort로 호출(결과 무시)한 뒤 `NIM_ADD`를 호출한다. 이유: 정상 explorer 재시작 시엔 이전 등록이 이미 소실돼 NIM_DELETE가 실패(무해)하지만, 만약 이전 등록이 남아있는 드문 경우(중복) `ERROR_ALREADY_EXISTS`로 NIM_ADD가 실패하는 것을 예방한다. 최초 등록은 `_added`가 false라 NIM_DELETE를 건너뛰고 바로 NIM_ADD. `_added`는 NIM_ADD 결과로 재할당.
  - `_added` 상태 전이: 최초 Initialize → NIM_ADD 결과로 set. 재등록 성공 → true 유지. 재등록 실패 → false(다음 TaskbarCreated에서 재시도). Dispose → `_added`가 true일 때만 NIM_DELETE(기존 가드 유지).
- **D6 재등록 실패 격리.** `NIM_ADD` 실패 시 `_added=false`로 두고 무시한다(추가 재시도 로직 없음 — 다음 broadcast에서 복구). Dispose 이후 수신은 `_disposed` 가드(D4)로 무시.
- **D7 주석 정정 범위 = 둘 다 + 잔존 참조.** (Q3 확정, reviewer m1 반영) `TrayIconService` 클래스 주석(`TrayIconService.cs:6-7`)의 "메시지 전용 창" 표현과 더 이상 유효하지 않은 "plan.md T12" 잔존 참조, `App.xaml.cs:215`의 "메시지 전용 창" 표현을 모두 창 종류 변경에 맞게 정정. 동작 무관·정확성 목적.
- **D8 테스트 = 자동 테스트 미추가.** (Q2 확정) Win32 `Shell_NotifyIcon`·broadcast·실제 explorer 의존이라 단위 테스트 부적합하고, 기존에도 트레이 자동 테스트가 없다(무단 추가 금지 규칙 일치). 복구 동작은 수동 검증.

## 영향 범위 (Impact Analysis — 전수 조사)

### 심볼 사용처 (grep 전수 + Read 확인)
- `TrayIconService` 참조처: `App.xaml.cs`만(`_tray` 필드·`EnsureTray()`에서 생성/초기화/Dispose, `App.xaml.cs:25,213-232`). 다른 참조 없음(grep 확인, plan-reviewer 재확인).
- **공개 멤버 시그니처 불변**: `Initialize()`, `Dispose()`, 이벤트 `OpenRequested`/`ExitRequested`/`LeftClickRequested` 모두 시그니처 유지 → `App.xaml.cs` 호출부 **코드 변경 없음**(주석 1줄 정정 제외). caller 영향 없음.
- 변경은 `TrayIconService.cs` **내부 구현**에 한정(창 생성 인자·새 P/Invoke·새 필드·WindowProc 분기·private 헬퍼).

### WndProc 수신 메시지 집합 변화 (reviewer M1 반영)
- top-level 전환으로 트레이 창은 message-only일 때 받지 않던 추가 시스템/broadcast 메시지(`WM_SETTINGCHANGE`, `WM_DISPLAYCHANGE`, `WM_DWMCOLORIZATIONCOLORCHANGED` 등)를 `WindowProc`로 받기 시작한다.
- 그러나 새로 **처리하는** 분기는 `TaskbarCreated` 1건뿐이고, 나머지는 모두 기존대로 `default → DefWindowProc`로 흘러 **기능상 무해**하다.
- `App.xaml.cs:215-217`의 재진입 가드 전제(WndProc=UI 스레드 동기 실행 중 WinUI Window 생성 금지 → TryEnqueue 지연)는 **불변**이다: `AddIcon()`은 `Shell_NotifyIcon`만 호출하며 WinUI Window를 생성하지 않는다. 따라서 재진입 fail-fast(c0000602) 위험을 새로 만들지 않는다.

### 직렬화 / 계약
- 없음. 트레이 창은 내부 Win32 객체로 외부 계약·직렬화와 무관.

### 신규 P/Invoke
- `RegisterWindowMessage`(user32) 1건 추가. 기존 다수 user32 P/Invoke와 동일 패턴.

### 영향 받는 테스트
- `TrayIconService` 단위 테스트는 **존재하지 않음**(D8). 본 변경으로 자동 테스트 추가 없음.
- 기존 `dotnet test`(Domain/Application 단위 테스트)는 본 변경과 무관하게 계속 통과해야 함(회귀 없음 확인용).

## 위험
- **top-level 창이 의도치 않게 화면/작업 표시줄에 노출**: `WS_VISIBLE` 미부여 + 0 크기 + `WS_EX_TOOLWINDOW`로 차단(D2). 수동 검증으로 빈 창 미노출 확인.
- **NIM_ADD 중복/타이밍**: D5(선 NIM_DELETE → NIM_ADD)로 중복 예방. `TaskbarCreated`는 트레이 준비 완료 신호라 그 직후 NIM_ADD가 정석. 실패해도 다음 신호에 복구(D6).
- **실효성은 빌드로 검증 불가**: explorer 강제 종료→재시작은 GUI/셸 의존이라 헤드리스 불가. 빌드 통과까지만 자동 검증, 복구 동작은 **사용자 수동 검증** 항목으로 보고.

## 검증 방법
1. `dotnet build WorkGroup.slnx` — 경고/에러 0.
2. `dotnet test WorkGroup.slnx` — 기존 단위 테스트 회귀 없음(전부 통과).
3. **수동(F5 MSIX 배포, 헤드리스 불가 — 미검증 항목으로 보고)**:
   - 앱 실행 → 트레이 아이콘 표시 확인(기존 동작 회귀 없음).
   - 작업 관리자에서 `Windows 탐색기` 강제 종료 후 재시작(또는 다시 시작) → **트레이 아이콘이 자동으로 다시 나타나는지** 확인(RED→GREEN 핵심).
   - 트레이 좌클릭(폴더 팝업)·우클릭(열기/종료) 동작이 복구 후에도 정상인지 확인.
   - 작업 표시줄/Alt+Tab에 빈 창이 노출되지 않는지 확인.

## 작업 분해

### T1 — 트레이 창 top-level 전환 + TaskbarCreated 재등록 [Type C]
> 단일 책임(트레이 아이콘 생명주기)의 응집된 수정이라 한 task로 둔다(헬퍼 추출은 재등록을 위한 전제라 분리 시 중간 산출물이 버그를 못 고침). 구현은 ①→②→③ 순서로 진행해 회귀 격리.
- 파일: `src/WorkGroup.App/Services/TrayIconService.cs`, `src/WorkGroup.App/App.xaml.cs`(주석 1줄 정정)
- 내용:
  - ① **헬퍼 추출(동작 보존)**: 상수 추가 `WS_EX_TOOLWINDOW = 0x80`. 필드 `private IntPtr _hIcon;` 추가. 기존 `Initialize()`의 아이콘 등록 블록(`TrayIconService.cs:64-69`)을 `private void AddIcon()`로 이동 — `CreateData()` + `uFlags=NIF_MESSAGE|NIF_ICON|NIF_TIP`, `uCallbackMessage=WM_APP_TRAY`, `hIcon=_hIcon`, `szTip=...` 구성 후, `if (_added) Shell_NotifyIcon(NIM_DELETE, ref del);`(best-effort) → `_added = Shell_NotifyIcon(NIM_ADD, ref data);`. `Initialize()`는 `_hIcon = LoadTrayIcon();` 후 `AddIcon();` 호출.
  - ② **top-level 전환**: `CreateWindowEx` 부모를 `HwndMessage` → `IntPtr.Zero`, `dwExStyle`에 `WS_EX_TOOLWINDOW` 부여(크기·스타일 0, 비표시 유지).
  - ③ **TaskbarCreated 재등록**: `RegisterWindowMessage`(user32, `CharSet=Unicode`) P/Invoke + 필드 `private uint _taskbarCreatedMsg;` 추가. `Initialize()`에서 `_taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");`. `WindowProc` 진입부에 `if (!_disposed && msg == _taskbarCreatedMsg && _taskbarCreatedMsg != 0) { AddIcon(); return IntPtr.Zero; }`(기존 switch 앞).
  - ④ **주석 정정(D7)**: `TrayIconService.cs:6-7` 클래스 주석의 "메시지 전용 창" → "트레이 메시지 수신용 숨김 창(top-level)", "plan.md T12" 잔존 참조 제거. `App.xaml.cs:215`의 "메시지 전용 창" 표현 정정.
- Acceptance:
  - `dotnet build WorkGroup.slnx` 경고/에러 0.
  - 공개 멤버 시그니처 불변(`App.xaml.cs` 호출 코드 무변경, 주석만).
  - `_ownedIcon` 해제 경로 유지(Dispose 시 owned면 `DestroyIcon`), `_hIcon` 추가로 누수 없음.
  - **빌드 통과는 "코드 작업 완료"까지만 의미**한다. 핵심 복구 동작(explorer 재시작 후 아이콘 재등장)은 수동 검증 전까지 **미검증**이며, 수동 검증 전 task 체크박스 `[x]` 금지(빌드 GREEN이어도 동작은 "사용자 확인 필요"로 보고).
- Edge:
  - `RegisterWindowMessage` 실패(0) → `_taskbarCreatedMsg != 0` 가드로 분기 미발동(기존 동작 유지).
  - explorer 다중 재시작 → 매 신호마다 AddIcon. D5의 선 NIM_DELETE로 중복(ERROR_ALREADY_EXISTS) 예방.
  - 이미 `_added=true`인데 TaskbarCreated 수신 → 선 NIM_DELETE 후 재등록(D5).
  - Dispose 후 메시지 → `_disposed` 가드로 무시.
  - 폴백 아이콘(시스템 공유) 사용 시 `_hIcon`은 shared 핸들 → Dispose에서 `DestroyIcon` 미호출(`_ownedIcon==0`).
- Halt Forecast (reviewer M2 반영 — 빌드로 안 잡히고 판단이 필요한 지점은 모두 Decisions에서 선해결됨):
  - NIM_ADD 중복 처리 → **D5에서 확정**(선 NIM_DELETE). 구현 중 NIM_MODIFY 여부로 멈추지 말 것.
  - top-level 추가 메시지 처리 범위 → **Impact(M1)에서 확정**(TaskbarCreated만 처리, 나머지 DefWindowProc). 다른 메시지 처리 추가하지 말 것.
  - WS_EX_TOOLWINDOW 비노출 보장 → **D2에서 확정**. 추가 스타일 실험하지 말 것.
  - `RegisterWindowMessage` 마샬링/시그니처 오류 → 빌드 에러로 즉시 식별(공식 시그니처 `uint RegisterWindowMessage(string)` 준수, 추측 금지).

### T2 — 문서 갱신 [Type A]
- 파일: `notes.md` (+ README 해당 시)
- 내용: `notes.md` `## 최근 변경` 최상단에 이번 수정(증상/근본 원인/해결: message-only→top-level 전환 + TaskbarCreated 재등록) 상세 추가. 트레이 복구 동작이 사용자 가시 기능이면 README에 1줄 반영(현행 README 트레이 항목 유무 확인 후 결정).
- Acceptance: 문서가 실제 동작과 일치(역대조). 날짜는 확인 후 기입(현재 날짜 확인 가능 시).

## 승인 필요 항목 (1단계)
- 트레이 창 생성 방식 변경(message-only → hidden top-level) — 기존 동작 변경 가능성(D1/D2). T1.
- 신규 P/Invoke(`RegisterWindowMessage`) 추가 — T1.
- `App.xaml.cs` 주석 1줄 정정 — T1(D7).
> 수정 방향(TaskbarCreated 재등록으로 복구)은 사용자가 "구현"으로 승인. Open Questions(Q1~Q3) 사용자 확정 완료. 본 plan 전체 최종 승인 후 T1 착수.

## Out of Scope (영구 제외)
- 트레이 외 기능 동작 변경. 워킹트리 미커밋 사용자 변경(매니페스트/StoreAssociation/GroupEdit/help) 처리. explorer 부분 기동 대비 NIM_ADD 재시도 루프.

## Open Questions
- 없음 — Q1(WS_EX_TOOLWINDOW 적용)·Q2(자동 테스트 미추가, 수동 검증)·Q3(주석 둘 다 정정) 모두 사용자 확정. 구현 중 사용자에게 물을 결정 분기 0.

## Progress Log
- T1 (코드 완료, 동작 수동 검증 대기): `TrayIconService`를 message-only → 숨김 top-level 창(`WS_EX_TOOLWINDOW`)으로 전환 + `RegisterWindowMessage("TaskbarCreated")` 수신 시 `AddIcon()` 재등록(선 `NIM_DELETE`→`NIM_ADD`). 아이콘 핸들 `_hIcon` 보관 재사용, `HwndMessage` 제거, 주석 정정(클래스 + `App.xaml.cs:215`). 빌드 0/0, 테스트 132/132. 자체 검증: V-7 caller 전수 재추적(누락 0, `HwndMessage` 죽은 코드 제거 확인) + diff 스펙 대조(D1~D8 정확) + V-8 자기정직성 PASS. ⚠ V-5/V-6 reviewer subagent가 서버 과부하(529, 누적 6회)로 실행 불가 → 자체 검증으로 대체(사용자 승인).
- T2 (완료): `notes.md` `## 최근 변경` 최상단 항목 추가 + `README.md` 트레이 항목에 "작업 표시줄 재시작 시 자동 복구" 1줄. 빌드 영향 없음.
- Phase F: F-2 전체 빌드 0/0 + 테스트 132/132(Domain 23 + Application 109) 회귀 0. ⚠ F-7 plan-completion-reviewer(Opus)도 과부하 사유로 자체 plan 완료 검증 대체.
- reviewer 재실행(과부하 해소 후): spec-compliance-reviewer → BLOCKER 0/MAJOR 0/MINOR 1(D1~D8 모두 충족 확인), code-quality-reviewer → BLOCKER 0/MAJOR 0/MINOR 2(Win32 interop·재진입·리소스 관리 안전 확인). MINOR 중 방어적·일관성 2건 반영: `AddIcon`의 NIM_DELETE 분기에 `_added=false` 명시, Dispose에 `_hIcon=IntPtr.Zero` 정리(`_ownedIcon` 해제와 대칭). 취향 1건(`0x80`→`0x00000080`)은 reviewer가 "무방"이라 미반영. 반영 후 빌드 0/0·테스트 132/132 회귀 0.

## Next Steps
- 권장 다음 액션: ① 변경 커밋 승인(트레이 수정 5파일만 — 사용자 진행 중 매니페스트/StoreAssociation/GroupEdit/help 제외) → ② F5 MSIX 배포 후 작업 관리자 "Windows 탐색기 다시 시작"으로 **트레이 아이콘 자동 복구 + 빈 창 미노출** 수동 검증(RED→GREEN 핵심, 헤드리스 불가).
- reviewer 적대 검토: `spec-compliance-reviewer`·`code-quality-reviewer` 재실행 완료(BLOCKER/MAJOR 0, MINOR 반영). Opus `plan-completion-reviewer`(F-7)는 자체 검증으로 대체된 상태 — 필요 시 별도 재실행 가능(선택).
- 미처리(별도 housekeeping): `notes.md` `## 최근 변경`에 1주일 경과(≤2026-06-15) 항목 누적 — 43KB 전면 재작성 위험으로 보류. 별도 세션에서 `notes-archive/2026-06.md`로 이동 권장.
- Suggested skills: 공식 /verify(배포 후 동작 확인), 공식 /code-review.
