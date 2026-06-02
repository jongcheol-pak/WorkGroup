# plan.md — 설치 앱 아이콘 표시 개선 (패키지 앱 로고 추출)

> 이전 plan(그룹 다이얼로그 개편)은 완료(git 이력). 본 plan은 패키지 앱 아이콘 누락 문제 해결.

## 목표
설치된 앱 목록/팝업/그룹 아이콘에서 일부 항목(주로 UWP/Store 패키지 앱: Teams, Discord 등)의 아이콘이
표시되지 않는 문제를 해결한다. 참고 프로젝트 DevDashboard_WinUI의 "패키지 앱은 셸 로고를 직접 추출"하는
아이디어를, 현재 프로젝트의 WIC 파이프라인에 맞게 **WinRT 네이티브 API**(`AppListEntry.DisplayInfo.GetLogo`)로 적용한다.

## 배경 / 근본 원인 (확인된 사실)
- 패키지 앱은 `InstalledAppInventory.TryMapPackage`에서 `LaunchTarget = AUMID`, `IconLocation = package.Logo` 경로로 매핑된다.
  - `ResolveLogoPath`(InstalledAppInventory.cs:135-154)는 `package.Logo`가 null/비파일이면 `IconLocation = null`을 반환.
- 아이콘 로드는 두 경로:
  - `AppIconLoader.LoadAsync`(App, AppIconLoader.cs:13-41) — 팝업 그리드/멤버 미니 아이콘. 패키지 앱은
    `path = IconLocation`인데 null이면 로드 불가. 설령 AUMID여도 `GetFileFromPathAsync(AUMID)`는 실패.
  - `IconService.ResolveMemberBitmapAsync`(Infrastructure, IconService.cs:94-107) — 그룹 대표 .ico 생성.
    `IconLocation`만 사용하므로 null이면 단색 폴백.
- 결론: **패키지 앱 로고 경로(`package.Logo`)가 없으면 아이콘이 누락**된다. → 셸이 제공하는 공식 로고를
  AUMID로 직접 가져오면 해결된다(`AppDisplayInfo.GetLogo(Size)` — 스케일/대비 자산 자동 선택).

## 결정 사항 (사용자 승인 완료)
- **D1. 구현 방식**: WinRT 네이티브. 패키지 앱은 `PackageManager`→`AppListEntry`→`DisplayInfo.GetLogo(size)`로
  로고 스트림을 얻어 기존 WIC 파이프라인에 연결. **System.Drawing 등 새 의존성 추가 없음.** Win32 앱은 현행 셸 썸네일 유지.
- **D2. 캐시**: 도입하지 않음. 추출 로직만 교체(그룹 .ico 저장은 현행 유지).
- **D3. 적용 범위**: `AppIconLoader`(App) + `IconService`(Infrastructure) 두 곳 모두.

## 범위(Scope)
- In scope: 패키지 앱(`AppKind.Packaged`)의 아이콘을 AUMID 기반 셸 로고로 우선 추출. Win32는 현행 유지(폴백 동일).
- Out of scope: Win32 추출 알고리즘 변경(P/Invoke cascade), 아이콘 PNG 캐시/LRU, AppxManifest XML 파싱,
  `InstalledAppInventory.ResolveLogoPath` 변경, 공개 API/인터페이스 시그니처 변경, DI 등록 변경.

## 위험 (Risks)
- **R1. 패키지 열거 비용**: `FindPackagesForUser("")`는 동기·다소 느림. AUMID에서 PackageFamilyName을 파싱해
  해당 패키지 1개만 매칭하고, 동기 호출은 `Task.Run`으로 오프로드한다(UI 스레드 차단 방지).
- **R2. 비패키지/구버전 환경**: `GetAppListEntries`는 OS ≥ 10.0.19041 필요. 기존 `TryMapPackage`와 동일 가드 적용,
  실패 시 null 반환 → 기존 폴백 경로로 안전하게 흡수.
- **R3. 헤드리스 검증 한계**: 패키지 GUI/로고 추출은 헤드리스 자율 실행에서 실측 불가. 빌드·정적 코드 경로·테스트로만 검증.
- **R4. 스트림 수명**: `GetLogo` 스트림은 호출자가 dispose. `using`으로 누수 방지.

## 영향 범위 전수 조사 (Impact Analysis)
### 4-A. 심볼/호출처 (grep 전수 + Read 확인)
- `AppIconLoader.LoadAsync(AppEntry)` 호출처:
  - `src/WorkGroup.App/ViewModels/PopupAppItem.cs:20`
  - `src/WorkGroup.App/ViewModels/GroupListItem.cs:85`
  - → **시그니처 불변**이므로 호출처 수정 불필요.
- `IconService.CreateGroupIconAsync(...)`: `IIconService` 인터페이스. **시그니처 불변** → 구현/호출처 영향 없음.
- 신규 `PackagedAppIcon` (Infrastructure.Icons): 신규 파일, 기존 호출처 없음.
### 4-B. 계약/직렬화
- 변경 없음(반환 타입·이벤트·저장 포맷 불변).
### 4-C. 영향 테스트
- 기존 단위 테스트 대상은 `MergeApps`(순수 로직). 본 변경은 WinRT interop이라 헤드리스 단위 테스트 불가 →
  신규 테스트 추가 없음(YAGNI). `dotnet build`/`dotnet test`로 회귀만 확인.
### 4-D. 프로젝트 참조 확인 (Read)
- `WorkGroup.App` → `WorkGroup.Infrastructure` 참조 존재(App.csproj:72) → App에서 `PackagedAppIcon` 사용 가능.
- `WorkGroup.Infrastructure`는 WinUI 미참조(Infrastructure.csproj) → `ImageSource` 생성 불가. 따라서 공통 헬퍼는
  **WinRT 스트림**(`IRandomAccessStream`)까지만 책임지고, 각 소비자가 자기 타입(`SoftwareBitmap`/`BitmapImage`)으로 변환한다.
  (레이어 규칙 App→Infrastructure 준수)

### 4-E. WinRT API 사슬 검증 (M1 — 출처 명시)
- `appEntry.DisplayInfo`(`AppDisplayInfo`)는 이미 InstalledAppInventory.cs:121에서 사용 중(검증된 패턴).
- `AppDisplayInfo.GetLogo(Windows.Foundation.Size)` → `RandomAccessStreamReference` 반환 (MS Learn: Windows.ApplicationModel.AppDisplayInfo).
- `RandomAccessStreamReference.OpenReadAsync()` → `IRandomAccessStreamWithContentType`(= `IRandomAccessStream` 구현).
- 입력 호환 선례: `BitmapDecoder.CreateAsync(stream)`는 IconService.cs:128에서 이미 `IRandomAccessStream`을 받아 동작.
  `BitmapImage.SetSourceAsync(IRandomAccessStream)`은 AppIconLoader.cs:30(`SetSourceAsync(thumb)`)에서 이미 사용.
  → 반환 타입 사슬이 기존 소비자 입력과 호환됨이 코드로 확인됨.

---

## 작업 분해 (Tasks)

> 공통: 한글 주석, UTF-8(BOM 없음), 빌드 `dotnet build WorkGroup.slnx` 0/0, 테스트 회귀 없음.
> 자율 종료=빌드/테스트. 패키지 GUI 아이콘 표시는 사용자 수동 검증(R3).

- [x] **T1 — 공통 헬퍼 `PackagedAppIcon` 추가 (Infrastructure)** *(~1.5h)*
  - **Type**: C
  - **신규 파일**: `src/WorkGroup.Infrastructure/Icons/PackagedAppIcon.cs`
  - **Acceptance**:
    - 공개 정적 메서드 `public static Task<IRandomAccessStream?> OpenLogoStreamAsync(string aumid, uint size, CancellationToken cancellationToken = default)`.
    - AUMID에서 `PackageFamilyName`(`'!'` 앞부분) 파싱. 비면 null.
    - OS 가드: `OperatingSystem.IsWindowsVersionAtLeast(10,0,19041)` 아니면 null(`TryMapPackage`와 동일).
    - **데이터 흐름(M2 — Task.Run 경계 명확화)**:
      - 1단계 `var streamRef = await Task.Run<RandomAccessStreamReference?>(() => {...}, ct)`:
        람다 내부에서 `new PackageManager().FindPackagesForUser("")` → `Id.FamilyName == family`(OrdinalIgnoreCase) 첫 패키지.
        없으면 `return null`. `package.GetAppListEntries()` 중 `AppUserModelId == aumid`(Ordinal) 매칭, 없으면 첫 엔트리.
        `return (RandomAccessStreamReference)entry.DisplayInfo.GetLogo(new Windows.Foundation.Size(size, size))`.
        (동기 WinRT 호출만 람다 안에서 수행 — UI 스레드 차단 방지, R1.)
      - 2단계 람다 **밖**: `if (streamRef is null) return null;` → `return await streamRef.OpenReadAsync().AsTask(ct)`.
        (반환 타입 `IRandomAccessStreamWithContentType`는 `IRandomAccessStream`으로 업캐스트되어 메서드 반환.)
    - 전 구간 try/catch → 실패 시 예외 없이 null. 한글 주석으로 "왜" 명시.
    - `dotnet build WorkGroup.slnx` 0/0.
  - **Files**: `src/WorkGroup.Infrastructure/Icons/PackagedAppIcon.cs`(신규)
  - **Edge Cases**: aumid 빈 문자열/`'!'` 없음(family=전체)/패키지 미발견/엔트리 0개/GetLogo 예외/구버전 OS → 모두 null.
    AUMID 매칭 실패 시 "첫 엔트리"는 단일앱 패키지에선 정확, 멀티앱 패키지에선 대표 진입점 폴백(안전, null 아님 — m2).
  - **Halt Forecast**: 없음(신규 파일, 의존성 추가 없음, 순수 WinRT — CsWin32 불필요).
    만약 GetLogo 반환 타입 빌드 에러 시 → 4-E의 BitmapDecoder/SetSourceAsync 입력 호환 타입(`IRandomAccessStream`) 기준으로 캐스팅 조정.
  - **Depends on**: -

- [x] **T2 — `IconService`: 패키지 멤버 앱은 GetLogo 우선** *(~1h)*
  - **Type**: C
  - **Acceptance**:
    - `ResolveMemberBitmapAsync(member, ct)`(IconService.cs:94)에서, `member.Kind == AppKind.Packaged`이면
      `PackagedAppIcon.OpenLogoStreamAsync(member.LaunchTarget, CanvasSize, ct)`를 먼저 시도 →
      스트림 성공 시 `BitmapDecoder.CreateAsync(stream)` → `GetSoftwareBitmapAsync` → `ToBgra8` 반환.
    - 실패(null)면 기존 `IconLocation`(이미지/썸네일) 경로 → 단색 기본 순서 그대로 유지.
    - 신규 private helper `DecodeStreamAsync(IRandomAccessStream stream, CancellationToken ct)` 추가(스트림 디코드 공통화).
    - 빌드 0/0, 기존 테스트 회귀 없음.
  - **Files**: `src/WorkGroup.Infrastructure/Icons/IconService.cs`
  - **Edge Cases**: GetLogo null → 기존 IconLocation 경로. 디코드 예외 → 상위 `CreateGroupIconAsync` catch(IconService.cs:50-54)가 기본 아이콘으로 흡수.
  - **Halt Forecast**: 없음.
  - **Depends on**: T1

- [ ] **T3 — `AppIconLoader`: 패키지 앱은 GetLogo 우선** *(~1h)*
  - **Type**: C
  - **Acceptance**:
    - `LoadAsync(app)`(AppIconLoader.cs:13)에서, `app.Kind == AppKind.Packaged`이면
      `PackagedAppIcon.OpenLogoStreamAsync(app.LaunchTarget, 48, ...)`를 먼저 시도 →
      스트림 성공 시 `BitmapImage` 생성 후 `await bmp.SetSourceAsync(stream)` 반환.
    - 실패(null)면 기존 경로(IconLocation 이미지 파일 직접 → Win32 셸 썸네일 → null) 그대로 유지.
    - using 추가: `WorkGroup.Infrastructure.Icons`, `Windows.Storage.Streams`.
    - 호출처(PopupAppItem.cs:20, GroupListItem.cs:85) 무수정. 빌드 0/0.
  - **Files**: `src/WorkGroup.App/Services/AppIconLoader.cs`
  - **Edge Cases**: GetLogo null/예외 → 기존 경로. `SetSourceAsync` 실패 → 기존 try/catch에서 null.
  - **Halt Forecast**: 없음(App은 이미 Infrastructure 참조).
  - **Depends on**: T1

- [ ] **T4 — 문서 갱신 + 검증 게이트** *(~0.3h)*
  - **Type**: A
  - **Acceptance**:
    - `README.md`: 아이콘 처리 설명에 "패키지 앱은 셸 로고(AppListEntry.DisplayInfo.GetLogo) 우선 추출" 반영(기존 설명 갱신, 최소 추가).
    - `notes.md`: `## 최근 변경` 최상단에 `- 2026-06-02: 패키지 앱 아이콘 추출 개선(GetLogo) — PackagedAppIcon 신규 + AppIconLoader/IconService 적용` 추가. 1개월 초과 항목 제거.
    - 최종 `dotnet build WorkGroup.slnx`(0/0) + `dotnet test WorkGroup.slnx`(회귀 없음) 통과 확인.
  - **Files**: `README.md`, `notes.md`
  - **Depends on**: T1, T2, T3

## 의존 관계
- T1 → (T2, T3). T2·T3 독립. T4는 T1~T3 완료 후.

## 검증 방법
- `dotnet build WorkGroup.slnx` (플랫폼 미지정 — AGENTS.md).
- `dotnet test WorkGroup.slnx` (기존 Domain/Application 테스트 회귀 없음).
- 정적 코드 경로 검토(헤드리스라 GUI 실측 불가 — R3).
- 수동(GUI, 패키지 실행 — 자율 불가): ① 설치 앱 목록/팝업에서 UWP/Store 앱(예: Teams) 아이콘 표시 ②
  패키지 앱을 멤버로 하는 그룹의 대표 .ico에 해당 로고 반영.

## 승인 필요 항목
- 없음. (의존성 추가 없음, 공개 API/인터페이스 시그니처 불변, DI 변경 없음. 신규 파일 1개 + 기존 2개 내부 수정.)

## Open Questions (모두 해결됨)
- [x] 구현 방식 → WinRT 네이티브(GetLogo), 새 의존성 없음 (D1, 사용자).
- [x] 캐시 → 도입 안 함, 추출 품질만 개선 (D2, 사용자).
- [x] 적용 범위 → AppIconLoader + IconService 두 곳 (D3, 사용자).

## Progress Log
<!-- implement-task가 갱신 -->
- T1-T2 완료 (커밋 9e94076, 5501ed4): T1=PackagedAppIcon.OpenLogoStreamAsync 신규(AUMID→FindPackagesForUser→GetAppListEntries→DisplayInfo.GetLogo→IRandomAccessStream, OS 19041 가드, 실패 null). T2=IconService.ResolveMemberBitmapAsync에 Packaged 분기(GetLogo 우선)+DecodeStreamAsync 공통화(이미지/썸네일/로고 3경로 통일). 빌드 0/0, 테스트 80/80, spec OK.
