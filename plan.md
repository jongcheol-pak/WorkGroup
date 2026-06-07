# plan — 핀 클릭 콜드 시 트레이 상주(+팝업 표시)

## 목표
핀 클릭으로 그룹 팝업을 띄울 때 **상주(트레이) 인스턴스가 없으면**, 그 프로세스를 "팝업만 띄우고 닫히면 종료"가 아니라 **트레이에 상주(키 등록 + 트레이 아이콘 + Activated 구독)** 시키면서 팝업도 표시한다. → 이후 핀 클릭은 그 상주 인스턴스로 redirect(웜)되고 prewarm 등 이점을 받는다.

## 범위
- IN: `App.OnLaunched`의 핀 클릭 콜드 경로를 "팝업 전용 후 종료" → "트레이 상주 + 팝업 표시"로 변경. 상주 설정(키/Activated/트레이/prewarm/orphan 정리)을 `BecomeResidentInstance` 헬퍼로 추출해 메인 경로와 공유.
- OUT: 메인 창 자동 표시(트레이만 상주 — 사용자 "트레이로"), 폴더 팝업, 팝업 내용/애니메이션, groups.json·아이콘 캐시, 자동 시작(StartupTask) 동작.

## 동작 변경 명시 (사용자 승인 = 요청)
- **기존**: 앱 미실행 상태에서 핀 클릭 → 단명 프로세스가 팝업만 표시, 팝업 닫히면 프로세스 종료.
- **변경 후**: 앱 미실행 상태에서 핀 클릭 → 프로세스가 **트레이에 상주** + 팝업 표시. 팝업을 닫아도(포커스 잃음→Hide) **앱은 트레이에 남고**, 종료는 트레이 메뉴로 한다(메인 창은 안 뜸).

## 결정 사항 (확정)
- **D1 등록 방식**: 핀 경로의 `FindMainInstance()`(찾기 전용) → `AppInstance.FindOrRegisterForKey(MainInstanceKey)`(원자적 등록)로 교체. `IsCurrent` 아니면 redirect+Exit(웜, 기존과 동일 대상), `IsCurrent`면 이 프로세스가 상주가 됨(콜드 → 상주).
- **D2 상주 설정 공유**: `private void BecomeResidentInstance(AppInstance keyInstance)` 추출 — `_keyInstance` 보관 + `Activated += OnAppInstanceActivated` + `EnsureTray()` + prewarm(Low 지연) + `CleanupOrphansAsync`(fire-forget). 메인 경로(현 101-115)와 핀-상주 경로가 공유.
- **D3 팝업 표시 방식**: 상주 후 `ShowGroupPopup(groupId)`(재사용 흐름)로 표시 — 콜드 `new GroupPopupWindow(groupId)`(Closed→Exit) 대신. 닫힘 시 Hide로 상주 유지(`_groupPopup` 추적).
- **D4 메인 창 미표시**: 핀-상주 경로는 트레이만(메인 창 안 띄움). "그룹 수정"이 아닌 단순 핀이므로 팝업만.
- **D5 `FindMainInstance` 제거**: 핀 경로에서만 쓰던 헬퍼(grep 전수, 호출처 1곳=핀 경로) → `FindOrRegisterForKey` 전환으로 미사용 → 삭제.

## 영향 범위 전수 조사 (Impact Analysis)
- **변경 파일**: `src/WorkGroup.App/App.xaml.cs` 단일.
- **`OnLaunched` 핀 분기**(`App.xaml.cs:72-91`): `FindMainInstance()`(`:77`) + 콜드 팝업 블록(`:85-90`)을 `FindOrRegisterForKey`+`IsCurrent` 분기 + `BecomeResidentInstance` + `ShowGroupPopup`로 교체.
- **`OnLaunched` 메인/편집 분기**(`:93-...`): 이미 `FindOrRegisterForKey`+`IsCurrent`(`:94-100`). 상주 설정 인라인(`:101-115`)을 `BecomeResidentInstance(keyInstance)` 호출로 대체(이후 RouteEdit/ShowMainWindow는 유지).
- **`FindMainInstance()`**(`:129-135`): grep 전수 — 호출처 `:77` 1곳뿐. 전환 후 미사용 → 삭제(잔존 시 경고).
- **`BecomeResidentInstance`가 호출하는 것들**(모두 기존 존재, 심볼명 기준 — 라인은 근사): `EnsureTray()`(정의 `:191`~, ExitRequested 핸들러 포함), `OnAppInstanceActivated`(`:152`~, redirect 마샬링), `EnsureGroupPopup()`(직전 task), `CleanupOrphansAsync`(`IGroupAppService`), `_uiDispatcherQueue`(`:67` 캡처). 시그니처 변경 없음.
- **`FindMainInstance` 호출처 grep(Investigation Log)**: `grep "FindMainInstance"` → 정의(`:129`) + 호출 `:77` 1곳뿐. 전환 후 미사용 확정.
- **`EnsureTray`의 ExitRequested 주석(`:206`)**: "트레이는 메인 프로세스에만 초기화 … `_window`는 항상 메인 WindowEx — 팝업 분기는 EnsureTray 미호출로 미도달"은 **핀-상주 도입으로 거짓이 됨**(이제 핀-상주도 EnsureTray 호출 + `_window`는 null 가능). `_window?.Close()`는 null-safe라 동작은 안전하나 **주석을 갱신**해야 한다(M2).
- **`ShowGroupPopup`**(직전 task에서 `EnsureGroupPopup`+`ShowForGroup`): 핀-상주 경로에서 **UI 스레드(OnLaunched)에서 직접 호출** — 메인 경로의 `ShowMainWindow` 직접 호출과 동일 스레드. (기존 콜드도 `popup.Activate()`를 OnLaunched에서 직접 했으므로 창 표시 시점 동일.)
- **`_window`/`_groupPopup`/`ExitRequested`**: 핀-상주는 메인 창 없음 → `_window` null 유지. `ExitRequested`의 `_window?.Close()`+`_groupPopup?.Close()`는 null-safe(불변).
- **직렬화/도메인/DI/공개 API 변경 없음**. 신규 의존성 없음.
- **테스트**: App 계층 라이프사이클 — 단위 테스트 없음(AppInstance/WinUI 의존). 빌드 + GUI 수동 검증.

## 위험
- **라이프사이클·c0000602 영역**: 트레이 생성 + 창 표시 경로. `EnsureTray`는 트레이 콜백을 `TryEnqueue`로 지연(c0000602 기수정)하고, 팝업 표시는 안정화된 `ShowGroupPopup` 재사용 → 위험 낮음. 단 핀-콜드에서 트레이+팝업 동시 셋업은 신규 조합이라 GUI 검증 필수.
- **동시 핀 클릭(콜드)**: 둘이 거의 동시면 `FindOrRegisterForKey`가 원자적으로 하나만 IsCurrent → 그 프로세스가 상주, 나머지는 redirect. 상주가 redirect를 받아 팝업 재표시(ShowForGroup 토큰/재사용이 흡수).
- **동작 변경 자체**: 핀 클릭이 앱을 트레이에 상주시킨다(이전엔 단명). 사용자 요청이지만 "왜 앱이 안 꺼지지?" 혼동 가능 → 의도된 동작(트레이로 종료).
- **회귀**: 웜 redirect 경로·메인/편집 경로·자동 시작·`ExitRequested` 정리 불변(헬퍼 추출은 동작 보존). 콜드 단명 모델만 상주로 전환.

## 검증 방법
- `dotnet build WorkGroup.slnx` — 경고/에러 0(미사용 `FindMainInstance` 삭제 포함).
- `dotnet test WorkGroup.slnx` — 전체 통과(회귀 없음).
- **F5 MSIX GUI 수동 검증**: ① 앱 완전 종료 상태에서 핀 클릭 → 팝업 표시 **+ 트레이 아이콘 상주**, 팝업 닫아도 트레이 유지, 트레이로 종료 정상. ② 이미 상주 중 핀 클릭 → 기존처럼 redirect로 팝업(웜). ③ 동시/연속 핀 클릭 중복 창/크래시 없음. ④ 트레이 좌클릭(폴더)·우클릭(열기/종료)·메인 창 정상.

---

## 작업 분해

### T1. 핀-콜드 트레이 상주 + 상주 설정 헬퍼 추출  — Type D
- [x] 구현 완료
- **파일**: `src/WorkGroup.App/App.xaml.cs`
- **변경**:
  - `private void BecomeResidentInstance(AppInstance keyInstance)` 추가: `_keyInstance = keyInstance; keyInstance.Activated += OnAppInstanceActivated; EnsureTray(); _uiDispatcherQueue?.TryEnqueue(DispatcherQueuePriority.Low, () => { try { EnsureGroupPopup(); } catch { } }); _ = Services.GetRequiredService<IGroupAppService>().CleanupOrphansAsync();` (현 메인 경로 `:101-115`의 상주 설정 이동).
  - 메인/편집 경로(`:101-115`): 인라인 상주 설정 → `BecomeResidentInstance(keyInstance);` 한 줄로 대체(이후 `RouteEdit`/`ShowMainWindow` 분기 유지).
  - 핀 경로(`:77-90`) 교체: `var keyInstance = AppInstance.FindOrRegisterForKey(MainInstanceKey); if (!keyInstance.IsCurrent) { RedirectActivationTo(activation, keyInstance); Exit(); return; } BecomeResidentInstance(keyInstance); ShowGroupPopup(groupId); return;` (콜드 `new GroupPopupWindow(groupId)`+`Closed→Exit` 삭제).
  - `FindMainInstance()`(`:129`~) 삭제(미사용 — grep 호출처 `:77` 1곳뿐).
  - 주석 갱신: ① `OnLaunched` XML doc summary(`:58-62`)·콜드 주석(`:76`,`:85`)을 "상주 없으면 트레이 상주 + 팝업"으로 수정. ② **`EnsureTray`의 ExitRequested 주석(`:206`)** — "팝업 분기는 EnsureTray 미호출"이 거짓이 되므로 "핀-상주 경로에선 `_window`가 null일 수 있고 `?.Close()`가 null-safe로 처리한다"로 갱신(M2).
- **Decision Points**(11 — Type D):
  - 등록: `FindOrRegisterForKey`(원자적, D1) — 웜 redirect 동작 보존.
  - 헬퍼 추출: `BecomeResidentInstance`(D2) — 메인·핀 경로 공유, 동작 보존.
  - 팝업 표시: `ShowGroupPopup` 재사용 흐름(D3) — 닫혀도 상주.
  - 메인 창: 미표시(D4).
  - 정리: `FindMainInstance` 삭제(D5).
  - 스레드: OnLaunched(UI 스레드)에서 직접 호출 — 기존 `ShowMainWindow`/`popup.Activate` 동일.
- **Edge Cases**: 콜드 핀 → 상주+팝업(정상). 동시 콜드 핀(FindOrRegisterForKey 원자적 — 하나 상주, 나머지 redirect → **수신측 `OnAppInstanceActivated`(`:152`)의 groupId 분기가 `ShowGroupPopup` 호출로 흡수**, ShowForGroup 토큰/재사용). 상주 중 핀(IsCurrent 아님 → redirect, 기존). 팝업 닫음(Hide, 상주 유지). 상주 후 트레이 종료(ExitRequested 정리). prewarm·ShowGroupPopup 중복 생성(EnsureGroupPopup null 1회). `keyInstance` 등록 실패(이론상 없음 — FindOrRegisterForKey 계약상 항상 인스턴스 반환).
- **Halt Forecast**: 트레이+팝업 동시 셋업이 c0000602 fail-fast를 유발하면 → `EnsureTray`의 TryEnqueue 지연이 이미 방어. 빌드 에러(시그니처) 시 Phase I 1회 복귀. 실제 fail-fast는 헤드리스 미관측 → GUI 검증 권고(Halt 아님, 코드 리뷰로 안정화 패턴 확인).
- **Acceptance**: 빌드 성공(미사용 `FindMainInstance` 삭제 후 경고 0). 핀 콜드 경로가 `FindOrRegisterForKey`로 등록해 IsCurrent면 `BecomeResidentInstance`+`ShowGroupPopup`, 아니면 redirect; 메인 경로도 `BecomeResidentInstance` 공유(코드 리뷰). 실제 상주+팝업·트레이 유지·종료는 F5 GUI 수동 검증.

## 작업 의존성
- 단일 task(T1). 의존 없음.

## 문서 갱신
- `README.md`: **L61**의 "상주 인스턴스가 없으면(콜드) … 팝업만 표시 후 종료" 서술을 "상주 인스턴스가 없으면 새 인스턴스가 **트레이에 상주하며** 팝업을 표시(이후 종료는 트레이로)"로 수정(확정 위치).
- `notes.md`: 변경 내역 1줄(최신 위).

## 승인 필요 항목
- **활성화/라이프사이클 동작 변경**(핀 클릭이 앱을 트레이 상주시킴) — 사용자 요청으로 승인됨. 공개 API/직렬화/DI 변경 없음, 신규 의존성 없음.
