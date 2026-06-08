# plan — 설치 앱 열거를 PackageManager → shell:AppsFolder로 전환 (packageQuery 제거)

## 목표
MSIX 패키지 수락 검증 경고("packageQuery requires approval")를 없애기 위해, 패키지(Store/UWP) 앱 열거를 `PackageManager.FindPackagesForUser()`(제한 기능 `packageQuery` 필요)에서 `Shell.Application` COM의 `shell:AppsFolder` 네임스페이스 열거로 전환한다. 그 결과 `Package.appxmanifest`에서 `packageQuery` 제한 기능을 제거한다.

## 범위
- IN:
  - `InstalledAppInventory`의 **패키지 앱 소스**를 `PackageManager` → `shell:AppsFolder` COM 열거로 교체.
  - `Package.appxmanifest`에서 `packageQuery` capability 제거.
  - 패키지 AUMID 판별 순수 헬퍼 + 단위 테스트 추가.
  - 관련 문서(README, notes, 클래스 주석) 갱신.
- OUT (사용자 결정 = 패키지 소스만 교체):
  - **Win32 소스(시작 메뉴 .lnk 열거)는 그대로 유지** — 변경 없음.
  - `AppLauncher`(실행/관리자 권한 실행) 변경 없음.
  - `IconService`/`AppIconLoader`/`ShellIcon` 변경 없음.
  - `runFullTrust` capability 유지.
  - 도메인/직렬화/공개 API/DI 변경 없음.

## 동작 변경 명시 (사용자 승인 = 요청)
- **기존**: 패키지 앱 = `PackageManager.FindPackagesForUser("")` 열거 → `GetAppListEntries()[0]`의 DisplayName/AUMID, IconLocation = 패키지 로고 파일 경로.
- **변경 후**: 패키지 앱 = `shell:AppsFolder` 항목 중 **AUMID 형식(`PackageFamilyName!AppId`, path에 `!` 포함)** 만 추출 → `item.Name`=DisplayName, `item.Path`=AUMID(=LaunchTarget), **IconLocation = null**.
- **아이콘 회귀 근거 + 트레이드오프(확인)**: `AppIconLoader.LoadAsync`(`AppIconLoader.cs:26`)와 `IconService.ResolveMemberBitmapAsync`(`IconService.cs:105`)는 패키지 앱 아이콘을 **항상 `ShellIcon.OpenForAppAsync`(= `shell:AppsFolder\{AUMID}`)로 먼저** 해석한다. AUMID가 유효하면 **ShellIcon 성공 → 아이콘은 기존과 동일하게 렌더(회귀 없음)**.
  - **단, ShellIcon이 실패하는 경우의 폴백은 저하된다(수용된 트레이드오프, M2)**: 기존엔 `ResolveLogoPath`로 얻은 로고 파일 경로가 `IconLocation`에 있어 최후 폴백이 가능했으나, 변경 후 `IconLocation=null`이면 `AppIconLoader.cs:44`(`Kind==Win32 ? LaunchTarget : IconLocation`)의 패키지 폴백 경로가 무력화된다. **수용 근거**: 패키지 아이콘의 정규 렌더 경로는 `shell:AppsFolder\{AUMID}`이며, 유효 AUMID에서 이것이 실패할 확률은 낮고(시작 메뉴가 그리는 것과 동일 메커니즘) DevDashboard도 전적으로 이 경로에만 의존한다. 실패 시 단색 기본 아이콘 폴백은 유지된다.

## 결정 사항 (확정)
- **D1 전환 범위**: 패키지 소스만 교체, Win32 .lnk 소스 유지(사용자 선택). 근거: `packageQuery`는 `PackageManager`에서만 필요하고, Win32를 shell:AppsFolder로 바꾸면 AppsFolder의 Win32 항목 다수가 실제 경로가 아닌 AUMID라 `AppLauncher.LaunchAsAdmin`(runas는 실제 .exe/.lnk 경로 필요)이 깨질 수 있음.
- **D2 패키지 판별 기준**: `shell:AppsFolder` 항목의 `Path`가 **`!`를 포함**하면 패키지 AUMID로 본다(`PackageFamilyName!AppId`는 Windows AUMID 사양상 `!` 구분자 필수 — 기존 테스트 데이터 `Microsoft.WindowsCalculator_8wekyb3d8bbwe!App`와 일치). `.exe`로 끝나는 실제 파일 경로는 제외(이는 Win32 .lnk 소스가 담당). 이 판별을 순수 `internal static` 헬퍼 `IsPackagedAumid`로 분리해 단위 테스트한다.
- **D3 COM 수명 관리**: DevDashboard 참고 구현과 동일하게 `Type.GetTypeFromProgID("Shell.Application")` + `Activator.CreateInstance` + `dynamic` 열거, 각 `item`은 루프 내 `finally`에서 `Marshal.ReleaseComObject`, `folder`/`shell`은 바깥 `finally`에서 해제.
- **D4 시스템 항목 필터**: 별도 이름 기반 필터(`Microsoft.*Extension/Client`)는 **추가하지 않는다**. 기존 `PackageManager` 경로 동작(프레임워크/리소스 패키지만 제외)과 가장 가깝게 유지 — shell:AppsFolder는 "모든 앱" 목록과 동일 집합을 반환하므로 사용자 대상 앱만 노출된다.
- **D5 실패 격리**: 한 소스(패키지) 실패가 전체를 막지 않는 기존 계약 유지. **전체 COM 접근 실패**는 `_logger.LogWarning` 후 빈/부분 목록 반환, **항목 단위 실패**는 `_logger.LogDebug`(노이즈 억제 — 기존 `TryMapPackage`/`ResolveLogoPath`와 동일 수준) 후 해당 항목만 skip. `OperationCanceledException`은 재전파(취소=취소, 기존 PackageManager 경로와 동일). Win32 소스는 독립적으로 계속 수집.

## 영향 범위 전수 조사 (Impact Analysis)

### 변경 대상 & 사용처(전수 grep 후 Read 확인)
- `PackageManager`/`Windows.Management.Deployment` 전 솔루션 grep 결과(정정 — B1): **코드 사용처는 `InstalledAppInventory.cs` 1곳뿐**이나, 문자열 언급은 추가로 ① `WorkGroup.Infrastructure.csproj:4`(TFM 주석에 `Windows.Management.Deployment` 예시), ② `README.md`, ③ `notes.md`에 존재 → 모두 정정 대상(T4).
- `src/WorkGroup.Infrastructure/Inventory/InstalledAppInventory.cs` — 유일한 `PackageManager` **코드** 사용처.
  - 교체 대상 메서드: `GetPackagedAppsAsync`(`:77`), `TryMapPackage`(`:112`), `ResolveLogoPath`(`:140`) → 마지막 둘 삭제, 첫째는 shell:AppsFolder 열거로 재구현.
  - 유지: `GetInstalledAppsAsync`(`:29`, 조합 흐름 불변), `MergeApps`(`:40`), `CreateManualEntry`(`:57`), `GetStartMenuApps`(`:163`), `StartMenuRoots`(`:200`).
- `IAppInventory`(`src/WorkGroup.Application/Inventory/IAppInventory.cs`) — **시그니처 변경 없음**(반환 타입·메서드 동일). 구현체는 `InstalledAppInventory` 1개(grep 확인).
- `AppEntry`(`src/WorkGroup.Domain/Groups/AppEntry.cs`) — 변경 없음. `AppKind.Packaged` 의미(LaunchTarget=AUMID) 그대로 사용.
- DI 등록: `ServiceConfiguration.cs` — `IAppInventory` 등록(타입 동일, 변경 불필요. 확인만).

### 다운스트림 소비처(패키지 AppEntry 사용) — 회귀 점검 완료
- `AppLauncher.BuildSpec`(`AppLauncher.cs:32`): Packaged → `explorer.exe shell:AppsFolder\{AUMID}`. AUMID는 동일 형식 → **불변**.
- `ShellIcon.OpenForAppAsync`(`ShellIcon.cs:35`): Packaged → `shell:AppsFolder\{AUMID}`. **불변**.
- `AppIconLoader.LoadAsync`(`AppIconLoader.cs:18`): ShellIcon 우선, IconLocation 폴백 → IconLocation=null 안전.
- `IconService.ResolveMemberBitmapAsync`(`IconService.cs:102`): 동일. IconLocation=null 안전.

### 직렬화 호환성
- `JsonGroupRepository`는 그룹에 **저장된** AppEntry(AUMID 포함)를 직렬화 — AUMID 형식이 동일하므로 기존 저장 그룹과 호환. IconLocation은 nullable, 누락 무해.

### 영향 받는 테스트
- `tests/WorkGroup.Application.Tests/InstalledAppInventoryTests.cs`:
  - 단위(MergeApps/CreateManualEntry): **불변, 계속 통과**.
  - 통합(`[Trait("Category","Integration")]`): 이제 패키지 소스가 shell:AppsFolder 경유 — 반환 비어있지 않음/표시명 유효/중복 없음 검증은 여전히 유효(머신 의존).
  - **추가**: `IsPackagedAumid` 순수 단위 테스트.
- `AppLauncherTests.cs`: **확인됨** — `BuildSpec_Packaged는_AppsFolder_AUMID`(`AppLauncherTests.cs:30`)가 AUMID `Microsoft.WindowsCalculator_8wekyb3d8bbwe!App`로 검증. AUMID 형식 불변이므로 **계속 통과**(영향 없음).

## 위험
- **COM/dynamic 의존**: `Microsoft.CSharp`(dynamic 런타임 바인더)는 .NET 런타임 기본 포함 — 추가 패키지 불필요. `Type.GetTypeFromProgID`/`Marshal`은 `System.Runtime.InteropServices`(기본). 위험 낮음.
- **스레드 컨텍스트**: `Task.Run`(MTA 스레드풀)에서 Shell.Application 열거 — DevDashboard 동일 패턴으로 검증됨. STA 요구 없음.
- **집합 차이**: shell:AppsFolder 패키지 집합이 `PackageManager` 집합과 미세하게 다를 수 있음(거의 동일, "모든 앱" 기준). 사용자 노출 앱은 동일 수준.
- **AUMID 판별 오탐**: Win32 앱이 명시적 AUMID(예 `Microsoft.Office.WINWORD.EXE.15` — 점 구분, `!` 없음)를 가져도 `!` 미포함이라 패키지로 오분류되지 않음. `!`는 패키지 PFN 구분자 전용.
- **회귀 격리**: Win32 소스·실행·아이콘·직렬화 전부 불변. 패키지 열거 메커니즘만 교체.

## 검증 방법
1. `dotnet build` (솔루션) — 경고/에러 0.
2. `dotnet test` — 기존 단위 + 신규 `IsPackagedAumid` 단위 테스트 통과. 통합 테스트(머신 의존) 통과.
3. **패키지 소스 누락 회귀 검출(B2/M1)**: 통합 테스트에 `apps.Any(a => a.Kind == AppKind.Packaged)` 단언 추가 — shell:AppsFolder 패키지 추출이 0개를 반환하면(=`!` 판별이 너무 엄격하거나 COM 실패) 테스트가 **빨강**으로 즉시 드러난다. (Win32 .lnk만으로 NotEmpty가 통과해 회귀가 은폐되던 구멍을 막음.)
4. 수동(GUI): 그룹 편집 화면에서 설치 앱 목록에 **패키지(Store/UWP) 앱과 Win32 앱이 모두** 표시되는지, 패키지 앱 아이콘이 정상 렌더되는지, 패키지 앱 실행이 동작하는지, **시스템 확장 잡음 항목이 과다 노출되지 않는지(m1)** 확인.
5. `Package.appxmanifest`에 `packageQuery`가 없고 `runFullTrust`만 남았는지 확인. 패키징/배포 시 packageQuery 경고가 사라지는지 확인.

## 작업 분해

### T1 — InstalledAppInventory 패키지 소스를 shell:AppsFolder로 교체 [Type D]
- **파일**: `src/WorkGroup.Infrastructure/Inventory/InstalledAppInventory.cs`
- **내용**:
  - `using Windows.ApplicationModel;`, `using Windows.Management.Deployment;` 제거 → `using System.Runtime.InteropServices;` 추가.
  - `GetPackagedAppsAsync`를 `Task.Run`으로 `GetPackagedAppsFromShellFolder(ct)` 호출하도록 재구현.
  - 신규 private `GetPackagedAppsFromShellFolder(CancellationToken)`: `Shell.Application` COM 생성 → `NameSpace("shell:AppsFolder")` 열거 → 각 항목 `Name`/`Path` 읽어 `IsPackagedAumid(Path)`이면 `new AppEntry(name, path, AppKind.Packaged)`(IconLocation 미지정=null) 추가. 항목별 try/catch + `Marshal.ReleaseComObject(item)`(finally), 바깥 finally에서 folder/shell 해제. `OperationCanceledException` 재전파, 그 외 예외는 `_logger.LogWarning` 후 부분 반환.
  - 신규 `internal static bool IsPackagedAumid(string? path)`: 공백 아님 && `!` 포함 && `.exe`로 끝나지 않음.
  - `TryMapPackage`, `ResolveLogoPath` 삭제.
  - 클래스 XML 주석을 새 메커니즘(패키지=shell:AppsFolder AUMID 열거)으로 갱신.
- **Acceptance**: 빌드 성공, `PackageManager`/`Windows.Management.Deployment` 참조가 파일에서 사라짐(grep 0). `GetInstalledAppsAsync` 시그니처·동작(조합/병합) 불변.
- **Edge Cases**:
  - `Shell.Application` ProgID 미해석 / 인스턴스 null → 빈 목록 반환(폴백).
  - `item.Name` 또는 `item.Path` 빈 값/예외 → 해당 항목 skip.
  - 취소 토큰 발화 → 루프 중 `ThrowIfCancellationRequested` → `OperationCanceledException` 재전파(COM 해제는 finally 보장).
- **Halt Forecast**:
  - `dynamic` 호출 시 런타임 바인더 누락 빌드 오류 → `Microsoft.CSharp`는 기본 포함이므로 불발 예상. 발생 시 빌드 로그로 즉시 식별(코드 추측 금지, build 재확인).

### T2 — Package.appxmanifest에서 packageQuery 제거 [Type A]
- **파일**: `src/WorkGroup.App/Package.appxmanifest`
- **내용**: `<rescap:Capability Name="packageQuery" />`와 그 위 주석 줄(`:72-73`) 제거. `runFullTrust` 유지. `rescap` 네임스페이스 선언은 `runFullTrust`가 계속 사용하므로 유지.
- **Acceptance**: 매니페스트에 `packageQuery` 문자열 없음(grep 0), `runFullTrust` 존재, XML 유효(빌드 통과).

### T3 — 테스트 추가 (IsPackagedAumid 단위 + 패키지 소스 통합 검증) [Type C]
- **파일**: `tests/WorkGroup.Application.Tests/InstalledAppInventoryTests.cs`
- **내용**:
  - `InstalledAppInventoryUnitTests`에 `IsPackagedAumid` 검증 추가:
    - `Microsoft.WindowsCalculator_8wekyb3d8bbwe!App` → true
    - `C:\Program Files\app\app.exe` → false (.exe 경로)
    - `Microsoft.Office.WINWORD.EXE.15` → false (`!` 없음)
    - `null`/빈 문자열 → false
  - `InstalledAppInventoryIntegrationTests`에 **패키지 앱 ≥1개 반환** 검증 추가(B2/M1): `Assert.Contains(apps, a => a.Kind == AppKind.Packaged)`. shell:AppsFolder 패키지 추출이 0개면 실패하도록 하여 조용한 누락을 검출. (개발 머신은 Calculator/Store 등 패키지 앱이 항상 존재.)
- **Acceptance**: `dotnet test` 통과. (InternalsVisibleTo=WorkGroup.Application.Tests 이미 설정됨 — `IsPackagedAumid`는 internal static.)
- **Edge Cases**: null/빈 입력 포함.
- **Halt Forecast**: 통합 테스트의 패키지 ≥1 단언이 실패하면 → `!` 판별이 너무 엄격하거나(실제 `item.Path` 형식이 예상과 다름) COM 열거 실패. 이 경우 멈추고 실제 `item.Path` 샘플 값을 진단(추측 수정 금지). 참고 구현(`.exe 미종료` 기준)으로의 완화는 별도 결정.

### T4 — 문서·구성 주석 갱신 [Type A]
- **파일**: `README.md`, `notes.md`, `src/WorkGroup.Infrastructure/WorkGroup.Infrastructure.csproj`
- **내용**:
  - `README.md`: 설치 앱 수집 설명(`:41` "Store/UWP(PackageManager)")을 "Store/UWP(shell:AppsFolder)"로 갱신. 그 외 PackageManager 언급 위치 grep 후 일괄 정정.
  - `csproj`(`:4` 주석, B1): TFM 주석의 예시 `Windows.Management.Deployment`를 제거(또는 실제 사용 중인 `Windows.Graphics.Imaging` 등으로 교체). **TFM 자체(`net10.0-windows10.0.19041.0`)는 다른 WinRT/Win32 interop가 계속 필요하므로 변경하지 않는다.**
  - `notes.md`: `## 최근 변경` 맨 위에 `- 2026-06-08: 설치 앱 패키지 열거를 PackageManager → shell:AppsFolder COM 방식으로 전환(packageQuery 제한 기능 제거). Win32 .lnk 소스·실행·아이콘 불변.` 추가. 1개월 초과 항목 정리.
- **Acceptance**: README·csproj 주석에 `Windows.Management.Deployment`/`PackageManager` 잔존 언급 없음(grep 0, 변경 이력 문맥 제외). notes 최신 항목 반영. csproj TFM 불변.

## Open Questions
- 없음(전환 범위 확정, 다운스트림 회귀 점검 완료).

## Progress Log
- T1 완료 (커밋 후속): InstalledAppInventory 패키지 소스를 shell:AppsFolder COM 열거로 교체 + IsPackagedAumid 헬퍼. COM 누수(FolderItems) 수정. 빌드/테스트 OK(121/121). spec+quality 리뷰 통과.
- T2 완료: Package.appxmanifest에서 packageQuery 제거(runFullTrust 유지). App 빌드 OK.
