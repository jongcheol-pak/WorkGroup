# Plan: WorkGroup (작업 그룹 런처)

## Goal
사용자가 PC에 설치된 앱들을 묶어 "작업 그룹"을 만들고, 그 그룹을 작업 표시줄에 핀해 클릭 한 번으로 그룹 안의 앱들을 아이콘 그리드 팝업에서 바로 실행할 수 있게 한다.

## Out of Scope
- 작업 표시줄 자체를 대체/커스터마이즈(자체 도킹 바, deskband)하는 기능 — 진짜 Windows 작업 표시줄을 사용한다.
- 그룹 클라우드 동기화, 다중 사용자 공유, 네트워크 기능.
- 앱 설치/제거 관리, 그룹 외 앱 실행 통계.
- Windows 10 / Windows 11 23H2 이하 호환 보장(대상은 사용자가 검증한 Windows 11 빌드 26300).
- Microsoft Store 인증 통과(사이드로드 설치 전제). Store 배포가 필요하면 별도 plan.

## Investigation Log
- `ls "D:/Personal Project/Windows/WorkGroup"` → 빈 디렉터리. 신규 프로젝트, 기존 코드/AGENTS.md 없음.
- WebSearch(설치 앱 열거) → Win32는 `Windows.System.Inventory.InstalledDesktopApp`, 패키지 앱은 `Windows.Management.Deployment.PackageManager.FindPackages`로 열거 가능. 전체 사용자 열거는 관리자 권한 필요, 현재 사용자 범위는 비관리자 가능.
- WebSearch(작업 표시줄 핀) → 서드파티 앱 자동 핀/`taskbarpin` verb는 차단(1709~24H2). 단, **사용자가 자신의 환경(Win11 빌드 26300)에서 .lnk 드래그 핀이 정상 동작함을 직접 확인** → 본 plan은 "진짜 작업 표시줄 + .lnk 수동/드래그 핀"을 채택.
- WebSearch(MSIX 단축키/활성화) → MSIX 풀트러스트 앱은 IShellLink COM으로 실제 경로에 .lnk 생성 가능. 업데이트에도 깨지지 않도록 단축키 타깃을 `AppExecutionAlias`로 지정 권장. 작업 표시줄 핀은 올바른 AppUserModelID(AUMID) 필요.
- WebSearch(MSIX 권한) → 패키지 앱이 `PackageManager`로 열거하려면 `packageQuery` 제한 capability 필요. WinUI3 데스크톱(Medium IL) 풀트러스트 동작·OOP COM은 `runFullTrust` 제한 capability 필요.

## Risks & Unknowns
| 위험 | 영향 | 완화책 |
|---|---|---|
| MSIX 샌드박스에서 "핀→클릭→인자 전달→팝업 위치" 끝단 연결이 기대대로 안 될 수 있음 | 핵심 기능 불능 | **T2 게이트로 최우선 검증(D15).** 실패 시 Halt 후 사용자와 대안 재논의 + plan 재작성 |
| 드래그 소스(우리 ListView)에서 작업 표시줄로 .lnk를 끌 때 셸이 핀으로 받아들이지 않을 수 있음 | "드래그 등록" UX 불능 | T2 C4에서 SetStorageItems(CF_HDROP) 드롭 핀 검증. 실패 시 폴백 = "그룹 폴더 열기 + 우클릭 핀 안내"(T10) |
| 클릭한 아이콘의 정확한 화면 좌표 획득 난이도 | 팝업이 엉뚱한 위치 | D4 좌표 규칙(표시 전 GetCursorPos 캡처 + rcWork 기준 변 배치 + 수용 기준 3개). 2차(개선): UIAutomation으로 자기 AUMID 버튼 Rect 조회 |
| `PackageManager`/`InstalledDesktopApp` 전체 사용자 열거 시 관리자 요구 | 권한 상승 팝업/실패 | 현재 사용자 범위로 한정(D6) |
| 멤버 앱 아이콘을 .lnk용 .ico로 변환하는 품질/투명도 손실 | 아이콘 흐림 | 다중 해상도(16/32/48/256) .ico 생성(D16), 256 PNG 압축 프레임 포함 |
| .NET 10 + 최신 Windows App SDK 조합의 템플릿/패키징 변동 | 빌드 셋업 시행착오 | T1a에서 최소 패키지 앱 빌드·실행 먼저 확정 후 진행 |

## Impact Analysis
신규 프로젝트로 **기존 코드 없음** → 변경 대상 심볼/호출자/직렬화/테스트가 존재하지 않는다. 본 plan은 전부 신규 생성이며, 영향 분석은 "신규 모듈 간 의존 방향"으로 대체한다.

### 4-A. 신규 모듈 의존 방향 (DDD 레이어)
| 모듈 | 의존 대상 | 비고 |
|---|---|---|
| `WorkGroup.Domain` | (없음) | 순수 모델/불변식. 외부 의존 0 |
| `WorkGroup.Application` | Domain | Infrastructure 인터페이스 정의(IAppInventory, IGroupRepository, IShortcutService, IIconService) |
| `WorkGroup.Infrastructure` | Application(인터페이스), Domain | WinRT/Win32 interop 구현 |
| `WorkGroup.App` (WinUI3, 패키지) | Application, Infrastructure, Domain | DI 조립, View/ViewModel, 활성화/런처 |
| `WorkGroup.*.Tests` | 각 대상 모듈 | xUnit |

### 4-B. 계약·직렬화 변경
- 신규 직렬화 형식: `groups.json`(스키마 버전 필드 `schemaVersion` 포함, D7). 기존 데이터 없음 → 마이그레이션 불필요(향후 대비 버전 필드만 둔다).
- 신규 외부 계약: .lnk 타깃 = `AppExecutionAlias` + 인자 `--group {GroupId}` (D2). 변경 시 기존 핀 .lnk와 호환 깨짐 → 인자 포맷을 D2에 고정.

### 4-C. 테스트 파일
- 신규: `WorkGroup.Domain.Tests`(모델 불변식), `WorkGroup.Application.Tests`(GroupAppService, 리포지토리 모킹).
- Infrastructure/UI는 OS·셸 의존이 커 단위 테스트 대신 **수동 검증 절차**(Verification Strategy)로 커버.

### Verified by
- 디렉터리 비어 있음 확인(Investigation Log) → 충돌 대상 심볼 0.
- 신규 라이브러리 가용성은 T1 빌드에서 실측 확정.

## Decisions

### D1. 작업 표시줄 등록 방식
- **Options**: A) 진짜 작업 표시줄 + 그룹별 .lnk 핀 / B) 자체 도킹 바(AppBar) / C) 플로팅 런처 바
- **Chosen**: A
- **Rationale**: 사용자가 .lnk 드래그 핀이 자신의 환경에서 정상 동작함을 직접 확인. 요구사항의 "진짜 작업 표시줄" 의도에 부합.
- **Source**: 사용자 확인.

### D2. .lnk 타깃·인자·활성화 방식 (외부 계약)
- **Options**: A) AppExecutionAlias + `--group {id}` 인자 / B) 커스텀 프로토콜 `workgroup://group/{id}` / C) 패키지 exe 직접 경로
- **Chosen**: **.lnk 타깃은 항상 A 고정** (`AppExecutionAlias` + `--group {id}`). B(프로토콜)는 .lnk 타깃이 **아니라**, 활성화 핸들러가 인자를 받는 **보조 수신 경로**로만 등록한다. 런타임 분기 없음 — .lnk는 단일 타깃(A)만 갖는다.
- **Rationale**: 업데이트로 설치 경로가 바뀌어도 alias는 불변 → 핀된 .lnk가 깨지지 않음. C는 패키지 경로 변동에 취약. B를 타깃으로 쓰면 핀 아이콘/AUMID 연동이 불확실하므로 타깃에서 배제하고, 외부(다른 앱/URL)에서의 그룹 열기 용도로만 프로토콜을 남긴다.
- **분기 제거**: "A 실패 시 B로 폴백" 같은 런타임 타깃 전환은 **하지 않는다**. T2 spike에서 A(alias)가 성립하지 않으면 그것은 Halt 사유이며(D2-B로 폴백 아님), plan 재작성으로 분기한다(B4 게이트 참조).
- **Source**: WebSearch(MSIX 단축키 AppExecutionAlias 권장).

### D3. 그룹별 작업 표시줄 버튼 분리
- **Options**: A) .lnk마다 고유 AppUserModelID(AUMID) 부여 / B) 단일 AUMID 공유
- **Chosen**: A
- **Rationale**: B는 모든 그룹이 하나의 작업 표시줄 버튼으로 합쳐져 그룹별 클릭 구분 불가. A는 IPropertyStore의 `System.AppUserModel.ID`로 .lnk별 고유 AUMID 지정 → 그룹마다 별도 버튼.
- **Source**: WebSearch(작업 표시줄 핀 AUMID).

### D4. 팝업 위치 계산
- **Options**: A) 활성화 시 커서 좌표 기준 / B) UIAutomation으로 자기 작업 표시줄 버튼 Rect 조회 / C) 화면 중앙 고정
- **Chosen**: A (1차), B는 후속 개선(본 plan 범위 밖)
- **좌표 캡처 시점·소스(고정)**:
  1. 활성화 핸들러 진입 **최초 1줄**에서, 어떤 창도 표시하기 전에 `GetCursorPos()`로 커서 좌표 `(cx, cy)`를 캡처한다(이후 커서 이동·팝업 표시의 영향 배제).
  2. `(cx, cy)`가 속한 모니터의 작업영역(`MonitorFromPoint` → `GetMonitorInfo`의 `rcWork`)을 구한다.
  3. 작업 표시줄 변(하/좌/우/상)은 모니터 전체 영역 `rcMonitor`와 `rcWork`의 차이로 판정한다.
  4. 팝업 배치:
     - 하단 작업 표시줄(기본): 팝업의 **수평 중심 = cx**, 팝업 **하단 = rcWork.bottom**(작업영역 위, 즉 작업 표시줄 바로 위). 화면 밖으로 나가면 `rcWork.left`/`rcWork.right` 안으로 클램프.
     - 좌/우/상도 같은 원리로 해당 변에 붙이고 cx 또는 cy를 기준으로 정렬.
- **수용 기준(측정 가능)**: 팝업이 (a) 항상 작업영역 안에 완전히 포함되고, (b) 작업 표시줄 변에 접하며(간격 ≤ 8px), (c) 클릭 좌표(cx 또는 cy)가 팝업의 해당 축 범위 안에 들어온다. T2/T11에서 이 3개를 수동 확인.
- **Rationale**: 클릭 직후 커서는 아이콘 위에 있으므로, 표시 전에 캡처한 커서 좌표를 기준으로 작업 표시줄 변에 붙이면 "클릭한 아이콘 위"를 측정 가능한 기준으로 만족. UIA(B)는 빌드별 트리 변동 위험이 있어 1차에서 제외.
- **Source**: Win32 GetCursorPos/MonitorFromPoint/GetMonitorInfo 표준 동작(검증은 T2/T11 수동 확인).

### D5. 설치 앱 열거 범위(소스)
- **Options**: A) Win32 + Store/UWP 모두 + 수동 exe 추가 / B) Win32만 / C) 자동만(수동 추가 없음)
- **Chosen**: A
- **Rationale**: 사용자 선택("Win32 + Store/UWP 모두"). 수동 exe 추가는 누락 앱 보완용으로 포함.
- **Source**: 사용자 확인.

### D6. 열거 사용자 범위
- **Options**: A) 현재 사용자 / B) 전체 사용자(관리자)
- **Chosen**: A
- **Rationale**: 전체 사용자 열거는 관리자 권한 요구 → UAC 상승·MSIX 제약. 현재 사용자 범위로 비관리자 동작.
- **Source**: WebSearch(PackageManager 전체 사용자 열거 시 관리자 필요).

### D7. 영속화 위치·형식
- **Options**: A) `ApplicationData.Current.LocalFolder`에 JSON / B) 레지스트리 / C) SQLite
- **Chosen**: A (`groups.json`, `System.Text.Json`, `schemaVersion` 필드 포함)
- **Rationale**: 데이터가 작고 구조 단순. MSIX LocalFolder는 샌드박스 안전 경로. .lnk·.ico 실제 파일은 별도 실경로(D8).
- **Source**: 프로젝트 규모/MSIX 권장.

### D8. .lnk·.ico 저장 경로(실파일 필요)
- **Options**: A) `%LOCALAPPDATA%\WorkGroup\Shortcuts`(.lnk), `...\Icons`(.ico) / B) LocalFolder 내부
- **Chosen**: A
- **Rationale**: 작업 표시줄 핀·드래그 대상은 셸이 접근하는 실제 파일이어야 함. 풀트러스트로 실경로 쓰기 가능. LocalFolder 내부 경로는 셸 핀과의 호환이 불확실.
- **Source**: WebSearch(MSIX 풀트러스트 .lnk 생성).

### D9. 그룹 아이콘 소스(요구사항 5)
- **Options**: A) 내장 세트 + 멤버 앱 아이콘 + 사용자 이미지/.ico 모두 / B) 사용자 이미지만 / C) 내장만
- **Chosen**: A
- **Rationale**: 사용자 선택("모두 지원").
- **Source**: 사용자 확인.

### D10. 팝업 레이아웃
- **Options**: A) 아이콘 그리드 / B) 아이콘 + 이름 리스트
- **Chosen**: A
- **Rationale**: 사용자 선택("아이콘 그리드"). 미첨부된 "1번 이미지" 대체 확정.
- **Source**: 사용자 확인(preview 선택).

### D11. 단일 인스턴스·활성화 처리
- **Options**: A) AppInstance 단일 인스턴스 + Redirect(인자 전달) / B) 매 클릭 새 프로세스
- **Chosen**: A
- **Rationale**: 백그라운드 상주 메인 앱이 활성화 인자(`--group {id}`)를 받아 팝업 표시. 새 프로세스 난립 방지·상태 일관.
- **Source**: Windows App SDK AppInstance 표준 패턴(검증은 T2).

### D12. 기술 스택·라이브러리
- **Options**: 신규 의존성 선정
- **Chosen** (사용자 승인 완료):
  - WinUI3 / Windows App SDK(최신 안정), .NET 10, **MSIX 패키지**
  - MVVM: `CommunityToolkit.Mvvm`
  - DI/호스팅: `Microsoft.Extensions.Hosting` + `Microsoft.Extensions.DependencyInjection`
  - Win32 interop: `Microsoft.Windows.CsWin32`(소스 생성기) — IShellLink/IPropertyStore/SHGetFileInfo/GetCursorPos 등
  - **창/트레이/백드롭 헬퍼: `WinUIEx`** — 창 위치·크기·지속성, 트레이 아이콘, always-on-top, HWND 헬퍼. T11 팝업 위치·T12 트레이에 활용.
  - 직렬화: `System.Text.Json`
  - 테스트: `xUnit`
- **Rationale**: 전역 CLAUDE.md / dotnet 스킬 컨벤션(CommunityToolkit.Mvvm, 한글 주석, 1500라인 제한, DDD)과 일치. CsWin32는 수동 P/Invoke 대비 안전·검증 용이. WinUIEx는 WinUI3 창 관리의 사실상 표준 커뮤니티 확장.
- **Source**: 전역 CLAUDE.md, dotnet-enterprise-dev 스킬, **사용자 승인(WinUIEx 포함 모두 추가)**.

### D13. 명명·레이어 위치
- **Options**: 네임스페이스/프로젝트 구조
- **Chosen**: `WorkGroup.Domain` / `WorkGroup.Application` / `WorkGroup.Infrastructure` / `WorkGroup.App`(WinUI3) / `WorkGroup.*.Tests`
- **Rationale**: DDD 레이어드(전역 CLAUDE.md 2단계 "DDD 준수"). 비즈니스 로직은 Domain/Application 중심, OS interop은 Infrastructure 격리.
- **Source**: 전역 CLAUDE.md.

### D14. 에러 처리 정책
- **Options**: A) 도메인/애플리케이션은 Result 패턴, 인프라 경계는 예외 캐치→Result 변환 / B) 전역 예외
- **Chosen**: A
- **Rationale**: UI까지 예외 전파 대신 사용자 메시지(InfoBar/Toast)로 변환. 셸/COM 실패가 잦은 인프라 경계에서 흡수.
- **Source**: dotnet 스킬 컨벤션.

### D15. T2 spike 게이트 (실패 시 분기) — B4 대응
- **Options**: A) spike를 통과 게이트로 두고 성공 시에만 T7+ 진입 / B) spike 결과와 무관하게 진행
- **Chosen**: A
- **규칙**:
  - T2는 **사용자 확인 게이트**다. T2의 검증 항목(아래 T2 체크리스트)이 **모두 통과**해야 T7·T10·T11에 진입한다.
  - T2 일부라도 실패 시 → **즉시 Halt.** implement-task는 추측으로 T7+를 진행하지 않는다. T3~T6(도메인·인벤토리·아이콘·영속화)는 T2와 독립이므로 진행 가능.
  - Halt 후 처리: 실패 항목을 Progress Log에 기록 → 사용자와 대안(프로토콜 전용 활성화 / 자체 AppBar / 폴더 열기+수동 핀 안내) 재논의 → **plan 재작성**으로 T7/T10/T11 재정의. 본 plan의 T7/T10/T11 가정(.lnk+AUMID+alias 활성화)은 T2 통과를 전제로만 유효하다.
- **Rationale**: T2가 핵심 가설 전체를 짊어지므로, 실패가 후속 task로 전파되지 않도록 게이트로 격리.
- **Source**: plan-reviewer B4.

### D16. .ico 인코딩 수단 — M4 대응
- **Options**: A) 자체 .ico 라이터(ICONDIR + 다중 해상도 PNG 프레임 직접 작성) / B) System.Drawing.Common / C) WIC(Windows.Graphics.Imaging BitmapEncoder)
- **Chosen**: A (PNG 프레임 인코딩은 WinAppSDK의 `Windows.Graphics.Imaging.BitmapEncoder`(PNG)로, .ico 컨테이너 헤더는 직접 작성)
- **Rationale**: B(System.Drawing.Common)는 WinUI3/MSIX 환경에서 권장되지 않고 .ico 다중 해상도 지원이 제한적. .ico 포맷은 ICONDIR/ICONDIRENTRY + 프레임 바이트로 단순·문서화가 잘 되어 있어 자체 라이터가 견고하고 외부 의존 추가 불필요. 256px 프레임은 PNG 압축 프레임으로 저장(.ico 표준).
- **분기 제거**: "불가 시 Open Question"을 제거. 인코딩 수단을 A로 확정하여 T5 자율 실행 중 멈춤 없음.
- **Source**: plan-reviewer M4, .ico 포맷 표준.

### D17. UI 디자인 가이드 — WinUI 3 Gallery / Fluent
- **Options**: A) WinUI 3 Gallery 디자인 가이드 적용(Fluent) / B) 임의 커스텀 스타일
- **Chosen**: A
- **적용 기준**:
  - 레이아웃·간격·타이포그래피·색상은 WinUI Gallery가 시연하는 Fluent 패턴을 따른다(표준 컨트롤 우선: `NavigationView`/`ListView`/`GridView`/`InfoBar`/`ContentDialog`).
  - 창 배경은 Mica/Acrylic 백드롭(WinUIEx 또는 SystemBackdrop) 적용. 라이트/다크 테마 자동 대응.
  - 팝업(T11)은 Acrylic 백드롭 + 둥근 모서리 등 Fluent 표면 스타일.
  - 표준 컨트롤로 충분하지 않은 경우에만 커스텀 스타일을 최소 도입.
- **Rationale**: 사용자 요청. Windows 11 기본 앱과 일관된 룩앤필 확보, 접근성·테마 대응을 표준 컨트롤로 자연 확보.
- **Source**: 사용자 확인.

## Tasks

> 총 13개(T1a/T1b 분할 포함)로 큼 → 승인 게이트에서 **Plan 분할(코어/인프라 T1a–T8 + UI/런처 T9–T12)** 여부를 함께 결정한다(Open Questions Q1).

> **모든 task 공통 완료 기준(CLAUDE.md 반영, 매 task acceptance에 암묵 포함)**:
> - 추가/수정 코드에 **한글 주석** 작성(m3). 빌드로 안 잡히므로 각 task 완료 시 self-review로 확인.
> - **파일 1500라인 내외 유지**(m2). 초과 우려 파일은 기능 단위로 분리: 특히 `InstalledAppInventory`(T4, 소스별 분리 가능), `IconService`(T5, 추출/인코딩 분리), ViewModel(T9, 이미 분리 설계), interop(NativeMethods는 CsWin32 생성이라 제외).
> - 파일 **UTF-8** 저장. 변경 시 해당 문서(`README.md`/`notes.md`) 갱신.

### Tasks — Plan 1 (코어·인프라): **현재 실행 대상**

- [x] **T1a. WinUI3 MSIX 패키지 앱 최소 빌드·실행 확정** (빌드 검증 완료 / GUI 창 표시는 수동 확인 사용자 대기)
  - **Type**: C
  - **Acceptance**: 단일 WinUI3 패키지 앱이 `dotnet build` 성공 + 배포 후 실행되어 빈 메인 창 표시(수동). MSIX 패키징·디버그 실행 절차가 확정되어 Verification Strategy에 기록됨.
  - **Files**:
    - 주: `WorkGroup.sln`, `src/WorkGroup.App/WorkGroup.App.csproj`, `src/WorkGroup.App/Package.appxmanifest`, `src/WorkGroup.App/App.xaml(.cs)`, `src/WorkGroup.App/MainWindow.xaml(.cs)`
  - **Edge Cases**: .NET10 SDK/WindowsAppSDK 버전 부재 → 빌드 실패 메시지로 즉시 드러남.
  - **Halt Forecast**: "WindowsAppSDK 버전 어떤 것?" → D12에서 최신 안정 채택, 본 task에서 실측 확정. "패키지 앱 디버그 실행 방법?" → 본 task에서 확정·문서화.
  - **Depends on**: -

- [x] **T1b. 레이어 프로젝트·테스트·문서 스캐폴딩**
  - **Type**: C
  - **Acceptance**: Domain/Application/Infrastructure 프로젝트 + 2개 테스트 프로젝트가 솔루션에 추가되고 `dotnet build`/`dotnet test`(빈 테스트) 성공. `README.md`/`notes.md`/`AGENTS.md` 생성(m1 기준 충족).
  - **Files**:
    - 주: `src/WorkGroup.Domain/*.csproj`, `src/WorkGroup.Application/*.csproj`, `src/WorkGroup.Infrastructure/*.csproj`, `tests/WorkGroup.Domain.Tests/*`, `tests/WorkGroup.Application.Tests/*`
    - 문서: `README.md`, `notes.md`, `AGENTS.md`
  - **AGENTS.md 내용(m1)**: 빌드/테스트 명령(`dotnet build`, `dotnet test`), 레이어 구조(D13), Plan Location(`plan.md` 루트), 코딩 규칙(한글 주석·파일 1500라인 제한·DDD·UTF-8) — 전역 CLAUDE.md 핵심을 프로젝트 컨텍스트로 미러링.
  - **Edge Cases**: 프로젝트 참조 순환 → 레이어 방향(D13) 준수로 방지.
  - **Halt Forecast**: 없음(표준 스캐폴딩).
  - **Depends on**: T1a

- [ ] **T2. (SPIKE/게이트) 핀→클릭→인자 전달→팝업 위치 + 드래그 핀 끝단 검증**
  - **Type**: D
  - **Acceptance(통과/실패 체크리스트, 모두 통과해야 게이트 통과 — D15)**:
    - [ ] (C1) 수동 생성한 .lnk(고유 AUMID + AppExecutionAlias + `--group test`)가 작업 표시줄에 핀됨
    - [ ] (C2) 핀 아이콘 클릭 시 앱이 `--group test` 인자를 수신(AppInstance 단일 인스턴스/Redirect 경유)
    - [ ] (C3) D4 좌표 규칙대로 캡처한 커서 좌표 기준으로 작업 표시줄 변에 접한 테스트 팝업 표시(D4 수용 기준 3개 충족)
    - [ ] (C4) **우리 앱 ListView를 드래그 소스로** `DataPackage.SetStorageItems`(.lnk StorageFile)로 작업 표시줄에 드롭 → 핀 수용됨 (M5: 이 수단의 성립 자체를 본 spike에서 검증)
    - 결과(각 항목 통과/실패 + 실패 원인)는 **Progress Log에 기록**(M1).
  - **Files**:
    - 주: `src/WorkGroup.App/Package.appxmanifest`(AppExecutionAlias, 프로토콜, runFullTrust/packageQuery), `src/WorkGroup.App/Activation/LaunchActivationHandler.cs`, `src/WorkGroup.Infrastructure/Interop/*`(GetCursorPos/MonitorFromPoint/GetMonitorInfo)
    - 동반: `src/WorkGroup.App/App.xaml.cs`(AppInstance 단일 인스턴스/Redirect)
  - **Edge Cases**: 인자 없음/형식 오류 → 메인 창 표시로 폴백. 멀티 모니터/작업 표시줄 위치(하/좌/우/상) → D4 작업영역 기준 좌표 보정.
  - **Halt Forecast**: C1~C4 중 하나라도 실패 → **D15 게이트 발동: 즉시 Halt, T7/T10/T11 진입 금지, 사용자와 대안 재논의 후 plan 재작성.** (런타임 프로토콜 폴백은 D2에서 배제됨)
  - **spike 코드 처리**: 통과 시 LaunchActivationHandler/Interop 코드는 본 코드로 승격(T11에서 확장), 폐기 아님.
  - **Depends on**: T1a

- [x] **T3. Domain 모델**
  - **Type**: C
  - **Acceptance**: `AppGroup`(이름/멤버/아이콘소스/Id), `AppEntry`(표시명/실행타깃/원본아이콘), 값객체(`GroupId`, `IconSource`) 생성·멤버 추가/제거/중복방지 불변식을 단위 테스트로 검증(녹색).
  - **Files**:
    - 주: `src/WorkGroup.Domain/Groups/AppGroup.cs`, `AppEntry.cs`, `GroupId.cs`, `IconSource.cs`
    - 테스트: `tests/WorkGroup.Domain.Tests/AppGroupTests.cs`
  - **Edge Cases**: 빈 그룹명→거부. 동일 앱 중복 추가→무시/거부. 멤버 0개 그룹 허용 여부→허용(빈 그룹 생성 후 채우기).
  - **Halt Forecast**: "GroupId 생성 규칙?" → GUID 문자열(결정 본 task에 고정). "멤버 최대 개수?" → 제한 없음(UI에서 스크롤).
  - **Depends on**: T1b

- [ ] **T4. 설치 앱 인벤토리(Win32 + Store/UWP + 수동 추가)**
  - **Type**: D
  - **Acceptance**: `IAppInventory.GetInstalledAppsAsync()`가 현재 사용자 기준 Win32(`InstalledDesktopApp`) + 패키지(`PackageManager`) 앱을 합쳐 표시명·실행타깃·아이콘 핸들/경로로 N개 반환(수동: 주요 앱이 목록에 보임). 수동 .exe 추가 경로 제공.
  - **Files**:
    - 주: `src/WorkGroup.Application/Inventory/IAppInventory.cs`, `src/WorkGroup.Infrastructure/Inventory/InstalledAppInventory.cs`
    - 동반: `src/WorkGroup.App/Package.appxmanifest`(packageQuery, runFullTrust)
  - **Edge Cases**: 권한 없음→해당 소스 건너뛰고 나머지 반환(부분 실패 허용, 로깅). 실행 타깃 경로 누락 앱→목록 제외 또는 비활성. 중복(같은 앱이 Win32+패키지 양쪽)→AUMID/경로 기준 dedup.
  - **Halt Forecast**: "전체 사용자 열거 관리자 필요?" → D6(현재 사용자). "패키지 앱 실행 타깃?" → AUMID 기반 실행(T11에서 Shell `shell:AppsFolder\{AUMID}` 또는 IApplicationActivationManager).
  - **Depends on**: T3

- [ ] **T5. 아이콘 추출·.ico 생성 서비스**
  - **Type**: C
  - **Acceptance**: `IIconService`가 (a) 앱 실행파일/패키지 로고에서 아이콘 추출, (b) 내장 세트 제공, (c) 사용자 이미지/.ico → 다중 해상도 .ico 변환을 수행하고 `%LOCALAPPDATA%\WorkGroup\Icons\{groupId}.ico` 생성(수동: 파일 생성·아이콘 정상 표시).
  - **Files**:
    - 주: `src/WorkGroup.Application/Icons/IIconService.cs`, `src/WorkGroup.Infrastructure/Icons/IconService.cs`
    - 동반: `src/WorkGroup.App/Assets/BuiltInIcons/*`
  - **Edge Cases**: 손상/미지원 이미지→기본 아이콘 폴백. 초대형 이미지→256px로 다운스케일. 투명도 보존.
  - **Halt Forecast**: "이미지→.ico 인코딩 수단?" → **D16에서 확정**(자체 .ico 라이터 + BitmapEncoder PNG 프레임). 미결 없음.
  - **Depends on**: T3

- [x] **T6. 영속화(JSON 리포지토리)**
  - **Type**: C
  - **Acceptance**: `IGroupRepository` Save/Load/Delete가 `LocalFolder\groups.json`에 그룹 컬렉션을 직렬화/역직렬화하고, 앱 재시작 후 복원됨(단위 테스트는 임시 폴더 주입으로 검증).
  - **Files**:
    - 주: `src/WorkGroup.Application/Persistence/IGroupRepository.cs`, `src/WorkGroup.Infrastructure/Persistence/JsonGroupRepository.cs`
    - 테스트: `tests/WorkGroup.Application.Tests/JsonGroupRepositoryTests.cs`
  - **Edge Cases**: 파일 없음→빈 컬렉션. JSON 손상→백업 후 빈 컬렉션으로 복구(로깅). 동시 쓰기→파일 락/원자적 쓰기(temp→rename). 디스크 풀→예외→Result 실패.
  - **Halt Forecast**: "스키마 버전 필드?" → D7(schemaVersion 포함). "경로 주입?" → 생성자 주입으로 테스트 가능화.
  - **Depends on**: T3

- [ ] **T7. 바로가기(.lnk) 생성 서비스**
  - **Type**: D
  - **Acceptance**: `IShortcutService.CreateOrUpdate(group)`가 IShellLink(CsWin32)로 `Shortcuts\{groupId}.lnk` 생성: 타깃=AppExecutionAlias, 인자=`--group {id}`, 아이콘=그룹 .ico, IPropertyStore로 고유 AUMID 설정. 탐색기에서 더블클릭 시 앱이 해당 group으로 기동(수동 확인). Delete 시 .lnk 제거.
  - **Files**:
    - 주: `src/WorkGroup.Application/Shortcuts/IShortcutService.cs`, `src/WorkGroup.Infrastructure/Shortcuts/ShortcutService.cs`
    - 동반: `src/WorkGroup.Infrastructure/Interop/NativeMethods.txt`(CsWin32 입력)
  - **Edge Cases**: .lnk 이미 존재→갱신(덮어쓰기). 아이콘 파일 없음→T5로 선생성 보장(오케스트레이션 T8). AUMID 충돌→groupId 기반 고유 보장.
  - **Halt Forecast**: "alias 실 경로 확인?" → T2 spike에서 검증된 alias 사용. "IPropertyStore COM 시그니처?" → CsWin32 생성.
  - **Depends on**: T2(C1/C2 통과 — alias 활성화), T5

- [ ] **T8. Application 서비스(그룹 관리 use case 오케스트레이션)**
  - **Type**: C
  - **Acceptance**: `GroupAppService`의 Create/Update/Delete가 도메인 검증→아이콘 생성(T5)→.lnk 생성/갱신(T7)→영속화(T6)를 순서대로 수행하고, 실패 시 아래 단일 일관성 정책대로 동작(단위 테스트는 인프라 인터페이스 모킹으로 각 실패 지점 검증).
  - **일관성 정책(M3, 단일안)**: 영속화 순서를 **(1)아이콘 → (2).lnk → (3)groups.json 저장**으로 고정하고, **groups.json 저장이 성공해야만 그룹이 "존재"로 간주**한다.
    - (1) 또는 (2) 실패 → json 저장 안 함 → 그룹은 미존재. 이미 만든 .ico/.lnk는 정리 시도. 정리마저 실패하면 그 파일은 **orphan**으로 남기되, groupId가 json에 없으므로 무해(고아 파일은 앱 시작 시 "json에 없는 Shortcuts/Icons 파일 청소" 루틴으로 제거 — 본 task에 포함).
    - (3) json 저장 실패 → (1)(2) 산출물 정리 시도 → 그룹 미존재.
    - 재시도는 하지 않는다(사용자가 다시 저장 시 멱등 수렴).
  - **Files**:
    - 주: `src/WorkGroup.Application/Groups/GroupAppService.cs`, `IGroupAppService.cs`
    - 테스트: `tests/WorkGroup.Application.Tests/GroupAppServiceTests.cs`
  - **Edge Cases**: 삭제 시 .lnk/.ico/json 모두 정리. 멱등성(같은 그룹 중복 Create→Update로 수렴). 시작 시 orphan 파일 청소.
  - **Halt Forecast**: "롤백/orphan 정책?" → 위 단일 일관성 정책으로 고정(분기 없음).
  - **Depends on**: T6, T7

---

## Tasks — Plan 2 (UI·런처): **Plan 1(T1a–T8) 완료 후 별도 실행**

> Q1 답변에 따라 분할. **implement-task는 우선 Plan 1만 실행**한다. Plan 1 완료(특히 T2 게이트 통과) 후, 이 Plan 2 task들을 활성 대상으로 승격해 별도 실행한다. T2가 게이트 실패 시 T10/T11은 plan 재작성 대상이다(D15).
> Plan 2 UI는 **D17(WinUI 3 Gallery / Fluent 디자인 가이드)** 를 전 task 공통으로 적용한다.

- [ ] **T9. 메인 화면 UI(앱 목록 + 그룹 빌더 + 아이콘 설정)**
  - **Type**: D
  - **Acceptance**: 메인 창에서 (1) 설치 앱 목록 표시·검색, (2) 앱 선택→그룹 구성/이름 지정, (3) 그룹 아이콘 3소스(내장/멤버앱/사용자이미지) 선택, (4) 저장 시 GroupAppService 호출 흐름 동작(수동). x:Bind 기반 MVVM. **D17 Fluent 적용**(NavigationView 기반 셸, Mica 백드롭, 표준 컨트롤, 라이트/다크 테마).
  - **Files**:
    - 주: `src/WorkGroup.App/Views/MainPage.xaml(.cs)`, `src/WorkGroup.App/ViewModels/MainViewModel.cs`, `GroupEditViewModel.cs`, `IconPickerViewModel.cs`
    - 동반: `src/WorkGroup.App/Views/IconPickerDialog.xaml(.cs)`, DI 등록(`App.xaml.cs`/`ServiceConfiguration.cs`)
  - **Edge Cases**: 앱 목록 로딩 중→진행 표시. 빈 검색 결과→빈 상태 안내. 그룹명 미입력→저장 비활성. 대량 앱 목록→가상화(ItemsRepeater/ListView 가상화).
  - **Halt Forecast**: "아이콘 미리보기 비동기 로딩?" → 가상화 + 비동기 썸네일. "검증 실패 UI?" → D14(InfoBar). "디자인 기준?" → D17. 파일 1500라인 초과 시 ViewModel 분리(이미 분리 설계).
  - **Depends on**: T4, T8

- [ ] **T10. 그룹 리스트 + 작업 표시줄 드래그 등록**
  - **Type**: D
  - **Acceptance**: 저장된 그룹이 목록으로 표시되고, 목록 항목을 OS로 드래그 시 해당 그룹의 .lnk를 CF_HDROP 셸 파일로 제공 → 작업 표시줄에 핀됨(수동, 대상 환경). 드래그 핀 불가 환경 폴백: "그룹 폴더 열기 + 안내" 버튼.
  - **Files**:
    - 주: `src/WorkGroup.App/Views/GroupListPage.xaml(.cs)`, `src/WorkGroup.App/ViewModels/GroupListViewModel.cs`, `src/WorkGroup.App/Interop/ShellDragSource.cs`
    - 동반: `src/WorkGroup.Infrastructure/Interop/NativeMethods.txt`
  - **Edge Cases**: 드래그 취소→무동작. .lnk 미생성 그룹→드래그 차단. 핀 실패(환경)→폴백 안내.
  - **Halt Forecast**: "WinUI 드래그로 OS 파일 드롭 제공 방법?" → T2(C4)에서 검증된 `DataPackage.SetStorageItems`(.lnk StorageFile) 경로. T2 게이트 통과 전제(D15).
  - **Depends on**: T2(C4 통과), T8

- [ ] **T11. 팝업 런처(클릭 시 그룹 그리드 팝업 + 앱 실행)**
  - **Type**: D
  - **Acceptance**: 핀된 그룹 아이콘 클릭 → 단일 인스턴스 앱이 `--group {id}` 수신 → D4 좌표 규칙대로 작업 표시줄 변 위에 아이콘 그리드 팝업(항상 위, 포커스 잃으면 자동 닫힘) 표시 → 항목 클릭 시 해당 앱 실행(Win32 경로 실행 / 패키지 AUMID 활성화)(수동 확인). **D17 Fluent 적용**(Acrylic 백드롭, 둥근 모서리). 창 위치·always-on-top·표시는 **WinUIEx** 헬퍼 활용.
  - **Files**:
    - 주: `src/WorkGroup.App/Views/GroupPopupWindow.xaml(.cs)`, `src/WorkGroup.App/ViewModels/GroupPopupViewModel.cs`, `src/WorkGroup.App/Activation/LaunchActivationHandler.cs`(T2 확장)
    - 동반: `src/WorkGroup.Infrastructure/Launch/IAppLauncher.cs` + `AppLauncher.cs`(IApplicationActivationManager / Process.Start)
  - **Edge Cases**: 그룹 삭제됨/멤버 0개→안내 팝업. 멀티 모니터/작업 표시줄 위치→D4 작업영역 보정. 앱 실행 실패(경로 없음)→토스트. 팝업 떠 있을 때 다른 그룹 클릭→기존 팝업 닫고 새로 표시.
  - **Halt Forecast**: "패키지 앱 실행 방법?" → IApplicationActivationManager.ActivateApplication(AUMID). "포커스 손실 닫힘?" → Window Deactivated 처리(WinUIEx). "정확 좌표?" → D4. "디자인?" → D17.
  - **Depends on**: T2(C1~C3 통과), T8

- [ ] **T12. 자동 시작 + 마무리(설정/트레이/문서)**
  - **Type**: C
  - **Acceptance**: 로그인 시 백그라운드 상주(StartupTask extension, 사용자 토글 가능), 트레이 아이콘에서 메인 창 열기/종료. `README.md`(개요·기능·실행·아키텍처) 및 `notes.md` 최종 갱신.
  - **Files**:
    - 주: `src/WorkGroup.App/Package.appxmanifest`(windows.startupTask), `src/WorkGroup.App/Services/StartupService.cs`, `src/WorkGroup.App/Tray/TrayIconService.cs`(WinUIEx 트레이)
    - 문서: `README.md`, `notes.md`
  - **Edge Cases**: 사용자가 자동 시작 비활성→다음 로그인부터 미기동. 트레이 미지원 환경→무시.
  - **Halt Forecast**: "StartupTask 동의 흐름?" → RequestEnableAsync 결과 처리. "트레이 구현?" → WinUIEx. 문서 누락 시 4단계 위반 → 본 task에서 확정.
  - **Depends on**: T9, T10, T11

## Known Workarounds
- 작업 표시줄 드래그 핀이 동작하지 않는 Windows 빌드: 우리 앱은 핀을 강제할 수 없으므로 "그룹 폴더 열기 + 우클릭 핀 안내"를 폴백으로 제공(T10). 근본적으로는 Microsoft 핀 API(Limited Access Feature) 채택이 필요하나 본 plan 범위 밖.

## Verification Strategy
- 빌드: **`dotnet build WorkGroup.slnx`** (플랫폼 미지정 — 솔루션 단위 빌드 확정. `-p:Platform=x64` 강제 시 slnx 솔루션 구성 매핑 오류 발생하므로 미지정 사용. 앱 단독 RID 빌드가 필요하면 `dotnet build src/WorkGroup.App/WorkGroup.App.csproj -p:Platform=x64`). **검증됨: T1a에서 0 warning / 0 error.**
- 단위 테스트: `dotnet test`(Domain/Application; Infrastructure·UI 제외)
- 패키지 앱 디버그 실행(수동, GUI — 자율 실행에서 관찰 불가, 사용자 확인 필요): Visual Studio F5(MSIX 배포) 또는 `dotnet build` 후 생성 패키지 등록. 절차는 README에 기록(T1b).

> **follow-up(T1a)**: `dotnet new sln`이 .NET 10 기본값인 `WorkGroup.slnx`(신형 XML 솔루션)를 생성 → plan/AGENTS의 `WorkGroup.sln` 표기를 `WorkGroup.slnx`로 통일. 템플릿은 `VijayAnand.WinUITemplates`(서드파티)이나 생성물은 표준 WinUI3 구성(Microsoft.WindowsAppSDK 1.*, Microsoft.Windows.SDK.BuildTools 10.*, Microsoft.Web.WebView2 1.* — 공식 템플릿 baseline과 동일). 기본 `WindowsPackageType=None`(비패키지 실행 가능)이며 `EnableMsixTooling=true`로 MSIX 패키징 가능 — MSIX 식별자/capability 설정은 T2에서 확정.
- 수동 검증(필수):
  - T2(게이트): C1~C4 체크리스트(핀/클릭 인자 수신/팝업 위치/드래그 핀) 모두 통과 확인
  - T4: 주요 설치 앱이 목록에 표시되는지 확인
  - T7: .lnk 더블클릭 기동 확인
  - T10: 리스트 드래그→작업 표시줄 핀 확인(대상 환경)
  - T11: 핀 아이콘 클릭→그리드 팝업→앱 실행 확인

## Progress Log
<!-- implement-task가 2 task마다 갱신 -->
- T1a–T1b 완료 (커밋 1e2b1be 외): WinUI3 앱 + 4 레이어/2 테스트 프로젝트 스캐폴딩, 문서(AGENTS/README/notes) 작성. 전체 빌드 0/0, 테스트 2/2 통과. 빌드 명령 `dotnet build WorkGroup.slnx` 확정(.slnx 채택). **미완(수동 사용자 대기): T1a GUI 창 표시 시각 확인.**
- T3 완료 (Domain): AppGroup/AppEntry/GroupId/IconSource + Result 패턴(D14). 불변식 단위 테스트 11/11 통과, 빌드 0/0(CS0109 경고 수정). spec-compliance OK. Domain 외부 의존 0 확인.
- T6 완료 (영속화): IGroupRepository(Application) + JsonGroupRepository(Infrastructure, 경로 주입형·원자적 쓰기·손상 백업·schemaVersion). 테스트 8/8, 빌드 0/0, spec-compliance OK. **부수 변경(필수)**: ① Infrastructure에 Microsoft.Extensions.Logging.Abstractions 추가 ② Application.Tests TFM→net10.0-windows + Infrastructure 참조(net10.0 테스트가 windows 프로젝트 참조 불가 해소) ③ App.xaml.cs 베이스를 `Microsoft.UI.Xaml.Application`로 정규화(WorkGroup.Application 네임스페이스 충돌 CS0118 해소).
  - follow-up: 향후 UI 코드에서 unqualified `Application` 사용 시 형제 네임스페이스 충돌 재발 가능 → 정규화 또는 alias 주의(T9~). T8에서 JsonGroupRepository에 `ApplicationData.Current.LocalFolder.Path` 주입 연결 필요.

## Next Steps
<!-- 체크포인트/세션 종료 시 갱신 -->
- **현재 상태(2026-06-01 체크포인트 2)**: T1a·T1b·T3·T6 완료(커밋됨, 최신 389c99f). 전체 빌드 0/0, 테스트 19/19 통과. 다음 자율 작업 = **T5(아이콘 서비스)** → T4(인벤토리). T5는 CsWin32(아이콘 추출) 신규 도입 + WinRT BitmapEncoder(.ico) 포함.
- **사용자 확인 필요(2건)**:
  1. T1a GUI: Visual Studio에서 `WorkGroup.App` F5로 빈 창이 정상 표시되는지 시각 확인.
  2. T2 게이트(C1~C4)는 작업 표시줄 핀/클릭/팝업/드래그핀의 **수동 검증**이라 자율 실행에서 통과 불가 → T2 spike 코드 구현 후 사용자 수동 검증 필요(D15).
- **다음 자율 작업(T2 게이트와 독립, D15)**: T6(영속화)·T5(아이콘)·T4(인벤토리 코드)는 단위 테스트/빌드로 자율 검증 가능 → 이어서 진행 가능. T7·T8은 T2 게이트 통과 후.
- 재개: "T5부터 계속" 또는 "T2 spike 코드부터" 등으로 지정.
- Suggested skills: pjc:implement-task / 공식 /code-review

## Open Questions (모두 해결됨)
- [x] Q1: task 분할 → **B) 2개 plan으로 분할(Plan 1=T1a~T8, Plan 2=T9~T12)** 로 확정(사용자).
- [x] Q2: 신규 의존성 → **모두 승인 + WinUIEx 추가 + WinUI 3 Gallery 디자인 가이드(D17) 적용** 으로 확정(사용자). D12/D17 반영.
