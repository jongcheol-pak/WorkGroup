# plan — WorkGroup.App ViewModel 테스트 프로젝트 신설

> 요구: `intent/2026-09-08-app-viewmodel-tests.md`

## Goal

`tests/WorkGroup.App.Tests`를 신설해 `WorkGroup.App`을 참조하고, 두 목록 ViewModel(`WorkGroupsViewModel`·`FolderShortcutsViewModel`)의 검색 필터→`CanReorder`, 검색 중 `MoveAsync` 무동작, 재정렬 실패→`StatusMessage`, `LoadAsync`의 상태 초기화를 `dotnet test WorkGroup.slnx` 한 번으로 측정한다. 차단 요인이던 `WorkGroup.App`의 `DeploymentManager` 자동 초기화자는 패키지 ID 가드가 붙은 명시 호출로 바꿔 앱 실행 동작을 보존한다.

| # | 기준 | 측정 방법 |
|---|---|---|
| G1 | `WorkGroup.App`을 참조하는 테스트 프로젝트가 `tests/`에 있다 | `grep -rl "WorkGroup.App.csproj" tests/ --include=*.csproj` → 작성 시점 0건 → 1건 |
| G2 | 솔루션 테스트가 전부 통과하고 건수가 늘었다 | `dotnet test WorkGroup.slnx` → 실패 0 · 총 **작성 시점 173건 → 193건 이상** |
| G3 | 두 ViewModel이 실제로 실행돼 측정된다 | `dotnet test WorkGroup.slnx --filter "FullyQualifiedName~WorkGroup.App.Tests"` → 실패 0 · 20건 이상(T1 3 + T3 10 + T4 7) |
| G4 | 앱 빌드가 깨지지 않는다(패키징 포함) | `dotnet build src/WorkGroup.App/WorkGroup.App.csproj -p:Platform=x64` → 경고 0 / 오류 0 |
| G5 | 자동 초기화자를 끈 자리에 대체 호출이 실재한다 | `grep -c "DeploymentManager.Initialize" src/WorkGroup.App/*.cs` → 0건 → 1건 |

## Out of Scope

- `ReorderDrop` — 살아있는 `ListView`·`DragUI`·XamlRoot가 필요해 어느 구성에서도 단위 테스트가 불가하다. 순수 계산부 `ListInsertionPoint`는 `tests/WorkGroup.Application.Tests/ListInsertionPointTests.cs`가 이미 덮는다.
- `GroupEditViewModel`·`SettingsViewModel`·`AboutViewModel` — 인터뷰에서 "두 목록 VM만"으로 확정. 대역이 커져 이번 회차가 테스트 작성보다 fake 작성에 기울어진다.
- 두 페이지 코드비하인드(`WorkGroupsPage`·`TrayMenuPage`)의 드래그 핸들러 — `Page` 생성에 XamlRoot가 필요하다.
- 직전 회차의 GUI 수동 검증 6항목 — 여전히 사용자 F5 확인 대상이며 이 회차가 대신하지 않는다.

## Decisions

| # | 결정 | 근거 |
|---|---|---|
| D1 | 테스트 프로젝트는 `tests/WorkGroup.App.Tests`, TFM `net10.0-windows10.0.19041.0`, `Platforms`는 **x64만** | `WorkGroup.App`이 `OutputType=WinExe`+MSIX라 AnyCPU 빌드에서 패키징이 실패한다(Investigation Log 1행). 기존 두 테스트 프로젝트의 `AnyCPU;x64`를 따라 하면 같은 실패를 부른다 |
| D2 | 테스트 프로젝트에 `WindowsPackageType=None` + `WindowsAppSdkBootstrapInitialize=true`를 둔다 | `LocalizationService`의 `new ResourceManager()`(MRT Core)가 WinRT 활성화를 요구한다. 이 두 속성이면 WinAppSDK targets가 **버전이 박힌 부트스트랩 모듈 초기화자를 생성**해 준다 — 손으로 `Bootstrap.Initialize(0x000200NN)`을 적으면 패키지 버전 상향마다 낡는다 |
| D3 | `WorkGroup.App.csproj`에 `WindowsAppSdkDeploymentManagerInitialize=false`를 넣고, App에 **패키지 ID 가드가 붙은 `[ModuleInitializer]`**를 직접 둬 `DeploymentManager.Initialize()`를 호출한다 | 자동 초기화자는 `WindowsPackageType==MSIX && OutputType==WinExe`일 때 무조건 붙고(Investigation Log 5행), 패키지 ID 없는 프로세스에서 예외를 던져 App.dll의 **타입 로드 자체**를 막는다. 모듈 초기화자로 옮기면 실행 시점이 지금과 같고, 가드가 테스트 호스트만 건너뛴다. Source: `src/WorkGroup.App/App.xaml.cs:50-58`(현재 App 생성자 바디에 관련 호출 없음) |
| D4 | 패키지 ID 판정은 `kernel32!GetCurrentPackageFullName`의 반환값(`APPMODEL_ERROR_NO_PACKAGE`=15700)으로 한다 | `Package.Current`는 비패키지에서 **예외**로 답해 정상 경로에 예외 비용·디버거 정지를 만든다. P/Invoke는 반환값 비교라 새 의존성도 없다(App은 CsWin32를 참조하지 않는다) |
| D5 | ViewModel 생성자는 그대로 둔다(`LocalizationService` 구상 타입 유지) | 실측에서 부트스트랩만으로 `LocalizationService`가 생성되고 `Get`이 키를 폴백 반환한다(Investigation Log 8행). 이번 검증 대상인 `StatusMessage`는 리포지토리가 넣은 문자열이라 현지화와 무관하다 — 생성자를 `ILocalizer`로 바꾸는 것은 호출부가 걸리는 공개 API 변경이고 이번 목적에 불필요하다 |
| D6 | 미커밋 패키지 버전 상향은 **T0에서 단독 커밋**한다 | 이후 `WorkGroup.App.csproj` diff가 D3의 한 줄만 남아 되돌리기 쉬워진다. 인터뷰 확정 |
| D7 | `WorkGroup.slnx`의 새 항목은 App과 같은 `Debug\|Any CPU → x64` 매핑을 함께 갖는다 | 기존 테스트 프로젝트는 `*\|x64` 매핑만 있어 기본 솔루션 빌드(`Debug\|Any CPU`)에서 AnyCPU로 떨어진다(Investigation Log 10행 — 출력이 `bin/Debug/`). D1의 프로젝트가 AnyCPU로 떨어지면 App 참조가 패키징 오류를 낸다 |

## Investigation Log

| 주장 | 실행한 명령 | 출력 요지 |
|---|---|---|
| 테스트 프로젝트가 App을 참조하면 AnyCPU 빌드에서 MSIX 패키징이 실패한다 | `dotnet build Probe.csproj`(스크래치 프로브, App ProjectReference) | `error : Packaged .NET applications with an app host exe cannot be ProcessorArchitecture neutral. Please specify a RuntimeIdentifier or a Platform other than AnyCPU` |
| `-p:Platform=x64`면 App 참조·컴파일은 성공한다 | `dotnet build Probe.csproj -p:Platform=x64` | `빌드했습니다. 경고 0개 오류 0개` — `WorkGroup.App.dll` 산출 |
| **App.dll의 모듈 초기화자가 테스트 호스트를 죽인다**(핵심 차단 요인) | `dotnet test Probe.csproj -p:Platform=x64` | `TypeInitializationException : The type initializer for '<Module>'` ← `DeploymentManagerCS.AutoInitialize.AccessWindowsAppSDK()` → `COMException REGDB_E_CLASSNOTREG` |
| 테스트 호스트에서 WinAppSDK를 부트스트랩해도 막힌다 — 패키지 ID가 없어서 | 위 + 손으로 쓴 `[ModuleInitializer]`에서 `Bootstrap.Initialize(0x00020004)` | 오류가 `REGDB_E_CLASSNOTREG` → `InvalidOperationException : 프로세스에 패키지 ID가 없습니다`로 바뀜(부트스트랩 자체는 성공) |
| 원인은 WinAppSDK targets의 기본값이다 | `grep -rn "DeploymentManagerInitialize" ~/.nuget/packages/microsoft.windowsappsdk.foundation/2.3.9/buildTransitive/` | `DeploymentManagerCommon.targets:5` — 기본 `true` 조건이 `'$(WindowsPackageType)'=='MSIX' and ('$(OutputType)'=='Exe' or 'Winexe')` → `WorkGroup.App`이 정확히 해당 |
| **끄면 전부 통과한다** | `dotnet test Probe.csproj -p:Platform=x64 -p:WindowsAppSdkDeploymentManagerInitialize=false` | `통과! 실패 0, 통과 2` — VM 생성 + `LocalizationService` 생성·조회 |
| 테스트 쪽 부트스트랩은 필수다 | 위에서 부트스트랩 파일만 제거 후 재실행 | `REGDB_E_CLASSNOTREG at Microsoft.Windows.ApplicationModel.Resources.ResourceManager..ctor() at WorkGroup.App.Services.LocalizationService..ctor()` — VM 타입 로드는 되고 MRT만 실패 |
| `WindowsPackageType=None` + `WindowsAppSdkBootstrapInitialize=true`면 부트스트랩이 자동 생성된다(버전 하드코딩 불필요) | 손으로 쓴 부트스트랩 제거 + 두 속성 추가 후 `dotnet test ... -p:WindowsAppSdkDeploymentManagerInitialize=false` | `통과! 실패 0, 통과 2` |
| **실제 시나리오가 헤드리스에서 살아남는다** — `LoadAsync`의 아이콘 fire-and-forget 포함 | 프로브에 `LoadAsync`→검색→`MoveAsync`(검색 중/해제 후)→실패 메시지 검증 2건 추가 후 동일 명령 | `통과! 실패 0, 통과 4` — `Groups` 순서 `가,나,다`→`나,다,가` · 검색 중 `ReorderAsync` 미호출 · `StatusMessage`에 `"저장 실패했습니다."` |
| 기본 솔루션 빌드는 테스트 프로젝트를 AnyCPU로 떨어뜨린다 | `dotnet test WorkGroup.slnx` | 출력이 `tests/WorkGroup.Domain.Tests/bin/Debug/net10.0/…`(`bin/x64/` 아님) — slnx의 `*\|x64` 매핑은 솔루션 플랫폼이 x64일 때만 적용 |
| 현재 테스트 기준선 | `dotnet test WorkGroup.slnx` | `통과! 실패 0, 통과 23` (Domain) + `통과! 실패 0, 통과 150` (Application) = **173건** |
| 두 테스트 프로젝트 모두 App을 참조하지 않는다 | `grep -rl "WorkGroup.App" tests/ --include=*.csproj` · `grep -rn "WorkGroup.App" tests/*/[A-Z]*.cs` | 양쪽 0건 |
| App 진입점에 `DeploymentManager`·`Bootstrap` 호출이 없다 | `Grep "DeploymentManager\|Bootstrap"` (`src/`, `*.cs`) | `No matches found` — 현재는 자동 초기화자에만 의존 |
| `IGroupAppService`·`IFolderShortcutRepository`·`ILocalizer`는 모두 인터페이스라 대역 작성이 가능하다 | `cat src/WorkGroup.Application/Groups/IGroupAppService.cs` 외 2개 | 각각 6·6·2 멤버 · 모두 `Task`/`Task<Result…>` 반환 |
| `Result`의 팩토리는 `Ok`/`Fail`이다(`Success` 아님) | `cat src/WorkGroup.Domain/Common/Result.cs` | `public static Result Ok()` · `public static Result Fail(string error)` |
| 이 레포에는 Deferred 대장(`docs/plans/deferred.md`)이 없다 | `ls docs` | `docs/ 없음` — 확인했더니 없음 |
| `intent/` 폴더는 있고 직전 회차 파일 1개가 있으며 `Open questions`가 "없음"이다 | `ls intent` · `cat intent/2026-09-08-reorder-drag-visual.md` | `2026-09-08-reorder-drag-visual.md` · `## Open questions` → `없음` — 이월할 것 없음 |
| `AGENTS.md`에 `## 위키` 절이 없다 | `grep -n "위키" AGENTS.md` | 0건 — 이 레포는 위키 허브를 지목하지 않는다. 코드를 1차 출처로 삼았다 |
| 워킹트리 미커밋 변경은 패키지 버전 상향 하나다 | `git status --short` · `git diff -- src/WorkGroup.App/WorkGroup.App.csproj` | `M src/WorkGroup.App/WorkGroup.App.csproj` — WindowsAppSDK 2.2.0→2.4.0 · Extensions 10.0.9→10.0.11 · WinUIEx 2.9.1→2.9.3. 위 실측은 전부 이 상태에서 수행했다 |

## 작업 단계

### T0. 미커밋 패키지 버전 상향을 단독 커밋으로 분리

- [x] **T0-1** `dotnet build WorkGroup.slnx`·`dotnet test WorkGroup.slnx`로 현재 상태가 통과함을 확인한다(기준선 재확인)
- [x] **T0-2** `src/WorkGroup.App/WorkGroup.App.csproj`만 스테이징해 `설정: WinAppSDK 2.4.0 등 패키지 버전 상향`으로 커밋한다
- **Files**: `src/WorkGroup.App/WorkGroup.App.csproj`
- **Acceptance**: `git status --short`에서 `M src/WorkGroup.App/WorkGroup.App.csproj` 1줄 → 0줄. (**실행 중 정정**: 이 레포는 `plan.md`를 **추적**하므로 — `.gitignore`에 없다 — 회차 중 `git status`가 완전히 비지 않는다. 기준을 「빈 출력」이 아니라 「App.csproj 행이 사라졌는지」로 좁힌다. 목표를 낮춘 것이 아니라 이 task가 재는 대상을 정확히 지목한 것이다.) **케이스를 만들지 않는 사유**(면제 5범주 어디에도 해당하지 않아 번호를 쓰지 않는다): 이 task는 코드를 한 글자도 바꾸지 않고 **워킹트리에 이미 있는 변경을 그대로 커밋만** 한다. 그 상태에서 173건이 통과함이 이미 측정됐다(Investigation Log 마지막 행 · T0-1이 착수 시점에 재확인한다)
- **검증**: `git status --short` → `M src/WorkGroup.App/WorkGroup.App.csproj` 없음 · `git log --oneline -1` → 새 커밋

### T1. App의 `DeploymentManager` 자동 초기화자를 가드 붙은 명시 호출로 대체

> **의존**: T1-5(케이스)는 T2가 프로젝트를 만든 뒤에 작성한다 — 실행 순서는 T1-1~T1-4 → T2 → T1-5 → T1-6~T1-7 → T3 → T4 → T5.

- [x] **T1-1** `src/WorkGroup.App/WorkGroup.App.csproj`의 `PropertyGroup`에 `<WindowsAppSdkDeploymentManagerInitialize>false</WindowsAppSdkDeploymentManagerInitialize>`를 추가하고, **왜 끄는지**(테스트 호스트에서 App.dll 타입 로드를 막는다 + 대체 호출 위치)를 주석으로 남긴다
- [x] **T1-2** `src/WorkGroup.App/WindowsAppRuntimeInitializer.cs`를 신설한다 — `[ModuleInitializer]` 하나가 `GetCurrentPackageFullName`을 호출해 그 반환값을 **판정 함수에 넘기고**, 참이면 `DeploymentManager.Initialize()`를 호출한다
- [x] **T1-3** 판정을 순수 함수로 분리한다 — `internal static bool ShouldInitialize(int hresult)`: `APPMODEL_ERROR_NO_PACKAGE`(15700)면 false, 그 외(0=`ERROR_SUCCESS` · 122=`ERROR_INSUFFICIENT_BUFFER` 등 이름이 돌아온 경우)면 true. 네이티브 호출과 판정을 갈라 두어야 판정에 러너가 닿는다
- [x] **T1-4** `src/WorkGroup.App/WorkGroup.App.csproj`에 `<InternalsVisibleTo Include="WorkGroup.App.Tests" />`를 추가한다 — `ShouldInitialize`와 기존 `internal` 타입(`ReorderDrop` 등)을 테스트에서 볼 수 있게 한다
- [x] **T1-5** `tests/WorkGroup.App.Tests/WindowsAppRuntimeInitializerTests.cs`에 판정 케이스를 둔다 — ① 15700이면 false ② 0이면 true ③ 122(버퍼 부족 = 패키지 있음)면 true. **T2 완료 후에 작성한다**(그 프로젝트가 있어야 한다)
- [x] **T1-6** `dotnet build src/WorkGroup.App/WorkGroup.App.csproj -p:Platform=x64`로 패키징 포함 빌드가 통과하는지 확인한다
- [ ] **T1-7** `HUMAN-VERIFY` — 헤드리스에서 패키지 실행을 관찰할 수 없어 **미검증**. 사용자 F5 MSIX 실행으로 확인할 항목: ① 앱이 정상 시작한다(시작 즉시 크래시 없음) ② 트레이 아이콘·그룹 팝업·언어 리소스가 지금과 같다
- **Files**: `src/WorkGroup.App/WorkGroup.App.csproj` · `src/WorkGroup.App/WindowsAppRuntimeInitializer.cs`(신규) · 재는 자리: `tests/WorkGroup.App.Tests/WindowsAppRuntimeInitializerTests.cs`(신규 — T2가 프로젝트를 만든 뒤 T1-5가 채운다)
- **구조**: ⓐ **왜 새 파일인가** — `App.xaml.cs`(331줄)는 활성화 분기·single-instance·트레이 상주를 담은 진입점이고, 이 코드는 그보다 이른 **모듈 로드 시점**에 돌아야 해 성격이 다르다. 여기 섞으면 "App 생성자보다 먼저 도는 코드"가 App 생성자 파일 안에 숨는다. ⓑ **레이어** — App(조립/부트). WinAppSDK 런타임 초기화는 실행 형태(패키지/비패키지)에 대한 것이라 Infrastructure로 내릴 수 없다. ⓒ **재사용** — P/Invoke 선언은 이 파일 안 `private static extern` 하나로 끝난다(CsWin32를 App에 새로 들이지 않는다 — D4)
- **Acceptance**: `grep -c "WindowsAppSdkDeploymentManagerInitialize" src/WorkGroup.App/WorkGroup.App.csproj` → 작성 시점 0건 → 1건 · `grep -c "DeploymentManager.Initialize" src/WorkGroup.App/WindowsAppRuntimeInitializer.cs` → 1건 · `dotnet test WorkGroup.slnx --filter "FullyQualifiedName~WindowsAppRuntimeInitializerTests"` → 작성 시점 0건 → **3건, 실패 0**. **변이 실증**: `ShouldInitialize`의 `hresult != 15700`을 `true`로 고정하면 ① 케이스가 실패해야 한다(구현 중 1회 확인해 Progress Log에 적는다). **네이티브 호출 자체와 `DeploymentManager.Initialize()`의 실제 효과는 `[면제 ④]`** — 테스트 호스트에는 패키지 ID가 원리상 없어 true 분기를 실행할 수 없다(Investigation Log 4행이 그 벽이다). 그 분기는 T1-7의 F5 실행이 받는다
- **검증**: `dotnet build src/WorkGroup.App/WorkGroup.App.csproj -p:Platform=x64` → 경고 0 / 오류 0 · `dotnet build WorkGroup.slnx` → 경고 0 / 오류 0 · `dotnet test WorkGroup.slnx --filter "FullyQualifiedName~WindowsAppRuntimeInitializerTests"` → 3건, 실패 0

### T2. `tests/WorkGroup.App.Tests` 프로젝트 신설과 솔루션 등재

- [x] **T2-1** `tests/WorkGroup.App.Tests/WorkGroup.App.Tests.csproj`를 만든다 — TFM `net10.0-windows10.0.19041.0` · `TargetPlatformMinVersion` 10.0.17763.0 · `Platforms`=`x64` · `WindowsPackageType=None` · `WindowsAppSdkBootstrapInitialize=true` · `EnableMsixTooling=false` · `IsPackable=false`. 패키지는 기존 두 테스트 프로젝트와 같은 버전(`Microsoft.NET.Test.Sdk` 17.14.1 · `xunit` 2.9.3 · `xunit.runner.visualstudio` 3.1.4 · `coverlet.collector` 6.0.4) · `<Using Include="Xunit" />` · `ProjectReference`는 `WorkGroup.App` 하나(Application·Domain은 전이로 들어온다)
- [x] **T2-2** 각 속성이 왜 필요한지 csproj에 주석으로 남긴다 — `Platforms=x64`(App이 WinExe+MSIX라 AnyCPU 불가) · `WindowsPackageType=None`+`BootstrapInitialize`(MRT `ResourceManager` 활성화)
- [x] **T2-3** `WorkGroup.slnx`의 `/tests/` 폴더에 항목을 추가한다 — `<Platform Solution="*|x64" Project="x64" />`와 `<Platform Solution="Debug|Any CPU" Project="x64" />` 둘 다(D7)
- [x] **T2-4** `tests/WorkGroup.App.Tests/Fakes/FakeGroupAppService.cs`·`Fakes/FakeFolderShortcutRepository.cs`를 만든다 — 각각 반환할 목록·`ReorderAsync`의 `Result`·**마지막으로 전달된 순서**를 노출한다(순서가 저장까지 갔는지 재려면 인자를 붙잡아야 한다)
- [x] **T2-5** `dotnet test WorkGroup.slnx`가 새 프로젝트를 집어 실행하는지 확인한다. AnyCPU로 떨어져 패키징 오류가 나면 `ProjectReference`에 `SetPlatform="Platform=x64"` 메타데이터를 붙여 고정한다(대비책)
- **Files**: `tests/WorkGroup.App.Tests/WorkGroup.App.Tests.csproj`(신규) · `tests/WorkGroup.App.Tests/Fakes/FakeGroupAppService.cs`(신규) · `tests/WorkGroup.App.Tests/Fakes/FakeFolderShortcutRepository.cs`(신규) · `WorkGroup.slnx` · `AGENTS.md`(테스트 프로젝트 목록 — T5에서 갱신)
- **구조**: ⓐ **왜 새 프로젝트인가** — 기존 두 테스트 프로젝트는 `net10.0`/`net10.0-windows`이면서 `Platforms=AnyCPU;x64`이고 App을 참조하지 않는다. 여기에 App 참조를 얹으면 그 프로젝트들이 x64 전용이 되고 부트스트랩 모듈 초기화자가 Domain·Application 테스트에도 붙는다 — 지금 10초에 도는 150건에 WinAppSDK 로딩을 지운다. ⓑ **레이어** — `tests/`, App 레이어 대응. 이름은 `src/WorkGroup.App` ↔ `tests/WorkGroup.App.Tests`로 기존 두 쌍의 규칙을 그대로 따른다. ⓒ **재사용** — 패키지 버전·`<Using Include="Xunit" />`·`IsPackable=false`는 `tests/WorkGroup.Application.Tests`의 것을 그대로 쓴다(새로 고르지 않는다)
- **Acceptance**: `grep -rl "WorkGroup.App.csproj" tests/ --include=*.csproj` → 작성 시점 0건 → 1건 · `grep -c "WorkGroup.App.Tests" WorkGroup.slnx` → 0건 → 1건 · `dotnet test WorkGroup.slnx` → 실패 0, 총 173건(이 단계에서는 케이스가 아직 없어 건수 불변). **케이스**: 이 task 자체의 동작은 T3·T4의 케이스가 실행되는 것으로 증명된다 — 프로젝트가 솔루션에 안 붙으면 T3·T4가 0건으로 잡히고 G3이 거짓이 된다
- **검증**: `dotnet build WorkGroup.slnx` → 경고 0 / 오류 0 · `dotnet test WorkGroup.slnx` → 실패 0

### T3. `WorkGroupsViewModel` 케이스 작성

- [ ] **T3-1** `tests/WorkGroup.App.Tests/WorkGroupsViewModelTests.cs`를 만든다. 공통 준비: `AppGroup.Restore(GroupId.New(), name, IconSource.DefaultBuiltIn, apps)`로 그룹을 만들고 `FakeGroupAppService`에 넣는다
- [ ] **T3-2** 검색 필터 축 — ① 검색 없음이면 전체가 보이고 모든 항목의 `CanReorder`가 true ② 이름 부분일치로 좁혀지고 **남은 항목의 `CanReorder`가 false** ③ **멤버 앱 이름으로도 걸린다** ④ 검색어를 비우면 `CanReorder`가 다시 true ⑤ 공백만 입력한 것은 검색 아님(`Trim()` 후 판정) ⑥ 대소문자를 무시한다
- [ ] **T3-3** `MoveAsync` 축 — ⑦ 검색 중이면 순서가 그대로이고 `ReorderAsync`가 **호출되지 않는다** ⑧ `fromIndex==toIndex`면 무동작 ⑨ 범위 밖 인덱스(-1 · `Count`)는 무동작 ⑩ 정상 이동이면 `Groups` 순서가 바뀌고 `ReorderAsync`에 **새 순서 그대로** 전달된다
- [ ] **T3-4** 상태 메시지 축 — ⑪ `ReorderAsync` 실패 시 `StatusMessage`가 그 `Error` 문자열이 되고 `HasStatus`가 true ⑫ 성공 시 `StatusMessage`가 빈 문자열 ⑬ 실패 후 `LoadAsync`가 `StatusMessage`를 비운다 ⑭ 실패해도 목록은 되돌리지 않는다(주석이 선언한 동작)
- [ ] **T3-5** `IsEmpty` 축 — ⑮ 전체 0건일 때만 true이고, 검색으로 표시 0건이 된 경우는 false
- **Files**: `tests/WorkGroup.App.Tests/WorkGroupsViewModelTests.cs`(신규)
- **구조**: ⓐ **왜 새 파일인가** — 기존 테스트는 `tests/WorkGroup.Application.Tests/`에 `<대상>Tests.cs` 1파일 = 1대상 규칙으로 놓여 있다(`GroupAppServiceTests.cs` 등 15개). 같은 규칙을 따른다. ⓑ **레이어** — `tests/WorkGroup.App.Tests`. ⓒ **재사용** — 대역은 T2-4의 `Fakes/`를 쓰고 파일 안에서 새로 만들지 않는다
- **Acceptance**: `dotnet test WorkGroup.slnx --filter "FullyQualifiedName~WorkGroupsViewModelTests"` → 작성 시점 0건 → **10건 이상, 실패 0**. **케이스는 구현과 다른 축으로 세운다** — `ApplyFilter`의 조건식을 그대로 옮기지 않고 *관측 가능한 결과*(`Groups`의 이름 나열 · `CanReorder` 값 · fake가 붙잡은 순서 인자)로만 단언한다. **변이 실증**: `WorkGroupsViewModel.MoveAsync`의 `if (IsFiltered ...) return;`을 지우면 ⑦이 실패해야 한다(구현 중 1회 확인해 Progress Log에 적는다)
- **검증**: `dotnet test WorkGroup.slnx` → 실패 0 · `dotnet test WorkGroup.slnx --filter "FullyQualifiedName~WorkGroupsViewModelTests"` → 10건 이상

### T4. `FolderShortcutsViewModel` 케이스 작성

- [ ] **T4-1** `tests/WorkGroup.App.Tests/FolderShortcutsViewModelTests.cs`를 만든다. 준비: `FolderShortcut.Create(id, name, path).Value`
- [ ] **T4-2** T3과 같은 축을 이 VM에 맞춰 세운다 — 검색은 **이름과 경로** 양쪽 부분일치이고, 재정렬 인자는 `int` Id 목록이며, 실패 메시지 출처는 `IFolderShortcutRepository.ReorderAsync`다
- [ ] **T4-3** 두 VM이 같은 동작을 갖는지 대조하는 축을 하나 둔다 — 검색 중 `MoveAsync` 무동작이 **두 페이지에서 같다**는 것이 직전 회차가 두 페이지에 같은 코드를 넣은 근거다
- **Files**: `tests/WorkGroup.App.Tests/FolderShortcutsViewModelTests.cs`(신규)
- **구조**: ⓐ **왜 새 파일인가** — T3과 같은 1파일=1대상 규칙. 두 VM은 인터페이스도 항목 타입도 달라 한 파일에 합치면 제네릭 헬퍼가 먼저 필요해진다(YAGNI). ⓑ **레이어** — `tests/WorkGroup.App.Tests`. ⓒ **재사용** — T2-4의 `Fakes/FakeFolderShortcutRepository.cs`
- **Acceptance**: `dotnet test WorkGroup.slnx --filter "FullyQualifiedName~FolderShortcutsViewModelTests"` → 작성 시점 0건 → **7건 이상, 실패 0**. **변이 실증**: `FolderShortcutsViewModel.ApplyFilter`의 `item.CanReorder = query.Length == 0;`을 `= true;`로 바꾸면 검색 중 `CanReorder` 케이스가 실패해야 한다(구현 중 1회 확인해 Progress Log에 적는다)
- **검증**: `dotnet test WorkGroup.slnx` → 실패 0, 총 193건 이상

### T5. 문서 갱신

- [ ] **T5-1** `AGENTS.md`의 「아키텍처 (DDD 레이어)」에 `tests/WorkGroup.App.Tests`(net10.0-windows, x64 전용, App 참조 — WinAppSDK 부트스트랩 필요)를 추가한다
- [ ] **T5-2** `AGENTS.md`의 「빌드 / 테스트 명령」에 **x64 전용 제약**을 한 줄 적는다 — 그 문장에 `WorkGroup.App.Tests`를 이름으로 명시하고(단독 실행 시 `-p:Platform=x64` 필요), 왜 그런지(App이 WinExe+MSIX라 AnyCPU 패키징이 실패한다)를 함께 적는다
- [ ] **T5-3** `notes.md`「최근 변경」 맨 위에 이번 회차를 추가한다 — 무엇을·왜(직전 회차의 `[면제 ④]`), `DeploymentManager` 자동 초기화자를 왜 껐는지, 수동 검증 대상(T1-7)
- **Files**: `AGENTS.md` · `notes.md`
- **Acceptance**: `grep -c "WorkGroup.App.Tests" AGENTS.md` → 작성 시점 0건 → 2건 이상 · `grep -c "DeploymentManager" notes.md` → 0건 → 1건 이상. [면제 ①] 실행 경로를 바꾸지 않는 문서 수정
- **검증**: `grep -n "WorkGroup.App.Tests" AGENTS.md` · `grep -n "DeploymentManager" notes.md`

## 검증 방법

| task | 명령 | 판정 |
|---|---|---|
| T0 | `git status --short` | `src/WorkGroup.App/WorkGroup.App.csproj` 행 없음 |
| T1 | `dotnet build src/WorkGroup.App/WorkGroup.App.csproj -p:Platform=x64` | 경고 0 / 오류 0 |
| T1 | `grep -c "DeploymentManager.Initialize" src/WorkGroup.App/WindowsAppRuntimeInitializer.cs` | 1 |
| T1 | `dotnet test WorkGroup.slnx --filter "FullyQualifiedName~WindowsAppRuntimeInitializerTests"` | 실패 0 · 3건 |
| T2 | `dotnet build WorkGroup.slnx` | 경고 0 / 오류 0 |
| T2 | `grep -rl "WorkGroup.App.csproj" tests/ --include=*.csproj` | 1건 |
| T3 | `dotnet test WorkGroup.slnx --filter "FullyQualifiedName~WorkGroupsViewModelTests"` | 실패 0 · 10건 이상 |
| T4 | `dotnet test WorkGroup.slnx --filter "FullyQualifiedName~FolderShortcutsViewModelTests"` | 실패 0 · 7건 이상 |
| 전체 | `dotnet test WorkGroup.slnx` | 실패 0 · 총 193건 이상(기준선 173건) |
| T5 | `grep -n "WorkGroup.App.Tests" AGENTS.md` | 2건 이상 |

## 승인 필요 항목

- **구조 변경 — 테스트 프로젝트 신설과 솔루션 등재(T2)**. 왜: App 레이어에 러너가 닿는 자리를 만든다. 영향 범위: `tests/WorkGroup.App.Tests/`(신규 5파일) · `WorkGroup.slnx`(항목 1개 추가) · `dotnet test WorkGroup.slnx`가 이제 `WorkGroup.App`까지 빌드하므로 테스트 시간이 늘어난다(App 빌드 약 45초). 되돌리는 방법: `git revert` 또는 디렉터리 삭제 + slnx 항목 제거 — 기존 173건은 영향받지 않는다.
- **앱 실행 설정 변경 — `WindowsAppSdkDeploymentManagerInitialize=false`와 대체 모듈 초기화자(T1)**. 왜: 이것 하나가 App.dll을 테스트 호스트에서 로드 불가로 만든다(실측). 영향 범위: 패키지 실행 시 WinAppSDK 배포 초기화 시점이 "모듈 로드"에서 "모듈 로드 + 패키지 ID 확인"으로 바뀐다 — 순서는 같고 조건만 붙는다. 판정 함수(`ShouldInitialize`)는 T1-5가 3건으로 재지만 **실제 패키지 실행은 T1-7의 사용자 F5 확인이 필요하다.** 되돌리는 방법: csproj 두 줄(`WindowsAppSdkDeploymentManagerInitialize`·`InternalsVisibleTo`)과 신규 파일 1개를 제거하면 자동 초기화자가 되돌아온다(`git revert` 1회).
- (정보 제공 — 승인 게이트 아님) **로컬 커밋(T0·회차 종료 커밋)**. 전역 지침상 로컬 커밋은 승인 없이 진행한다. 되돌리는 방법: `git reset --soft HEAD~1`. **push·병합·태그·릴리즈·PR은 이번 회차에 하지 않으며, 하게 되면 그때 별도 승인이다.**

## Deferred / Follow-up

- `[다음 회차]` `GroupEditViewModel` 케이스 — 이름 빈 값·중복 이름·앱 0개에서 `StatusMessage`와 저장 차단. 이번 인터뷰에서 범위 밖으로 확정했고, T2가 만든 프로젝트에 파일 하나만 더하면 되므로 진입 비용이 사라진 상태다.
- `[다음 회차]` `SettingsViewModel` 케이스 — 초기화 성공/실패 메시지와 자동 시작 실패 안내.
- `[등재]` 이 레포에는 Deferred 대장(`docs/plans/deferred.md`)이 없다. 위 두 항목은 `plan.md`가 교체되면 사라지므로, 다음 회차 시작 시 이 절을 인계 프롬프트로 읽어야 한다.
- 직전 회차의 GUI 수동 검증 6항목(드래그 비주얼)은 여전히 미검증으로 남아 있다 — 이번 T1-7의 F5 확인과 함께 볼 수 있다.

## Progress Log

- **T0** 패키지 버전 상향 단독 커밋(`95bb026`). 이 레포는 `plan.md`를 **추적**하므로(`.gitignore`에 없다) `git status`가 회차 중 완전히 비지 않는다 — T0 Acceptance를 「App.csproj 행이 사라졌는지」로 좁혔다.
- **T1** `WindowsAppSdkDeploymentManagerInitialize=false` + `WindowsAppRuntimeInitializer.cs`(패키지 ID 가드) + `InternalsVisibleTo`. **변이 실증이 예상보다 강했다**: `ShouldInitialize`를 `true` 고정으로 바꾸면 해당 1건이 아니라 **3건 전부**가 실패한다 — 가드가 풀리면 모듈 초기화자가 실제로 `DeploymentManager.Initialize()`를 불러 `<Module>` 타입 초기화가 터지고, App 타입을 쓰는 모든 케이스가 무너진다. 이 task가 존재하는 이유가 그대로 재현된 것이다.
- **T2** `tests/WorkGroup.App.Tests` 신설 + slnx 등재. slnx의 `Debug|Any CPU → x64` 매핑이 먹어 `bin/x64/Debug/`로 빌드됐다 — **대비책이던 `SetPlatform` 메타데이터는 불필요**했다.

## Next Steps
