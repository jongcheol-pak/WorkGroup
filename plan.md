# Plan — 폴더 바로가기(트레이 좌클릭 폴더 팝업) 기능 추가

AppGroup(`D:\Personal Project\Windows\AppGroup`)의 "시작 메뉴 폴더" 기능을 WorkGroup에 **별개 기능으로 신규 이식**한다.
디스크 폴더 경로를 등록해 두고, 트레이 아이콘 **좌클릭** 시 등록 폴더 목록 팝업을 띄우고,
폴더에 마우스를 올리면 그 안의 파일/하위폴더를 2차 팝업으로 보여준다. 관리 화면은 기존 "트레이 메뉴" 탭(`TrayMenuPage`)에 구현한다.

## 목표
1. **트레이 메뉴 탭(시작 화면)**: `TrayMenuPage`(현재 placeholder)를 폴더 관리 화면으로 구현 — 안내문 + 검색창 + "N개 폴더" 카운트 + 폴더 카드 목록 + 추가(+) + 설정(톱니) + 각 항목 편집(연필)/⋯(위치 열기·삭제) (이미지 1).
2. **트레이 좌클릭 폴더 팝업**: 좌클릭 시 등록 폴더 목록 팝업(`FolderListPopupWindow`), 폴더 호버 시 그 안의 파일/하위폴더 2차 팝업(`FolderContentsPopupWindow`, 재귀 깊이 설정) (이미지 2).
3. **팝업 설정**: 폴더 팝업 열 개수(1~5), 하위폴더 탐색 깊이(1~5), 숨김 파일/폴더 표시 여부.

## 범위
- **In scope**:
  - Domain: `Folders/FolderShortcut`, `Folders/FolderPopupSettings` 신규.
  - Application: `Folders/IFolderShortcutRepository`, `Folders/IDirectoryBrowser`, `Folders/IShellOpener` 신규.
  - Infrastructure: `Persistence/JsonFolderShortcutRepository`(folders.json), `Folders/DirectoryBrowser`, `Folders/ShellOpener` 신규. 폴더/파일 아이콘은 기존 `Icons/ShellIcon` 재사용.
  - App: `Services/FolderPopupSettingsService`(LocalSettings, ThemeService 패턴), `ViewModels/FolderShortcutsViewModel`·`FolderShortcutItem`, `Views/TrayMenuPage`(개조)·`FolderEditDialog`·`FolderListPopupWindow`·`FolderContentsPopupWindow`, `Services/TrayIconService`(좌클릭 이벤트 분리), `App.xaml.cs`(트레이 동작), `ServiceConfiguration`(DI), `WorkGroupPaths`(folders.json 경로).
  - 테스트: Domain(검증/클램프), Application/Infrastructure(저장소 라운드트립, DirectoryBrowser 숨김 필터).
- **Out of scope**:
  - 기존 "작업 그룹"(앱 묶음→작업표시줄 핀, `WorkGroupsPage`/`GroupPopupWindow`/`AppGroup` 도메인) 기능 — **그대로 유지, 손대지 않음**.
  - 폴더 항목 커스텀 아이콘 선택(셸 기본 폴더 아이콘만 사용 — 이미지에도 기본 폴더 아이콘만 노출). 향후 확장.
  - 폴더 목록 드래그 재정렬(AppGroup엔 있으나 1차 범위 제외).
  - 다국어 리소스(.resw) — 기존 프로젝트가 코드 내 한글 문자열을 쓰므로 동일하게 한글 직접 기입.

## 결정사항 (사용자 확정)
- **Q1(B)**: 트레이 **좌클릭 = 폴더 목록 팝업**. 메인 관리 창은 **우클릭 메뉴 "열기"로만** 연다(더블클릭으로 메인 창 열기 제거).
- **Q2(A)**: 2차 폴더 내용 팝업(호버 시 파일/하위폴더) **구현**, 관련 **설정 기능**(열 개수/하위폴더 깊이/숨김 표시) 포함.
- **Q3**: 기존 "작업 그룹" 기능 **유지**, 본 기능은 **별개**.
- **화면 위치**: 새 탭 추가가 아니라 **기존 "트레이 메뉴" 탭(`TrayMenuPage`)**에 폴더 관리 화면 구현. NavigationView 메뉴 항목은 현행 유지(탭 이름 "트레이 메뉴", 아이콘 List).

## 핵심 재사용 인프라 (확인 완료)
- `Infrastructure/Interop/TaskbarPopupPositioner.cs`(L35-62) + `ScreenMetricsProvider.cs` — 작업표시줄 변 판정 + 좌표 계산. 두 팝업 창에 그대로 재사용.
- `Views/GroupPopupWindow.xaml.cs`(전체) — Mica 배경 + `SetBorderAndTitleBar(true,false)` + `IsAlwaysOnTop` + `IsShownInSwitchers=false` + 화면 밖 측정 후 표시 + `AdjustToContent`(콘텐츠 높이 측정) + `OnActivated` Deactivated 시 Close. **두 새 팝업 창의 베이스 패턴**.
- `Services/ThemeService.cs`(L25-68) — `ApplicationData.Current.LocalSettings` 읽기/쓰기. `FolderPopupSettingsService`가 동일 패턴.
- `Persistence/JsonGroupRepository.cs` — 원자적 쓰기는 `WriteUnlockedAsync`(L116-137, temp→`File.Move(overwrite)`), 손상 백업은 `BackupCorruptFile`(L139-151), `SemaphoreSlim` 직렬화. `JsonFolderShortcutRepository`가 동일 패턴.
- `Icons/ShellIcon.cs` — **현재 public은 `OpenForAppAsync(AppEntry app, uint size, ...)` 단 하나**(L32)이고 경로 기반 `OpenStreamAsync(parsingName, size, ...)`는 **private**(L41). `AppIconLoader`(L19)도 `AppEntry` 전용이라 **임의 경로 아이콘 로드는 현재 불가**. → **T4에서 `public static Task<IRandomAccessStream?> OpenForPathAsync(string parsingName, uint size, CancellationToken)` 오버로드를 추가**(내부에서 기존 private `OpenStreamAsync` 그대로 호출). 폴더 경로도 `SHCreateItemFromParsingName`이 셸 폴더 아이콘을 반환하므로 파일·폴더 공통 사용 가능. App 레이어 변환은 **T5의 신규 `FolderIconLoader`**(AppIconLoader 패턴: 스트림→`BitmapImage`)가 담당.
- `ViewModels/SettingsViewModel.cs`(L24-81) — `[ObservableProperty]` TwoWay + `_suppress` 가드 + 변경 핸들러. 설정 UI 패턴.
- `ViewModels/WorkGroupsViewModel.cs` + `Views/WorkGroupsPage.xaml` — 목록/카운트/카드/검색 없는 버전. `FolderShortcutsViewModel`/`TrayMenuPage`가 검색 추가해 참고.

## 위험
- **트레이 동작 변경(회귀 위험)**: 현재 `TrayIconService.WindowProc`(L108-109)에서 `WM_LBUTTONUP or WM_LBUTTONDBLCLK → OpenRequested`(메인 창). 좌클릭을 폴더 팝업으로 바꾸면 **기존 "좌클릭=메인 창" 동작이 사라진다**(사용자 Q1=B로 의도된 변경). 메인 창 접근 경로가 우클릭 "열기"로만 남으므로, 우클릭 메뉴 "열기"(`CMD_OPEN → OpenRequested`)는 반드시 유지.
- **팝업 창 생성 비용/재사용**: AppGroup은 팝업 창을 미리 1개 생성해 숨겨두고 재사용(`startMenuPopupWindow?.ShowPopup()`). WorkGroup `GroupPopupWindow`는 매 클릭 새 창 생성 후 닫힘. 좌클릭은 빈번하므로 **매번 새 창 생성 방식**을 따르되(패턴 일관성), 생성 비용이 문제되면 재사용으로 전환(Decision D-팝업수명).
- **파일시스템 접근 예외**: 등록 폴더가 삭제/이동/권한없음일 수 있음 → `DirectoryBrowser`에서 `UnauthorizedAccessException`/`DirectoryNotFoundException` 처리 후 빈 상태/안내.
- **DDD 레이어 경계**: 파일시스템 열거/셸 실행은 Infrastructure. UI(팝업)는 Application 인터페이스(`IDirectoryBrowser`/`IShellOpener`)만 의존. 도메인은 파일시스템 무의존.
- **설정 저장 위치 이원화**: 폴더 목록 = `folders.json`(파일, Infrastructure). 팝업 설정 3개 = `LocalSettings`(App, ThemeService 패턴). 혼동 방지를 위해 명확히 분리.
- **포커스 전환 닫힘 vs 2차 팝업**: 1차 팝업이 Deactivated 시 닫히는데, 2차 내용 팝업으로 포커스가 가면 1차가 닫혀버릴 수 있음 → AppGroup은 2차 팝업을 별 창으로 띄우되 1차 위에 표시. **2차 팝업 표시 중에는 1차를 닫지 않도록** 처리 필요(Decision D-팝업포커스).

## DDD 레이어 배치 (확정)
| 레이어 | 신규 요소 | 파일 |
|---|---|---|
| Domain | `FolderShortcut`(엔티티), `FolderPopupSettings`(값 객체) | `WorkGroup.Domain/Folders/` |
| Application | `IFolderShortcutRepository`, `IDirectoryBrowser`+`DirectoryListing`/`DirectoryEntryInfo`, `IShellOpener` | `WorkGroup.Application/Folders/` |
| Infrastructure | `JsonFolderShortcutRepository`, `DirectoryBrowser`, `ShellOpener`, `ShellIcon`(public `OpenForPathAsync` 오버로드 추가) | `WorkGroup.Infrastructure/Persistence/`, `WorkGroup.Infrastructure/Folders/`, `WorkGroup.Infrastructure/Icons/ShellIcon.cs` |
| App | `FolderPopupSettingsService`, `FolderIconLoader`, `FolderShortcutsViewModel`, `FolderShortcutItem`, `TrayMenuPage`(개조), `FolderEditDialog`, `FolderListPopupWindow`, `FolderContentsPopupWindow` | `WorkGroup.App/Services/`, `ViewModels/`, `Views/` |

---

## Phase A — 데이터 + 관리 화면 (폴더 등록/관리 가능까지)

### T1 — Domain: FolderShortcut 엔티티 + FolderPopupSettings 값 객체 (Type D)
- **Files**:
  - `src/WorkGroup.Domain/Folders/FolderShortcut.cs` (신규)
  - `src/WorkGroup.Domain/Folders/FolderPopupSettings.cs` (신규)
  - `tests/WorkGroup.Domain.Tests/FolderShortcutTests.cs` (신규)
  - `tests/WorkGroup.Domain.Tests/FolderPopupSettingsTests.cs` (신규)
- **변경**:
  - `FolderShortcut`: 식별자 `int Id`, `string Name`, `string Path`. `Result<FolderShortcut> Create(int id, string name, string path)` 팩토리 — Name/Path 공백 불가 검증(`Result` 패턴, 기존 `WorkGroup.Domain/Common/Result.cs` 사용). `Rename`/`ChangePath`는 1차 범위에선 불요(편집은 새 인스턴스 생성으로 충분) — 단순 불변 record 형태로.
  - `FolderPopupSettings`: `int ColumnCount`, `int SubfolderDepth`, `bool ShowHiddenItems`. 기본값 `Default`(ColumnCount=1, SubfolderDepth=2, ShowHiddenItems=false — AppGroup 기본값과 일치). 생성 시 ColumnCount/SubfolderDepth를 **1~5로 클램프**하는 팩토리 `Create(int columnCount, int subfolderDepth, bool showHiddenItems)`.
- **Decision points**:
  - 식별자: AppGroup과 동일하게 **int 단조 증가**(folders.json 키). 기존 그룹은 GUID지만 폴더는 별개 저장소이므로 int로 단순화(드래그/핀 대상 아님 → GUID 불필요).
  - FolderShortcut는 가변 동작 없음 → record(불변). 편집은 같은 Id로 새 인스턴스 저장.
  - 클램프 범위 1~5는 도메인 불변식(설정 UI도 1~5만 제공).
- **Edge cases**: Name/Path 빈 문자열·공백 → `Result.Fail`. ColumnCount=0 또는 99 → 1 또는 5로 클램프. SubfolderDepth 동일.
- **Acceptance**:
  - `FolderShortcut.Create(1,"Module","D:\\Module").IsSuccess == true`.
  - `Create(1,"","D:\\x").IsSuccess == false`, `Create(1,"x","  ").IsSuccess == false`.
  - `FolderPopupSettings.Create(0,9,false)` → ColumnCount==1, SubfolderDepth==5.
  - `FolderPopupSettings.Default` → (1,2,false).
  - `dotnet build` + Domain 테스트 통과.
- **Halt Forecast**: `Result`/`Result<T>` API 형태가 불명확하면 → `src/WorkGroup.Domain/Common/Result.cs`와 `AppGroup.Create`(L30-37) 사용례를 그대로 따른다.

### T2 — Application: 인터페이스 정의 (Type D)
- **Files**:
  - `src/WorkGroup.Application/Folders/IFolderShortcutRepository.cs` (신규)
  - `src/WorkGroup.Application/Folders/IDirectoryBrowser.cs` (신규)
  - `src/WorkGroup.Application/Folders/IShellOpener.cs` (신규)
- **변경**:
  - `IFolderShortcutRepository`: `Task<IReadOnlyList<FolderShortcut>> LoadAllAsync(CancellationToken)`, `Task<Result<FolderShortcut>> AddAsync(string name, string path, CancellationToken)`(다음 Id 부여+저장), `Task<Result> UpdateAsync(int id, string name, string path, CancellationToken)`, `Task<Result> DeleteAsync(int id, CancellationToken)`. (기존 `IGroupRepository` 시그니처 스타일 참고.)
  - `IDirectoryBrowser`: `DirectoryListing Browse(string path, bool showHidden)`. `DirectoryListing`(record): `IReadOnlyList<DirectoryEntryInfo> Files`, `IReadOnlyList<DirectoryEntryInfo> Folders`, `DirectoryBrowseStatus Status`(Ok/NotFound/AccessDenied/Empty). `DirectoryEntryInfo`(record): `string Name`, `string FullPath`, `bool IsDirectory`.
  - `IShellOpener`: `void Open(string path)` — 폴더/파일을 셸 기본 동작으로 연다.
- **Decision points**:
  - 폴더 열거를 Application 인터페이스로 추상화 → 팝업(App)이 파일시스템 직접 의존하지 않음(테스트 가능). 동기 메서드(폴더 한 단계 열거는 빠름, AppGroup도 동기).
  - 중복 경로 검증은 repository `AddAsync`/`UpdateAsync` 내부(대소문자 무시) → `Result.Fail("이미 등록된 폴더입니다")`.
- **Edge cases**: N/A(인터페이스 정의). 계약상 `Browse`는 예외를 던지지 않고 Status로 표현.
- **Acceptance**: `dotnet build`(Application) 통과. 인터페이스만 — 구현은 T3/T4.
- **Halt Forecast**: `Result`/`Result<T>` 네임스페이스 import 경로는 `WorkGroup.Domain.Common` 확인 후 사용.

### T3 — Infrastructure: JsonFolderShortcutRepository (folders.json) (Type D)
- **Files**:
  - `src/WorkGroup.Infrastructure/Persistence/JsonFolderShortcutRepository.cs` (신규)
  - `src/WorkGroup.Infrastructure/WorkGroupPaths.cs` (수정 — folders.json 경로 추가)
  - `tests/WorkGroup.Application.Tests/JsonFolderShortcutRepositoryTests.cs` (신규)
- **변경**:
  - `WorkGroupPaths`: `public static string FoldersConfigPath => Path.Combine(RootDirectory, "folders.json");` 추가(기존 `ConfigDirectory`=RootDirectory와 동일 폴더).
  - `JsonFolderShortcutRepository`: 생성자 `(string filePath, ILogger)`. `JsonGroupRepository` 패턴 그대로 — `SemaphoreSlim` 직렬화, 원자적 쓰기(temp→`File.Move(overwrite)`), 손상 시 `.corrupt.bak` 백업 후 빈 목록 복구. DTO: `FoldersFileDto(int SchemaVersion, List<FolderDto> Folders)`, `FolderDto(int Id, string Name, string Path)`. `LoadAllAsync`/`AddAsync`(다음 Id=Max+1, 중복 경로 검사)/`UpdateAsync`/`DeleteAsync`(멱등).
  - DI는 T11에서 등록.
- **Decision points**:
  - 저장 위치: `%USERPROFILE%\WorkGroup\folders.json`(groups.json과 같은 폴더, 비가상화). 그룹과 파일 분리 → 상호 영향 없음.
  - 스키마 버전 1 시작(향후 마이그레이션 대비).
  - Id 부여: 최대 Id+1(삭제해도 재사용 안 함), 빈 파일이면 1.
- **Edge cases**: 파일 없음 → 빈 목록. 손상 JSON → 백업 후 빈 목록(그룹 저장소와 동일 정책). 중복 경로 Add → Fail. 없는 Id Update/Delete → Delete는 멱등 Ok, Update는 Fail(KeyNotFound 의미).
- **Acceptance**:
  - Add→Load 라운드트립: 추가한 폴더가 로드됨, Id 1부터.
  - 같은 경로 중복 Add → `Result.Fail`.
  - Update로 이름/경로 변경 후 Load 반영.
  - Delete 후 Load에서 제외, 없는 Id Delete는 Ok.
  - 손상 파일 로드 시 예외 없이 빈 목록 + .corrupt.bak 생성.
  - `dotnet test`(Application.Tests) 통과.
- **Halt Forecast**: `JsonGroupRepository`의 정확한 원자적 쓰기/백업 코드(L116-151)를 직접 열어 동일 구조로 복제. JsonSerializer 옵션도 동일하게.

### T4 — Infrastructure: DirectoryBrowser + ShellOpener + ShellIcon 경로 오버로드 (Type D)
- **Files**:
  - `src/WorkGroup.Infrastructure/Folders/DirectoryBrowser.cs` (신규)
  - `src/WorkGroup.Infrastructure/Folders/ShellOpener.cs` (신규)
  - `src/WorkGroup.Infrastructure/Icons/ShellIcon.cs` (수정 — public 경로 오버로드 추가)
  - `tests/WorkGroup.Application.Tests/DirectoryBrowserTests.cs` (신규)
- **변경**:
  - `DirectoryBrowser : IDirectoryBrowser`: `Browse(path, showHidden)` — `Directory.Exists` 확인(없으면 Status=NotFound). `Directory.GetFiles`/`GetDirectories` → `FileInfo`/`DirectoryInfo`로 숨김 속성 필터(`showHidden || (attr & Hidden)==0`), 이름 정렬(`OrderBy(Name)`). 파일/폴더 0개면 Status=Empty. `UnauthorizedAccessException` → Status=AccessDenied. (AppGroup `LoadFolderContents` L314-406 로직 이식, UI 비의존 순수 버전.)
  - `ShellOpener : IShellOpener`: `Open(path)` — `Process.Start(new ProcessStartInfo{ FileName = path, UseShellExecute = true })`. 예외는 로깅 후 무시(없는 경로 등).
  - **`ShellIcon.cs`(B1 해소)**: `public static Task<IRandomAccessStream?> OpenForPathAsync(string parsingName, uint size, CancellationToken cancellationToken = default)` 추가 — 본문은 `OpenStreamAsync(parsingName, size, cancellationToken)` 한 줄 호출(기존 private 메서드 L41 재사용, **기존 `OpenForAppAsync`/private 메서드는 무변경**). 폴더/파일 경로 모두 그대로 전달(`SHCreateItemFromParsingName`이 폴더면 셸 폴더 아이콘 반환).
- **Decision points**:
  - 숨김 필터/정렬을 Infrastructure에 둬 팝업 UI는 결과만 렌더.
  - `ShellOpener`는 폴더(탐색기)·파일(기본앱) 모두 `UseShellExecute=true`로 통일.
  - `ShellIcon`는 **public 표면만 확장**(기존 `OpenForAppAsync` 소비자 `AppIconLoader.cs:19`·`IconService.cs:100` 영향 없음 — 새 오버로드 추가일 뿐).
- **Edge cases**: 경로 없음 → NotFound. 권한 없음 → AccessDenied. 빈 폴더 → Empty. 숨김 항목 토글. 시스템/정션 폴더 접근 예외 → AccessDenied로 흡수. `OpenForPathAsync` 빈/없는 경로 → null(기존 `OpenStreamAsync` L43·L56-60이 흡수).
- **Acceptance**:
  - 임시 디렉터리에 파일/폴더/숨김파일 생성 → `Browse(dir,false)`는 숨김 제외, `Browse(dir,true)`는 포함.
  - 없는 경로 → Status==NotFound. 빈 디렉터리 → Status==Empty. 정렬: 이름 오름차순.
  - `ShellIcon.OpenForPathAsync(<유효 폴더 경로>, 48)`가 non-null 스트림 반환(수동/통합 — COM 호출이라 단위테스트 제외 명시), 없는 경로는 null.
  - `dotnet build` + `dotnet test`(DirectoryBrowser) 통과. ShellOpener/ShellIcon은 셸 호출이라 단위테스트 제외(수동 확인).
- **Halt Forecast**:
  - 숨김 속성 비트 연산은 `(f.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden` 형태(AppGroup과 동일).
  - `OpenForPathAsync` 추가 시 기존 `OpenForAppAsync`/`OpenStreamAsync` 시그니처를 건드리면 컴파일 깨짐 → **신규 public 메서드만 추가**(L39와 L41 사이에 삽입), 기존 본문 무변경.

### T5 — App: FolderPopupSettingsService + FolderIconLoader (Type C)
- **Files**:
  - `src/WorkGroup.App/Services/FolderPopupSettingsService.cs` (신규)
  - `src/WorkGroup.App/Services/FolderIconLoader.cs` (신규)
- **변경**:
  - `FolderPopupSettingsService`: `ThemeService` 패턴(L25-68) 그대로 — `ApplicationData.Current.LocalSettings.Values["FolderPopupSettings"]`에 JSON 직렬화 문자열로 저장. `FolderPopupSettings Read()`(없거나 예외 시 `FolderPopupSettings.Default`), `void Save(FolderPopupSettings)`(try/catch 흡수). 도메인 `FolderPopupSettings`를 그대로 직렬화(System.Text.Json).
  - `FolderIconLoader`(B1 App측): `AppIconLoader`(L11-53) 패턴 — `public static async Task<ImageSource?> LoadAsync(string path, uint size = 48)`. `ShellIcon.OpenForPathAsync(path, size)`로 스트림→`BitmapImage.SetSourceAsync`. 실패/null → null(호출자 플레이스홀더). 폴더·파일 경로 공통.
- **Decision points**:
  - 인터페이스 없이 App 서비스로 둔다(ThemeService·StartupService·AppIconLoader와 동일 — UI 전용 정적 클래스, 테스트 대상 아님). 설정값 검증(클램프)은 도메인 `FolderPopupSettings.Create`가 담당.
  - 키 1개에 JSON 묶음 저장(개별 키 분산 대신) → 원자적이고 단순.
  - `FolderIconLoader`는 정적 클래스(AppIconLoader와 동일) → DI 등록 불필요.
- **Edge cases**: 비패키지/접근 실패 → Default 반환(앱 동작 유지). 손상 JSON → Default. `FolderIconLoader` 없는/빈 경로 → null(셸 기본 폴더 아이콘 폴백은 ShellIcon이 처리, 그래도 null이면 호출자가 플레이스홀더).
- **Acceptance**:
  - `Read()` 후 `Save(new(3,4,true))` → 재 `Read()` 시 (3,4,true). 미저장 상태 `Read()` → Default.
  - `FolderIconLoader.LoadAsync(<유효 폴더 경로>)`가 non-null ImageSource(수동/GUI 확인 — 셸 호출).
  - `dotnet build` 통과.
- **Halt Forecast**: `LocalSettings` 접근이 비패키지 디버그에서 throw하면 → ThemeService처럼 try/catch로 Default 폴백(이미 설계 반영). `FolderIconLoader`는 T4의 `ShellIcon.OpenForPathAsync`에 의존 → **T4 완료가 선행 조건**(의존 관계에 반영).

### T6 — App: TrayMenuPage 폴더 관리 화면 + ViewModel + 편집 다이얼로그 (Type D)
- **현재 구조(확인 완료, 개조 전제)**: `TrayMenuPage.xaml`은 placeholder — `<ScrollViewer Padding="{StaticResource PageContentPadding}">` > `<StackPanel MaxWidth="{StaticResource ContentMaxWidth}">` 안에 제목 헤더("트레이 메뉴" + 부제) + `InfoBar`("추후 추가 예정")만 있음(L10-22). code-behind(`TrayMenuPage.xaml.cs`)는 `InitializeComponent`만(L8-11, ViewModel/DataContext 없음). `MainShell.xaml`의 탭은 `<NavigationViewItem Content="트레이 메뉴" Tag="TrayMenu">`(L60-64), 라우팅은 `MainShell.xaml.cs`의 `"TrayMenu" => typeof(TrayMenuPage)`. **개조는 이 placeholder 본문(StackPanel 내부)을 교체**하고 탭/라우팅은 손대지 않는다.
- **Files**:
  - `src/WorkGroup.App/ViewModels/FolderShortcutItem.cs` (신규 — 목록 항목)
  - `src/WorkGroup.App/ViewModels/FolderShortcutsViewModel.cs` (신규)
  - `src/WorkGroup.App/Views/TrayMenuPage.xaml` (개조 — placeholder 본문 교체)
  - `src/WorkGroup.App/Views/TrayMenuPage.xaml.cs` (개조 — VM 주입 + 추가/편집/삭제/위치열기 핸들러, FolderPicker)
  - `src/WorkGroup.App/Views/FolderEditDialog.xaml` (신규 — 추가/편집 다이얼로그)
  - `src/WorkGroup.App/Views/FolderEditDialog.xaml.cs` (신규)
- **변경**:
  - `FolderShortcutItem`: `int Id`, `string Name`, `string Path`, `[ObservableProperty] ImageSource? Icon`. `Task LoadIconAsync()` — **`FolderIconLoader.LoadAsync(Path)`**(T5)로 폴더 경로 아이콘 로드, null이면 플레이스홀더 유지. (`PopupAppItem.LoadIconAsync` 패턴.)
  - `FolderShortcutsViewModel`: `ObservableCollection<FolderShortcutItem> Folders`(전체), `FilteredFolders`(검색 적용), `[ObservableProperty] string SearchText`, `string FolderCountText`(예 "3개 폴더" — **전체 개수 기준**, 검색과 무관), `bool IsEmpty`. `LoadAsync()`(repository LoadAll→Item 생성→아이콘 로드), `DeleteAsync(id)`, 검색 필터(`OnSearchTextChanged`에서 `FilteredFolders` 재구성, 대소문자 무시 Name/Path contains). (`WorkGroupsViewModel` + AppGroup `ApplyFilter` 패턴.)
  - `TrayMenuPage.xaml`: 기존 placeholder 레이아웃(`ScrollViewer Padding=PageContentPadding` > `StackPanel MaxWidth=ContentMaxWidth`)을 **유지**하고 내부 본문만 교체. 상단 안내문("트레이 아이콘을 클릭하면 등록된 폴더가 표시됩니다."), 검색 `TextBox`(placeholder "폴더 검색...", `Text` TwoWay 바인딩), "N개 폴더" + 추가(+, `SymbolIcon Add`)·설정(톱니, `SymbolIcon Setting`) 버튼 행, 폴더 카드 `ListView`(`FilteredFolders` 바인딩, 카드별 아이콘 35~50px + 이름 + 경로 + 편집 연필 + ⋯ MenuFlyout[위치 열기/삭제]), 빈 상태 안내. **목록은 `ListView`(자체 가상 스크롤)이므로 바깥 `ScrollViewer`와 중첩되지 않게 ListView에 높이/`MaxHeight` 또는 `ItemsControl` 택1** — `WorkGroupsPage.xaml`(L11-13, Grid 행* + ListView)을 참고해 충돌 없는 구조로.
  - `TrayMenuPage.xaml.cs`: `FolderShortcutsViewModel` 주입(`App.Services`), `Loaded`에서 `LoadAsync`. 추가(+) → `FolderEditDialog`(신규 모드) → 저장 시 `repository.AddAsync` → 재로드. 편집(연필) → `FolderEditDialog`(편집 모드, 기존 값) → `UpdateAsync`. ⋯ "위치 열기" → `IShellOpener.Open(path)`. ⋯ "삭제" → 확인 `ContentDialog` → `DeleteAsync`. 톱니 → 설정 UI(T10).
  - `FolderEditDialog`: `ContentDialog`. 이름 `TextBox`(필수), 경로 표시 `TextBlock` + "찾아보기" 버튼(`FolderPicker` + `InitializeWithWindow`로 HWND 연결 — `App.MainWindow`). 추가 모드는 경로 선택 시 이름 비었으면 폴더명 자동 채움(AppGroup L1709-1713). 중복/빈값 검증 메시지 표시. 결과(이름/경로)를 page가 받아 repository 호출.
- **Decision points**:
  - 검색 필터는 ViewModel에서 `FilteredFolders` 컬렉션 재구성(WinUI는 ICollectionView 제약 → AppGroup·기존 패턴대로 별도 컬렉션).
  - 폴더 아이콘은 셸 폴더 아이콘(커스텀 미지원).
  - FolderPicker HWND는 `App.MainWindow`(트레이 메뉴 탭은 메인 창 안에서만 열림 → MainWindow 항상 존재).
  - 편집은 같은 Id로 `UpdateAsync`(도메인 record 새 인스턴스).
- **Edge cases**: 폴더 0개 → 빈 상태 + "0개 폴더". 검색 결과 0 → 빈 목록(카운트는 전체 기준 또는 검색 결과 기준 — **전체 기준 "N개 폴더" 유지**, 이미지1과 동일). 등록 폴더가 실제로 삭제됨 → 목록엔 남고 아이콘은 기본 폴더 아이콘(열기 시 ShellOpener가 실패 흡수). 이름 중복은 허용(경로만 유일).
- **Acceptance**:
  - 트레이 메뉴 탭에 안내문/검색/카운트/카드/추가/톱니가 이미지1처럼 표시(F5 수동).
  - 폴더 추가(찾아보기로 경로 선택→이름 자동) 후 목록·카운트 갱신.
  - 편집으로 이름/경로 변경 반영, ⋯ 위치 열기로 탐색기 열림, 삭제 확인 후 제거.
  - 검색어로 목록 필터링.
  - `dotnet build`(x64) 통과.
- **Halt Forecast**:
  - FolderPicker가 패키지 앱에서 HWND 미초기화로 throw → `WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd)` 필수(AppGroup L1700-1702).
  - 공통 레이아웃 토큰(`PageContentPadding` 등) 키 이름은 `Resources/Spacing.xaml`·기존 `WorkGroupsPage.xaml` 확인 후 동일 적용.

---

## Phase B — 트레이 좌클릭 팝업 (등록 폴더 표시)

### T7 — TrayIconService 좌클릭 이벤트 분리 + App.xaml.cs 트레이 동작 변경 (Type D)
- **Files**:
  - `src/WorkGroup.App/Services/TrayIconService.cs` (수정)
  - `src/WorkGroup.App/App.xaml.cs` (수정)
- **변경**:
  - `TrayIconService`: `public event Action? LeftClickRequested;` 추가(L42 근처). `WindowProc`(L106-112)에서 `WM_APP_TRAY` 시 **`WM_LBUTTONUP → LeftClickRequested?.Invoke()`만 트리거**하고 **`WM_LBUTTONDBLCLK`는 무시**(현재 L108의 `is WM_LBUTTONUP or WM_LBUTTONDBLCLK`에서 DBLCLK를 분리 — 더블클릭 시 팝업 깜빡임 방지). 기존 `OpenRequested` 호출을 좌클릭 분기에서 제거. 우클릭 메뉴 `CMD_OPEN → OpenRequested`(L116)는 **유지**(메인 창 진입). `ShowContextMenu`(L125-136) "열기"/"종료" 유지.
  - `App.xaml.cs EnsureTray`(L66-83): `_tray.OpenRequested += ShowMainWindow`(유지, 우클릭 열기용). `_tray.LeftClickRequested += ShowFolderListPopup`(신규) 추가. `ExitRequested` 유지.
  - `App.xaml.cs`: `private void ShowFolderListPopup()` 추가 — `new FolderListPopupWindow().Activate()`(T8). 좌클릭은 빈번하므로 기존 팝업이 떠 있으면 닫고 새로(또는 토글) — **기존 인스턴스 추적 필드 `_folderPopup`** 두고 열려있으면 Close 후 재생성(중복 창 방지).
- **Decision points**:
  - **D-팝업수명**: 매 좌클릭마다 새 `FolderListPopupWindow` 생성(GroupPopupWindow와 동일 패턴). 단, 직전 팝업이 살아있으면 닫는다(`_folderPopup?.Close()`). AppGroup식 사전생성+재사용은 도입하지 않음(WinUI 패턴 일관성·복잡도↓).
  - **D-더블클릭(확정)**: `WM_LBUTTONUP`만 폴더 팝업 트리거, `WM_LBUTTONDBLCLK`는 무시. 더블클릭으로 메인 창 열기는 제거(Q1=B — 메인 창은 우클릭 "열기"만).
  - 좌클릭=폴더 팝업, 메인 창은 우클릭 "열기"만(Q1=B).
- **Edge cases**: 폴더 미등록 상태 좌클릭 → 빈 목록 팝업("등록된 폴더가 없습니다") 표시. 연속 좌클릭 → 이전 팝업 닫고 새로. 더블클릭 → 첫 LBUTTONUP로 팝업 1회만(DBLCLK 무시). 메인 창이 떠 있는 상태에서 좌클릭 → 폴더 팝업만 별도 표시(독립).
- **Acceptance**:
  - 트레이 좌클릭 → 폴더 목록 팝업(메인 창 안 열림).
  - 트레이 우클릭 "열기" → 메인 창 표시(기존 유지).
  - 트레이 우클릭 "종료" → 앱 종료(기존 유지).
  - 빌드 0/0. (실제 클릭 동작은 F5 수동.)
- **Halt Forecast**: `WM_LBUTTONDBLCLK`를 좌클릭과 동일 처리하면 더블클릭 시 팝업이 한 번 더 뜰 수 있음 → 직전 팝업 닫기 로직으로 흡수(중복 창 안 생김). 문제 지속 시 더블클릭은 무시(LBUTTONUP만 처리).

### T8 — FolderListPopupWindow (좌클릭 폴더 목록 팝업) (Type D)
- **Files**:
  - `src/WorkGroup.App/Views/FolderListPopupWindow.xaml` (신규)
  - `src/WorkGroup.App/Views/FolderListPopupWindow.xaml.cs` (신규)
- **변경**:
  - `GroupPopupWindow` 베이스 패턴 복제: Mica 배경, `ConfigurePresenter`(IsAlwaysOnTop, SetBorderAndTitleBar(true,false), IsShownInSwitchers=false), `ScreenMetricsProvider.Capture()`로 클릭 좌표 캡처, 화면 밖 측정 후 `RevealAtTaskbar`→`TaskbarPopupPositioner.Compute`로 작업표시줄 변 배치, `AdjustToContent` 높이 측정, `OnActivated` Deactivated 시 Close.
  - XAML: 헤더("폴더" + 톱니 버튼 → 메인 창 트레이 메뉴 탭 열기), 폴더 목록(`ListView`/`ItemsControl`). 설정 `FolderPopupSettings.ColumnCount==1`이면 가로 레이아웃(아이콘+이름), 2~5면 그리드 레이아웃(아이콘 위/이름 아래) — **1차 구현은 세로 목록(ColumnCount는 표시 폭에 반영)**; 그리드 분기는 D-레이아웃 참조.
  - 폴더 항목: 아이콘(셸 폴더 아이콘) + 이름. 클릭 → `IShellOpener.Open(path)` + Close. 마우스 호버(PointerEntered) → 200ms 타이머 후 `FolderContentsPopupWindow` 표시(T9).
  - 데이터: `IFolderShortcutRepository.LoadAllAsync` + `FolderPopupSettingsService.Read()`.
- **Decision points**:
  - **D-레이아웃(확정)**: 세로 목록(1열) + 그리드(2~5열) 둘 다 구현. `FolderPopupSettings.ColumnCount==1`이면 세로 목록(아이콘+이름 가로 배치), 2~5면 그리드(아이콘 위/이름 아래). AppGroup `BuildFolderUI`(L504-533) 분기 이식.
  - **D-팝업포커스(코드 수준 확정, B2 해소)**: GroupPopupWindow 베이스의 `OnActivated`는 Deactivated 시 **무조건 `Close()`**(GroupPopupWindow.xaml.cs:217)이므로, **두 새 팝업 창은 이 가드를 포함한 자체 `OnActivated`를 구현**한다(베이스 복제 + 가드 추가). 구체 메커니즘:
    1. 1차에 `private bool _childOpen;` 필드.
    2. 폴더 호버 타이머 Tick → 2차 팝업 **생성·`Activate()` 호출 직전에 `_childOpen = true`** set(자식 Activate가 부모 Deactivated를 유발하기 전에 플래그가 먼저 켜짐 — 동기 순서 보장).
    3. 1차 `OnActivated`: `if (e.WindowActivationState == Deactivated) { if (_childOpen) return; Close(); }` — 자식이 떠 있으면 닫지 않음.
    4. 2차(자식) `Closed` 이벤트 → 부모 콜백으로 `_childOpen = false`. 그 콜백에서 부모가 여전히 비활성(포그라운드 아님)이면 부모도 `Close()`(사용자가 팝업 밖을 클릭해 자식이 닫힌 경우 체인 전체 종료). 부모가 활성으로 돌아왔으면 유지(사용자가 부모로 마우스 복귀).
    5. 1차 `Closed` 시 살아있는 자식도 `Close()`(역방향 정리).
  - 호버 딜레이 200ms(AppGroup `HOVER_DELAY_MS`), `DispatcherTimer` one-shot.
- **Edge cases**: 폴더 0개 → "등록된 폴더가 없습니다" 텍스트. 화면 경계 → Positioner 클램프. 호버 중 빠르게 다른 폴더로 이동 → 타이머 재시작, 직전 2차 팝업 닫고(`_childOpen` 잠깐 false→재생성 시 true) 새 폴더로. 팝업 밖 클릭 → 1차·2차 모두 닫힘.
- **Acceptance**:
  - 좌클릭 시 등록 폴더가 작업표시줄 근처 팝업으로 표시(이미지2 좌측). ColumnCount 설정에 따라 세로/그리드.
  - 폴더 클릭 → 탐색기로 열림 + 팝업 닫힘. 톱니 → 메인 창 트레이 메뉴 탭 표시.
  - 폴더 호버 200ms 후 2차 팝업 표시 시 **1차가 닫히지 않음**, 팝업 밖 클릭 시 체인 전체 닫힘.
  - 빌드 0/0. (시각/동작 F5 수동.)
- **Halt Forecast**:
  - 2차 표시 시 1차가 닫히면 → `_childOpen` set이 자식 `Activate()` **이전**에 실행되는지 순서 확인(가드의 핵심). 여전히 닫히면 자식 표시를 부모가 `Activate()` 유지하도록 자식에 `IsAlwaysOnTop`만 두고 부모를 Activate 상태로 유지하는 방식 검토(단, 1차 안에서 처리).
  - 콘텐츠 높이 측정 타이밍은 GroupPopupWindow `AdjustToContent`/`RevealAtTaskbar`(L118-205) 그대로.

### T9 — FolderContentsPopupWindow (2차 폴더 내용 재귀 팝업) (Type D)
- **Files**:
  - `src/WorkGroup.App/Views/FolderContentsPopupWindow.xaml` (신규)
  - `src/WorkGroup.App/Views/FolderContentsPopupWindow.xaml.cs` (신규)
- **변경**:
  - 팝업 창 베이스 패턴 동일(Mica/presenter/측정). **포커스 닫힘은 T8 D-팝업포커스 가드를 재귀 적용** — 각 `FolderContentsPopupWindow`도 `_childOpen` 필드 + 부모 콜백을 가져, 자식(더 깊은 내용 팝업)이 열리면 자신을 닫지 않고, 자식 Closed 시 자신·조상으로 종료 전파. 즉 1차(`FolderListPopupWindow`)→2차→3차…가 하나의 체인으로 함께 닫힌다. 헤더(폴더 이름), 파일 섹션 + 폴더 섹션(`ItemsControl`). `IDirectoryBrowser.Browse(path, settings.ShowHiddenItems)`로 채움. 파일/폴더 아이콘은 `FolderIconLoader.LoadAsync(path)`(T5, 내부 `ShellIcon.OpenForPathAsync`) — 파일은 파일 아이콘, 폴더는 폴더 아이콘.
  - 파일 클릭 → `IShellOpener.Open(file)` + 전체 팝업 체인 닫기. 폴더 클릭/호버 → `_currentDepth < SubfolderDepth`면 자식 `FolderContentsPopupWindow` 재귀 표시(부모 왼쪽/오른쪽 배치, AppGroup `ShowChildFolderPopup` L777-845 이식), 최대 깊이 도달 시 클릭하면 탐색기로 열기.
  - 위치: 부모 팝업 기준 좌측 우선, 공간 없으면 우측. 모니터 경계 클램프(상단 100px 여백).
  - 깊이: `FolderPopupSettings.SubfolderDepth`(1~5). depth 1이면 2차 팝업 자체를 띄우지 않음(1차 폴더 클릭=탐색기 열기). depth≥2부터 내용 팝업.
- **Decision points**:
  - depth 처리: 1차 폴더 목록=depth 1. 그 폴더 호버 시 내용 팝업=depth 2. 설정 `SubfolderDepth`가 2 이상일 때만 내용 팝업, 그 안에서 또 폴더 호버는 depth 증가하며 `SubfolderDepth`까지.
  - 부모-자식 팝업 체인을 리스트/참조로 관리해 한 번에 닫기.
- **Edge cases**: 빈 폴더 → "폴더가 비어있습니다". 권한 없음 → "접근할 수 없습니다". 경로 없음(삭제됨) → "폴더를 찾을 수 없습니다". 깊은 중첩 → SubfolderDepth에서 멈춤(클릭 시 탐색기). 파일 많은 폴더 → 스크롤(최대 높이 제한, AppGroup MAX_HEIGHT).
- **Acceptance**:
  - 1차 팝업에서 폴더 호버 → 그 안의 파일/하위폴더가 2차 팝업으로 표시(이미지2 우측).
  - 파일 클릭 → 실행, 폴더 재귀(설정 깊이까지), 최대 깊이 폴더 클릭 → 탐색기.
  - 숨김 표시 설정 반영.
  - 포커스 체인 닫힘 정상.
  - 빌드 0/0. (F5 수동.)
- **Halt Forecast**:
  - 자식 팝업 위치가 부모와 겹치거나 화면 밖 → AppGroup `POPUP_OVERLAP=20`/`TOP_MARGIN=100` 상수와 클램프 로직 이식.
  - 포커스 전환 시 체인 전체가 깜빡이며 닫히면 → 부모/자식이 같은 "팝업 그룹"으로 동작하도록 Deactivated 시 "체인 내 다른 창이 활성"인지 확인 후에만 닫기.

### T10 — 폴더 팝업 설정 UI (Type C)
- **Files**:
  - `src/WorkGroup.App/Views/FolderPopupSettingsDialog.xaml` (신규) + `.xaml.cs`
  - (또는 `TrayMenuPage` 내 인라인 설정 영역 — **다이얼로그 방식으로 확정**)
- **변경**:
  - `ContentDialog`: 열 개수 `ComboBox`(1~5), 하위폴더 깊이 `ComboBox`(1~5), 숨김 파일/폴더 표시 `ToggleSwitch`. `FolderPopupSettingsService.Read()`로 초기화, 저장 시 `FolderPopupSettings.Create(...)`로 클램프 후 `Save`.
  - `TrayMenuPage`의 톱니 버튼(T6) → 이 다이얼로그 표시. `FolderListPopupWindow` 헤더 톱니(T8)는 메인 창 트레이 메뉴 탭으로 이동(설정은 거기서).
- **Decision points**:
  - 설정은 다이얼로그(AppGroup `StartMenuSettingsDialog`와 동일). `SettingsViewModel`의 `_suppress` 가드 패턴은 불요(다이얼로그는 열 때 1회 로드→저장 버튼). 단순 code-behind.
  - 값 검증/클램프는 도메인 `FolderPopupSettings.Create`.
- **Edge cases**: ComboBox 인덱스↔값(1~5) 매핑 주의(SelectedIndex 0=값1). 저장 후 다음 팝업부터 반영(이미 열린 팝업엔 영향 없음).
- **Acceptance**:
  - 톱니 → 설정 다이얼로그, 3개 항목 표시·현재값 로드.
  - 변경·저장 후 `FolderPopupSettingsService.Read()` 반영, 다음 좌클릭 팝업에 적용.
  - 빌드 0/0.
- **Halt Forecast**: ComboBox `SelectedIndex` 오프바이원 → 값 = index+1로 명시 변환.

### T11 — ServiceConfiguration DI 등록 + 통합 (Type C)
- **Files**:
  - `src/WorkGroup.App/ServiceConfiguration.cs` (수정)
- **변경**(L26-66 사이에 추가):
  - `services.AddSingleton<IFolderShortcutRepository>(sp => new JsonFolderShortcutRepository(WorkGroupPaths.FoldersConfigPath, sp.GetRequiredService<ILogger<JsonFolderShortcutRepository>>()));`
  - `services.AddSingleton<IDirectoryBrowser, DirectoryBrowser>();`
  - `services.AddSingleton<IShellOpener, ShellOpener>();`
  - `services.AddSingleton<Services.FolderPopupSettingsService>();`
  - `services.AddTransient<ViewModels.FolderShortcutsViewModel>();`
  - using 추가(`WorkGroup.Application.Folders`, `WorkGroup.Infrastructure.Folders`).
- **Decision points**: 저장소/브라우저/오프너/설정서비스 Singleton(상태 적고 공유 안전), ViewModel Transient(기존 VM과 동일).
- **Edge cases**: N/A.
- **Acceptance**: 앱 시작 시 DI 해석 성공(트레이 메뉴 탭 진입·좌클릭 팝업 동작). `dotnet build`(x64) + 전체 `dotnet test` 통과.
- **Halt Forecast**: 인터페이스/구현 네임스페이스 불일치 시 using 확인. 순환 의존 없음(단방향).

### T12 — 문서 갱신 (Type A)
- **Files**: `README.md`, `notes.md`
- **변경**:
  - `README.md`: 핵심 기능에 "폴더 바로가기(트레이 좌클릭 폴더 팝업)" 섹션 추가 — 트레이 메뉴 탭 폴더 관리, 좌클릭 폴더 목록/2차 내용 팝업, 설정(열 개수/깊이/숨김). 트레이 동작 변경(좌클릭=폴더 팝업, 메인 창=우클릭 "열기") 반영.
  - `notes.md`: `## 최근 변경` 최상단에 본 작업 내역 추가. 1개월 경과 항목 정리.
- **Acceptance**: 문서가 현재 기능과 일치, 존재하지 않는 기능 미기재.

## 의존 관계
- T1 → T2 → T3 → T4 (도메인 → 인터페이스 → 저장소 → 브라우저/ShellIcon 오버로드)
- T1·T4 → T5 (FolderPopupSettingsService는 도메인 값객체, FolderIconLoader는 T4의 `ShellIcon.OpenForPathAsync` 의존)
- T1~T5 → T6 (관리 화면은 저장소/설정/아이콘로더/도메인 필요)
- T6 → T7 (트레이 동작; 팝업은 등록된 폴더 표시)
- T7 → T8 → T9 (트레이 좌클릭 → 1차 팝업 → 2차 팝업)
- T5 → T8/T9/T10 (설정값 사용/편집)
- T1~T10 → T11 (DI 통합) → T12 (문서)
- **Phase A(T1~T6)**: 폴더 등록/관리 가능. **Phase B(T7~T11)**: 좌클릭 팝업 표시. T12 문서.

## 검증 방법
- 각 task: `dotnet build WorkGroup.slnx`(경고/에러 0). UI task는 `dotnet build src/WorkGroup.App/WorkGroup.App.csproj -p:Platform=x64`.
- 도메인/저장소/브라우저: `dotnet test WorkGroup.slnx`(Domain.Tests, Application.Tests).
- 트레이 좌/우클릭 동작, 팝업 표시/위치/호버/2차 팝업, 관리 화면 추가·편집·삭제·검색, 설정 반영 → **F5 MSIX GUI 수동 검증**(헤드리스 불가 항목 명시).

## 문서 갱신 (구현 완료 시)
- T12에서 `README.md`/`notes.md` 갱신.

## 진행 체크리스트
- [x] T1 Domain: FolderShortcut + FolderPopupSettings
- [x] T2 Application: 인터페이스(Repository/DirectoryBrowser/ShellOpener)
- [x] T3 Infrastructure: JsonFolderShortcutRepository + 경로
- [x] T4 Infrastructure: DirectoryBrowser + ShellOpener
- [x] T5 App: FolderPopupSettingsService + FolderIconLoader
- [x] T6 App: TrayMenuPage 관리 화면 + VM + FolderEditDialog
- [x] T7 TrayIconService 좌클릭 분리 + App.xaml.cs
- [x] T8 FolderListPopupWindow
- [x] T9 FolderContentsPopupWindow
- [x] T10 폴더 팝업 설정 UI
- [x] T11 ServiceConfiguration DI 통합
- [x] T12 문서 갱신

## 자율 실행 준비도 자문
- 다른 사람이 추가 질문 없이 끝낼 수 있는가? → 예(레이어/파일/시그니처/엣지/Halt 명시, 재사용 인프라 파일·라인 인용).
- 구현 중 결정 분기가 남아있는가? → 아니오(팝업 수명/포커스/레이아웃/설정 저장 위치 모두 확정).
- 검증 가능한 acceptance가 각 task에 있는가? → 예.

## Next Steps
- 권장 다음 액션: **F5(Visual Studio MSIX 배포)로 GUI 수동 검증** 후 사용자 승인 시 머지/PR. 헤드리스 불가 항목이라 자동 검증 완료(빌드 0/0, 테스트 106/106) 뒤 남은 단계는 수동.
- GUI 수동 검증 체크리스트:
  1. 트레이 **좌클릭** → 등록 폴더 목록 팝업(작업 표시줄 변), **더블클릭 시 팝업 1회만**(깜빡임 없음).
  2. 트레이 **우클릭 메뉴 "열기"** → 메인 창, "종료" → 앱 종료.
  3. "트레이 메뉴" 탭: 폴더 추가(찾아보기)/검색/수정/위치 열기/삭제, "N개 폴더" 카운트.
  4. 폴더 호버 → 2차 내용 팝업(파일/하위폴더), 파일 클릭 실행, 하위 폴더 재귀(설정 깊이), **팝업 밖 클릭 시 체인 즉시 전체 닫힘**(m1).
  5. 톱니 → 설정 다이얼로그(열/깊이/숨김) 저장 후 다음 좌클릭 팝업 반영. 톱니(팝업 헤더) → 메인 트레이 메뉴 탭.
- Suggested skills: 공식 /code-review(머지 전 diff 리뷰), 공식 /security-review(해당 시).

## Follow-ups (plan-completion-reviewer MINOR)
- m1: 2차+ 팝업 체인 종료 시 드문 깜빡임 여지(WinUI Activated 순서 의존) — GUI 수동 검증으로 확인. 문제 시 자식 Closed에서 부모 재-Activate 보정 검토.
- m2: 트레이 아이콘 파일 로드(LoadTrayIcon) 변경은 이전 세션 잔재 — notes 2026-06-03 "트레이 아이콘 AppIcon.ico 적용" 항목에 이미 기록됨(추가 조치 불필요).
- m3(선택): FolderEditDialog 편집 모드 "이름만 변경" 저장소 단위 테스트 추가.

## Progress Log
- T1-T2 완료: Domain(FolderShortcut/FolderPopupSettings) + Application 인터페이스(IFolderShortcutRepository/IDirectoryBrowser/IShellOpener). 빌드 OK, Domain 테스트 23/23. 신규 파일만(호출자 0).
- T3-T4 완료: JsonFolderShortcutRepository(folders.json, 원자적 쓰기/백업) + WorkGroupPaths.FoldersConfigPath + DirectoryBrowser + ShellOpener + ShellIcon.OpenForPathAsync(경로 오버로드, 기존 OpenForAppAsync 무변경). 솔루션 빌드 0/0, Application 테스트 83/83. ShellIcon 기존 소비자 영향 0(빌드 확인).
- T5-T6 완료: FolderPopupSettingsService(LocalSettings) + FolderIconLoader + FolderShortcutsViewModel/Item + TrayMenuPage(폴더 관리 화면) + FolderEditDialog. App 빌드 x64 OK. OnSettingsClick은 T10 stub.
- T7 완료: TrayIconService LeftClickRequested 이벤트 분리(좌클릭=폴더팝업, DBLCLK 무시) + App.xaml.cs ShowFolderListPopup(T8 stub) 연결. OpenRequested는 우클릭 "열기"만 유지. 빌드 OK, caller 전수 확인.
- T8-T9 완료(함께 구현 — B2 포커스 가드 양방향 의존): FolderListPopupWindow(1차 폴더 목록, 세로/그리드 분기, 톱니→트레이메뉴 탭) + FolderContentsPopupWindow(2차 파일/하위폴더, 재귀 depth) + App.ShowFolderListPopup 본문/ShowTrayMenuFromPopup + MainShell.SelectTrayMenu + ChildPopupAnchor/CloseChainRequested. B2 가드: 자식 Activate 직전 _childOpen=true, OnActivated _isActive 추적+가드, 자식 Closed 콜백, OnClosed 체인 정리. 전체 빌드 0/0, 테스트 106/106. (팝업 실제 표시/위치/호버/포커스는 F5 GUI 수동.)
- T10-T11 완료: FolderPopupSettingsDialog(열/깊이/숨김, Create 클램프) + TrayMenuPage 톱니 연결 + ServiceConfiguration DI 등록(IFolderShortcutRepository/IDirectoryBrowser/IShellOpener/FolderPopupSettingsService/FolderShortcutsViewModel). 전체 빌드 0/0, 테스트 106/106. (DI 런타임 해석·설정 반영은 F5 GUI 수동.)
