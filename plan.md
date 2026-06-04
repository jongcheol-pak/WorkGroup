# Plan: 전체 다국어화(i18n) + 설정 언어 변경 기능

## Goal
앱의 모든 하드코딩된 사용자 표시 문구를 리소스(.resw)로 추출해 한국어/영어/일본어/중국어(간체) 4개 언어로 제공하고, 설정 화면에서 언어를 바꿀 수 있게 한다(기본값 "시스템 언어").

## Out of Scope
- **개발자 전용 문구**: `ArgumentException`/`throw` 생성자 가드 메시지("그룹 디렉터리가 비어 있습니다." 등), `ILogger` 로그 메시지 — 사용자 UI 노출 안 됨, 번역 불필요.
- **고유명사·수치**: `LicenseCatalog`의 라이브러리명/라이선스 종류명("MIT License" 등)/URL, 앱 버전 문자열.
- 한국어 외 언어 번역의 원어민 감수(모델 번역으로 1차 제공, 추후 개선 가능).
- 언어별 폰트/RTL 레이아웃 조정(대상 4개 언어 모두 LTR, 기존 폰트로 충분).

## Investigation Log
- `README.md`/`AGENTS.md` 읽음 → DDD 4레이어(App→Infrastructure→Application→Domain), 빌드 `dotnet build WorkGroup.slnx`, 테스트 `dotnet test WorkGroup.slnx`, 헤드리스에서 GUI 관찰 불가.
- `Glob src/WorkGroup.App/Strings/**` → **결과 없음**: 기존 로컬라이제이션 인프라(.resw/Strings 폴더) 전무. 모든 문구가 한국어 하드코딩.
- `ThemeService.cs` 읽음 → 설정 영속 패턴 확인(LocalSettings "AppTheme" 키, Read/Set/Save). LanguageService의 참고 모델.
- `App.xaml.cs` 읽음 → 진입점 `App()` 생성자에서 `ServiceConfiguration.Build()`. `OnLaunched`에서 창 생성. 언어 적용은 창 생성 이전(생성자)에 해야 함. `AppInstance` 재시작/리다이렉트 패턴 존재.
- `ServiceConfiguration.cs` 읽음 → 모든 인프라/서비스/ViewModel DI 등록 위치 확인. 6개 인프라 서비스 생성자에 ILocalizer 주입 시 여기 갱신.
- `WorkGroup.App.csproj` 읽음 → WinUI3/MSIX. `.resw` 자동 포함 보장 위해 `PRIResource` 명시 + 기본 언어 지정 필요.
- `Package.appxmanifest` 읽음 → `DisplayName`/`uap:VisualElements DisplayName·Description`/`uap5:StartupTask DisplayName` 모두 "작업 관리" 하드코딩. `<Resources><Resource Language="x-generate"/></Resources>` 존재.
- Explore 에이전트 전수조사 → App 레이어 사용자 표시 문구 약 95개(XAML 8파일 + C# 5파일). 동적 포맷 문자열 별도 식별.
- `grep "[가-힣]" src/WorkGroup.Infrastructure` → **인벤토리 누락분 발견**: 사용자에게 반환되는 `Result.Fail` 한국어 문구가 6개 서비스에 분포(ShortcutService, AppLauncher, InstalledAppInventory, IconService, JsonGroupRepository, JsonFolderShortcutRepository). 생성자 가드 `throw`는 개발자용으로 제외.
- `grep "[가-힣]" src/WorkGroup.Application` → 사용자 표시 `Result.Fail` 문구 **없음**(GroupAppService는 `throw`(가드)와 로그만). Application 레이어 번역 대상 0.
- 인프라 생성자 시그니처 실측: `AppLauncher(ILogger<AppLauncher>? logger = null)`, `ShortcutService(string groupsDirectory, string aliasExePath, IShortcutWriter? writer = null, ILogger? logger = null)`, `IconService()`/`InstalledAppInventory()`/`JsonGroupRepository(dir, logger?)`/`JsonFolderShortcutRepository(path, logger?)` — **모두 선택 인자+폴백 패턴**(`ILogger? = null`, `IShortcutWriter? = null`, `NullLogger`/기본 구현). → ILocalizer도 동일하게 **선택 인자(`ILocalizer? = null`, 마지막 위치)+폴백**으로 추가하면 기존 호출부 무수정(D5).
- **인프라 단위 테스트 존재(실측)**: `IconServiceTests`(`new IconService()` ×5), `InstalledAppInventoryTests`(`new InstalledAppInventory()` ×5), `ShortcutServiceTests`(`new ShortcutService(_dir,_alias,...)`), `JsonGroupRepositoryTests`(`new(_dir)`), `JsonFolderShortcutRepositoryTests`, `GroupAppServiceTests`, `AppLauncherTests`(`new AppLauncher()`). 선택 인자 방식이면 **컴파일 무영향**.
- 에러 **문구 텍스트**를 단언하는 테스트는 `JsonFolderShortcutRepositoryTests` **L72/L96 두 곳뿐**(`Assert.Equal("이미 등록된 폴더입니다.", dup.Error)`, `Assert.Equal("수정할 폴더를 찾을 수 없습니다.", result.Error)`). 나머지 인프라 테스트는 `IsFailure`/`IsSuccess`만 단언(에러 텍스트 비검증) → 로컬라이즈해도 불변. `AppLauncherTests` 무수정(`new AppLauncher()` 유효).
- `tests/WorkGroup.Application.Tests.csproj` 읽음 → TFM `net10.0-windows10.0.19041.0`, Infrastructure 참조.
- `GroupPopupWindow.xaml` 읽음 → 우클릭 컨텍스트 MenuFlyout("열기"/"관리자 권한으로 실행"/"그룹 수정") 확인.
- 마크업 메커니즘(D1) PoC 미수행 → T1에서 단일 속성+ToolTip에 `{loc:Localize}` 적용 후 빌드 성공을 **선행 게이트**로 검증(실패 시 x:Bind 정적 메서드 폴백).

## Risks & Unknowns
| 위험 | 영향 | 완화책 |
|---|---|---|
| WinUI3 `x:Uid`의 `PrimaryLanguageOverride` 반영 여부가 헤드리스로 검증 불가 | 언어 전환이 XAML에 미반영 가능 | **x:Uid 미사용**. 자체 `LocalizationService`(MRT Core `ResourceManager`+직접 구성한 `ResourceContext`)로 언어 한정자 명시 제어(D1). XAML 내부 컨텍스트 의존 제거. |
| 커스텀 MarkupExtension는 런타임 평가라 누락 키가 빌드로 안 잡힘 | 일부 문구 공백(런타임), 헤드리스 검증 곤란 | (1) ko-KR 소스 언어 폴백. (2) `Common_*` 공통 키로 중복 축소. (3) **resw 4개 언어 키 패리티+빈 값 검사 단위 테스트**(T10, 순수 XML)로 누락/미번역 자동 검출. |
| 인프라 6개 서비스 생성자에 ILocalizer 추가 | 회귀·테스트 영향 | **선택 인자(`ILocalizer? = null`)+폴백**으로 기존 컨벤션(`ILogger?`) 답습 → 기존 호출부 무수정. 텍스트 단언 테스트는 JsonFolderShortcutRepositoryTests 2곳뿐(키로 갱신). 변경 후 `dotnet build`/`test` 통과 의무. |
| MSIX 매니페스트 `ms-resource`의 `resources.pri` 맵 경로 오류 | 시작 메뉴 표시 이름이 공백/패키지명으로 표시 | `ms-resource:///Resources/App_DisplayName` 전체 경로 형식 사용. 빌드/패키지 성공으로 1차 검증(런타임 셸 표시는 수동 배포 확인 — Verification 명시). |
| 재시작(AppInstance.Restart)과 트레이/시작작업 경로 상호작용 | 재시작 시 예기치 않은 창 표시 | `Restart("")` 무인자=일반 실행 경로(메인 창 표시). 일반 실행과 동일 동작 → 회귀 위험 낮음. |
| 번역 품질(en/ja/zh) | 어색한 번역 | 용어집(Glossary) 고정으로 일관성. ko는 기존 문구(소스 진실). |

## Impact Analysis

### 4-A. 심볼/타입 추적 결과
| 심볼/대상 | 영향 받는 파일 | 영향 종류 |
|---|---|---|
| (신규) `LocalizationService` | `src/WorkGroup.App/Services/LocalizationService.cs` | 신규. MRT Core 래퍼 + static `Current` 접근자, `ILocalizer` 구현 |
| (신규) `LocalizeExtension` 마크업 | `src/WorkGroup.App/Markup/LocalizeExtension.cs` | 신규. XAML `{loc:Localize Key=...}` |
| (신규) `LanguageService` | `src/WorkGroup.App/Services/LanguageService.cs` | 신규. 언어 영속/적용/재시작 |
| (신규) `ILocalizer` | `src/WorkGroup.Application/Localization/ILocalizer.cs` | 신규. Application 추상화 |
| DI 등록 | `src/WorkGroup.App/ServiceConfiguration.cs` | `LocalizationService`(+`ILocalizer`)·`LanguageService` 등록 / 6개 인프라 서비스 생성자에 `ILocalizer` 인자 추가 |
| 앱 시작 언어 적용 | `src/WorkGroup.App/App.xaml.cs` | 생성자에서 `LanguageService.ApplyOnStartup()` 호출(창 생성 이전) |
| `ShortcutService(groupsDir, aliasExePath, writer?, logger?)` | `src/WorkGroup.Infrastructure/Shortcuts/ShortcutService.cs`, `ServiceConfiguration.cs` | 생성자 **마지막에 `ILocalizer? = null`** 추가(기존 `writer?`/`logger?` 뒤). `Result.Fail` 3 + `.lnk` 설명 접미사 로컬라이즈 |
| `AppLauncher(logger?)` | `src/WorkGroup.Infrastructure/Launch/AppLauncher.cs`, `ServiceConfiguration.cs` | 생성자 마지막에 `ILocalizer? = null` 추가. `Result.Fail` 3 로컬라이즈. **테스트 무수정**(`new AppLauncher()` 유효) |
| `InstalledAppInventory()` | `src/WorkGroup.Infrastructure/Inventory/InstalledAppInventory.cs`, `ServiceConfiguration.cs` | 생성자에 `ILocalizer? = null` 추가. `AddByPath` `Result.Fail` 3 로컬라이즈. 테스트 무수정 |
| `IconService()` | `src/WorkGroup.Infrastructure/Icons/IconService.cs`, `ServiceConfiguration.cs` | 생성자에 `ILocalizer? = null` 추가. `Result.Fail` 2 로컬라이즈. 테스트 무수정 |
| `JsonGroupRepository(dir, logger?)` | `src/WorkGroup.Infrastructure/Persistence/JsonGroupRepository.cs`, `ServiceConfiguration.cs` | 생성자 마지막에 `ILocalizer? = null` 추가. 저장 실패 `Result.Fail` 1 로컬라이즈. 테스트 무수정 |
| `JsonFolderShortcutRepository(path, logger?)` | `src/WorkGroup.Infrastructure/Persistence/JsonFolderShortcutRepository.cs`, `ServiceConfiguration.cs`, `tests/.../JsonFolderShortcutRepositoryTests.cs` | 생성자 마지막에 `ILocalizer? = null` 추가. `Result.Fail` 3 로컬라이즈. **테스트 L72/L96 단언을 키 기반으로 갱신** |
| `AboutViewModel.AppName` | `src/WorkGroup.App/ViewModels/AboutViewModel.cs` | `"작업 관리"`→`LocalizationService.Current.Get("App_DisplayName")` |
| `SettingsViewModel` | `src/WorkGroup.App/ViewModels/SettingsViewModel.cs`, `ServiceConfiguration.cs` | 생성자에 `LanguageService`+`LocalizationService` 주입(현재 `(StartupService, ThemeService)` → 확장). VM은 DI로만 생성(`App.Services.GetRequiredService`)되어 외부 직접 `new` 없음 → 안전 |
| 기타 ViewModel(`GroupEditViewModel`/`WorkGroupsViewModel`/`FolderShortcutsViewModel`/`AboutViewModel`) | 각 `ViewModels/*.cs`, `ServiceConfiguration.cs` | 생성자에 `LocalizationService` 주입(DI 등록 transient). AboutViewModel은 현재 무인자 → 인자 추가 |
| XAML 11개 파일 | (T4~T6 Files) | 하드코딩→`{loc:Localize}`, `xmlns:loc` 추가 |
| C# 코드비하인드/VM | (T3/T7 Files) | 하드코딩→`LocalizationService`/`ILocalizer` 호출 |
| 매니페스트 | `src/WorkGroup.App/Package.appxmanifest` | `ms-resource:///Resources/App_DisplayName`·`App_Description` 참조 |
| csproj | `src/WorkGroup.App/WorkGroup.App.csproj` | `PRIResource` 포함, 기본 언어 `ko-KR` |

### 4-B. 계약·직렬화 변경
- **인프라 6개 서비스 생성자에 `ILocalizer? = null` 선택 인자 추가**(맨 뒤). 기존 `ILogger?`/`IShortcutWriter?` 선택 인자 컨벤션과 동일 → 기존 호출부/테스트 컴파일 무영향. DI는 실제 `ILocalizer` 주입, null이면 키 폴백.
- **ViewModel 생성자 확장**: SettingsViewModel(+LanguageService/LocalizationService), 기타 VM(+LocalizationService). 모두 DI 전용 생성 → 외부 직접 `new` 없음(코드 확인). AboutViewModel 무인자→인자 추가.
- 신규 LocalSettings 키 `"AppLanguage"`(기존 키와 충돌 없음, 마이그레이션 불필요 — 키 없으면 "System" 폴백).
- `Result<T>`/`Result` 형식 불변(에러 문자열 값만 로컬라이즈).

### 4-C. 테스트 파일
- `tests/WorkGroup.Application.Tests/JsonFolderShortcutRepositoryTests.cs` — L72/L96이 에러 **문구 텍스트**를 단언 → ILocalizer 키 기반 변경에 맞춰 **단언을 키(`Infra_Folder_Duplicate`/`Infra_Folder_NotFound`)로 갱신**(null 폴백이 키 반환). 또는 stub ILocalizer 주입해 텍스트 단언 유지.
- 그 외 인프라 테스트(`IconServiceTests`/`InstalledAppInventoryTests`/`ShortcutServiceTests`/`JsonGroupRepositoryTests`/`GroupAppServiceTests`/`AppLauncherTests`) — `IsFailure`/`IsSuccess`/`ArgumentException`만 단언, 선택 인자 추가로 **무영향**(실측).
- (신규) `tests/WorkGroup.Application.Tests/ResourceParityTests.cs` — 4개 resw 키 패리티/빈 값 검사(순수 XML, 리포지토리 루트 상대 탐색).

### Verified by
- `grep "[가-힣]" src/WorkGroup.Infrastructure` → 사용자 표시 `Result.Fail`(리터럴 7 + 보간 2) + `.lnk` 설명 접미사 1건 식별. 서비스별 분포는 4-A 표 참조(모두 반영). 가드 `throw`/로그 제외 분류.
- `grep "[가-힣]" src/WorkGroup.Application` → 사용자 표시 문구 0건 확인.
- `grep "new (ShortcutService|InstalledAppInventory|IconService|JsonGroupRepository|JsonFolderShortcutRepository|AppLauncher|GroupAppService)\(" tests/` → 인프라 직접 `new` 호출처 전수 식별(IconService ×5, InstalledAppInventory ×5, ShortcutService, JsonGroupRepository, AppLauncher). 모두 선택 인자 방식이라 무영향, 에러 텍스트 단언은 JsonFolderShortcutRepositoryTests 2곳뿐.
- 인프라 생성자 시그니처 4개 파일 직접 Read로 선택 인자 패턴 확인.

## Decisions

### D1. XAML 로컬라이즈 메커니즘
- **Options**: A) `x:Uid`+.resw(MS 표준) / B) 커스텀 `{loc:Localize}` 마크업 익스텐션 + MRT Core `ResourceManager`(자체 `ResourceContext`) / C) x:Bind 헬퍼
- **Chosen**: B
- **Rationale**: 언어 한정자를 우리가 명시 설정한 `ResourceContext`로 제어 → XAML 내부 컨텍스트가 `PrimaryLanguageOverride`를 반영하는지에 의존하지 않음(헤드리스 검증 불가 리스크 제거). ToolTip/Header/Description/Content 등 모든 문자열 속성에 균일 적용. 재시작 전환(D2)이라 마크업 1회 평가가 정합.
- **Fallback (검증 실패 시)**: T1 PoC(단일 속성+ToolTip에 마크업 적용 후 빌드)에서 `Microsoft.UI.Xaml.Markup.MarkupExtension` 방식이 문제면 **x:Bind 정적 메서드 바인딩**(`Text="{x:Bind loc:L.Get('Settings_Title')}"`, 문자열 리터럴 인자)으로 전환 — 컴파일 타임 검증 가능. 둘 다 재시작 모델과 호환.
- **Source**: 사용자 결정(재시작) + WinUI3 MRT Core 문서 API. **단, 마크업 익스텐션의 WinUI3 동작은 T1 PoC 게이트에서 빌드로 1차 확인(미검증 전제이므로 PoC 선행 의무).**

### D2. 언어 전환 적용 방식
- **Chosen**: 앱 재시작(`Microsoft.Windows.AppLifecycle.AppInstance.Restart("")`).
- **Rationale**: 헤드리스 GUI 검증 불가 환경에서 가장 확실(시작 시 언어 적용 후 전체 UI 재구성). 타이틀바/트레이/팝업 일괄 반영.
- **Source**: 사용자 결정.

### D3. 영속/적용
- **Chosen**: `LanguageService` — LocalSettings `"AppLanguage"`에 선택값("System"|"ko-KR"|"en-US"|"ja-JP"|"zh-Hans") 저장. 시작 시 `ApplyOnStartup()`이 (1) `Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride`(System이면 `""`), (2) `LocalizationService`의 `ResourceContext` Language 한정자 설정.
- **Rationale**: `ThemeService`와 동일 패턴(일관성). PrimaryLanguageOverride는 매니페스트/셸 표시(ms-resource)용, LocalizationService 컨텍스트는 인앱 문구용으로 역할 분리.
- **Source**: 코드 확인(ThemeService.cs).

### D4. 지원 언어
- **Chosen**: ko-KR(기본/소스), en-US, ja-JP, zh-Hans(간체).
- **Source**: 사용자 결정(중국어=간체).

### D5. Infrastructure 에러 메시지 로컬라이즈
- **Chosen**: Application에 `ILocalizer` 인터페이스(`string Get(string key)`, `string Get(string key, params object[] args)`) 정의, App의 `LocalizationService`가 구현, 6개 인프라 서비스 생성자 **맨 뒤에 `ILocalizer? localizer = null` 선택 인자** 추가.
- **null 폴백**: 인자 null이면 내부 `NullLocalizer`(키 문자열을 그대로 반환) 사용 — 기존 `ILogger? → NullLogger` 패턴과 동형. 프로덕션은 DI가 항상 실제 구현 주입하므로 사용자에게 키 노출 없음. 테스트(null 경로)에서는 `.Error == 키`.
- **Rationale**: DDD 준수(Infra→Application 인터페이스 의존, 허용 방향). 선택 인자라 기존 호출부/테스트 컴파일 무영향(4-B/4-C). 에러 텍스트 단언 테스트(JsonFolderShortcutRepositoryTests 2곳)만 키로 갱신.
- **Source**: 사용자 결정 + 코드 컨벤션 실측(ILogger?/IShortcutWriter? 선택 인자).

### D5b. 로컬라이즈 접근 경로(컨텍스트별)
- **ViewModel**(DI 생성): `LocalizationService` 생성자 주입(SettingsViewModel은 +`LanguageService`). DDD-clean, 테스트 가능.
- **코드비하인드 View / TrayIconService / 마크업 익스텐션**(DI 비대상): static `LocalizationService.Current`(App 생성자에서 DI 인스턴스로 설정). 코드비하인드는 기존에도 `App.Services.GetRequiredService` 사용 → static `Current`도 동일 인스턴스.
- **Infrastructure**: `ILocalizer` 선택 인자(D5).
- **Rationale**: 마크업/static-접근 불가피한 곳만 static, 나머지는 주입으로 일관. 혼용 기준 명시(리뷰 M3 해소).
- **Source**: 코드 확인(SettingsPage.xaml.cs의 GetRequiredService 패턴).

### D6. 앱 표시 이름 번역값
- **Chosen**: ko=작업 관리 / en=WorkGroup / ja=ワークグループ / zh=工作组.
- **Source**: 사용자 결정.

### D7. 매니페스트 다국어화
- **Chosen**: `DisplayName`/`VisualElements DisplayName`·`Description`/`StartupTask DisplayName`을 `ms-resource:///Resources/App_DisplayName`·`App_Description`로. 키는 4개 resw에 정의.
- **Source**: 사용자 결정.

### D8. 리소스 키 네이밍
- **Chosen**: `Screen_Element` PascalCase+underscore. 예: `Settings_Title`, `WorkGroups_AddTooltip`, `App_DisplayName`, `Infra_Folder_Duplicate`. 공통 반복 문구(확인/취소/삭제/저장)는 `Common_*`로 단일화.
- **Rationale**: 마크업 익스텐션 키는 자유 문자열. 화면 prefix로 가독성·충돌 방지, Common으로 중복 축소(YAGNI — 3회+ 반복만 공통).
- **Source**: 코드 컨벤션.

### D9. 동적/포맷 문자열
- **Chosen**: resw 값에 `{0}` 자리표시자 포함, C#에서 `string.Format(loc.Get(key), args)`. 예: `WorkGroups_DeleteConfirm`="'{0}' 그룹을 삭제할까요? 작업 표시줄 핀(.lnk)도 제거됩니다.".
- **Source**: 표준 .NET 패턴.

### D10. 언어 선택 ComboBox 표시
- **Chosen**: "시스템 언어" 항목만 로컬라이즈(`Settings_Language_System`), 나머지 4개는 고정 endonym("한국어"/"English"/"日本語"/"中文(简体)").
- **Rationale**: 언어명은 자기 언어 표기가 국제 관례.
- **Source**: i18n 관례.

### D11. 검증용 키 패리티 테스트
- **Chosen**: `Application.Tests`(net10.0-windows)에 순수 XML 파싱 테스트 — 4개 resw 키 집합 동일성 + 값 비어있지 않음 검사. resw 경로는 실행 디렉터리에서 상위로 올라가 `src/WorkGroup.App/Strings` 탐색.
- **Rationale**: 마크업 런타임 평가의 누락/미번역을 헤드리스 자동 검출.
- **Source**: Risks 완화.

## Tasks

- [x] T1. 리소스 인프라 골격 + 마크업 PoC 게이트(LocalizationService + 마크업 + ILocalizer + resw 4개 + csproj)
  - **Type**: D
  - **Acceptance**: `dotnet build WorkGroup.slnx` 성공. **PoC 게이트**: SettingsPage 등 1개 요소의 `Text`와 1개 `ToolTipService.ToolTip`에 `{loc:Localize Key=...}` 적용 후 빌드 그린(마크업 익스텐션이 XAML 컴파일러에 인식됨 확인). 빌드 실패 시 D1 Fallback(x:Bind 정적 메서드)으로 전환. `LocalizationService.Current.Get("App_DisplayName")`가 기본 언어 값 반환. 4개 resw가 `App_DisplayName`/`App_Description`/`Settings_Language_*` + PoC 키 포함.
  - **Files**:
    - 주: `src/WorkGroup.App/Services/LocalizationService.cs`(신규, `ILocalizer` 구현 + static `Current`), `src/WorkGroup.App/Markup/LocalizeExtension.cs`(신규)
    - 주: `src/WorkGroup.Application/Localization/ILocalizer.cs`(신규), `src/WorkGroup.Application/Localization/NullLocalizer.cs`(신규, 키 반환 폴백)
    - 주: `src/WorkGroup.App/Strings/ko-KR/Resources.resw`·`en-US/`·`ja-JP/`·`zh-Hans/Resources.resw`(신규 4개)
    - 동반: `src/WorkGroup.App/WorkGroup.App.csproj`(`<DefaultLanguage>ko-KR</DefaultLanguage>`만 추가 — `.resw`는 .NET SDK가 PRIResource로 **자동 포함**하므로 명시 include 시 NETSDK1022 중복 오류 발생, 명시하지 않음), `src/WorkGroup.App/ServiceConfiguration.cs`(LocalizationService를 자신+`ILocalizer`로 등록), `src/WorkGroup.App/App.xaml.cs`(생성자에서 `LocalizationService.Current` 설정 — `InitializeComponent` 이전)
  - **Edge Cases**:
    - 키 없음/조회 실패 → `Get`은 키 문자열 자체 반환(크래시 금지). 빈/null 키 → 빈 문자열.
    - 비패키지 실행으로 ResourceManager 초기화 실패 → try/catch로 키 자체 반환(폴백).
  - **Halt Forecast**:
    - "마크업 익스텐션이 WinUI3에서 동작?" → **PoC 게이트(Acceptance)**가 빌드로 1차 판정, 실패 시 x:Bind 폴백(D1).
    - "MRT Core API 시그니처?" → `Microsoft.Windows.ApplicationModel.Resources.ResourceManager` + `MainResourceMap.TryGetValue`/`GetValue(key, context)`; 실패 시 `ResourceLoader` 기본 맵 폴백(D1).
    - ".resw 자동 포함?" → csproj `PRIResource` 명시(Files).
  - **Depends on**: -

- [x] T2. LanguageService(영속/적용/재시작) + 앱 시작 시 언어 적용
  - **Type**: D
  - **Acceptance**: 빌드 성공. 저장값↔태그 매핑("System"→`""`, 기타→BCP-47) 코드 검토 확인. App 생성자에서 창 생성 이전에 `ApplyOnStartup()` 호출.
  - **Files**:
    - 주: `src/WorkGroup.App/Services/LanguageService.cs`(신규)
    - 동반: `src/WorkGroup.App/App.xaml.cs`(생성자 적용), `src/WorkGroup.App/ServiceConfiguration.cs`(등록)
  - **Edge Cases**:
    - LocalSettings 접근 실패(비패키지) → "System" 폴백, 예외 삼킴(ThemeService 패턴).
    - 저장된 태그가 미지원 값 → "System" 정규화.
    - 재시작 호출 실패(비패키지) → 예외 삼키고 저장만(다음 실행 반영).
  - **Halt Forecast**:
    - "재시작 API?" → `AppInstance.Restart("")`(D2).
    - "적용 시점?" → App 생성자, `Build()` 직후·`OnLaunched` 이전(Investigation).
  - **Depends on**: T1

- [x] T3. 설정 화면 언어 변경 UI + ViewModel 전환 로직
  - **Type**: C
  - **Acceptance**: 빌드 성공. 설정 페이지에 "언어" `SettingsCard`+ComboBox(시스템 언어/한국어/English/日本語/中文(简体)) 추가. 항목 선택 시 확인 다이얼로그 후 저장+재시작 경로 호출(코드 검토). 진입 시 현재 선택값 로드.
  - **Files**:
    - 주: `src/WorkGroup.App/Views/SettingsPage.xaml`, `src/WorkGroup.App/ViewModels/SettingsViewModel.cs`(생성자에 `LanguageService`+`LocalizationService` 주입)
    - 동반: `src/WorkGroup.App/Strings/*/Resources.resw`(언어 카드 키), `src/WorkGroup.App/Views/SettingsPage.xaml.cs`(필요 시 다이얼로그 호스팅), `src/WorkGroup.App/ServiceConfiguration.cs`(SettingsViewModel 등록은 기존 transient 유지 — DI가 신규 의존성 자동 해석)
  - **Edge Cases**:
    - 초기 로드 중 SelectedIndex 오발동 → 기존 `_suppress` 패턴 재사용(SettingsViewModel M3).
    - 현재값과 동일 언어 재선택 → 재시작 생략.
    - 재시작 확인 취소 → 선택을 이전 값으로 되돌림(`_suppress` 가드로 핸들러 재진입 차단).
  - **Halt Forecast**:
    - "재시작 확인 UX?" → ContentDialog "변경하려면 앱을 다시 시작합니다" 확인/취소(Edge Cases).
    - "테마/자동시작 핸들러 간섭?" → 동일 `_suppress` 게이트 공유.
  - **Depends on**: T2

- [x] T4. XAML 로컬라이즈 배치 A — MainShell / SettingsPage(잔여) / AboutPage
  - **Type**: C
  - **Acceptance**: 빌드 성공. 세 파일 하드코딩 표시 문구 모두 `{loc:Localize Key=...}` 치환, `xmlns:loc` 선언. 키 4개 resw 존재(T10 검증).
  - **Files**:
    - 주: `src/WorkGroup.App/Views/MainShell.xaml`(앱 타이틀/네비 4), `Views/SettingsPage.xaml`(헤더/섹션/카드/토글/테마 ComboBoxItem), `Views/AboutPage.xaml`(헤더/섹션)
    - 동반: `src/WorkGroup.App/Strings/*/Resources.resw`
  - **Edge Cases**:
    - 테마 ComboBox `<x:String>`→`<ComboBoxItem Content="{loc:Localize ...}"/>` 변환 시 `SelectedIndex` 바인딩 유지(인덱스 순서 불변).
  - **Halt Forecast**:
    - "ComboBoxItem 변환이 SelectedIndex 깨뜨림?" → 항목 순서/개수 보존으로 인덱스 동일.
  - **Depends on**: T1

- [x] T5. XAML 배치 B — WorkGroupsPage / TrayMenuPage / GroupEditDialog
  - **Type**: C
  - **Acceptance**: 빌드 성공. 세 파일 표시 문구(헤더/부제/플레이스홀더/툴팁/빈상태/버튼/레이블/InfoBar/MenuFlyout) 전부 `{loc:Localize}` 치환. 키 4개 언어 존재.
  - **Files**:
    - 주: `src/WorkGroup.App/Views/WorkGroupsPage.xaml`, `Views/TrayMenuPage.xaml`, `Views/GroupEditDialog.xaml`
    - 동반: `src/WorkGroup.App/Strings/*/Resources.resw`
  - **Edge Cases**:
    - ToolTipService.ToolTip에 마크업 직접 적용 가능 여부 → D1 균일 처리(불가 시 `<ToolTip Content="{loc:Localize ...}"/>` 중첩 폴백, 1차는 직접 시도).
  - **Halt Forecast**:
    - "GroupEditDialog Title 동적(추가/수정)" → C#은 T7 처리(여기선 XAML만).
  - **Depends on**: T1

- [x] T6. XAML 배치 C — FolderEditDialog / FolderPopupSettingsDialog / FolderListPopupWindow / GroupPopupWindow
  - **Type**: C
  - **Acceptance**: 빌드 성공. 네 파일 표시 문구(레이블/버튼/플레이스홀더/툴팁/헤더/열·깊이 ComboBoxItem/컨텍스트 MenuFlyout 3항목) 전부 치환. 키 4개 언어 존재.
  - **Files**:
    - 주: `src/WorkGroup.App/Views/FolderEditDialog.xaml`, `Views/FolderPopupSettingsDialog.xaml`, `Views/FolderListPopupWindow.xaml`, `Views/GroupPopupWindow.xaml`
    - 동반: `src/WorkGroup.App/Strings/*/Resources.resw`
  - **Edge Cases**:
    - 열 개수/깊이 ComboBoxItem(`1열`~`5열`, `1단계`~`5단계`) → resw 5개씩 개별 키. 인덱스 바인딩 보존.
  - **Halt Forecast**:
    - "FolderEditDialog/GroupPopup Title 동적 텍스트" → C# T7 처리(XAML 기본값만 키화).
  - **Depends on**: T1

- [x] T7. C# 문자열 로컬라이즈 — App 레이어 ViewModel/코드비하인드/TrayIconService
  - **Type**: C
  - **Acceptance**: 빌드 성공. 아래 파일 사용자 표시 문구(상태/검증/다이얼로그 제목·본문·버튼/트레이 메뉴·툴팁/동적 포맷)가 `LocalizationService`/`ILocalizer` 호출로 치환. 동적 포맷은 `string.Format`+자리표시자 키.
  - **Files**:
    - 주: `src/WorkGroup.App/ViewModels/GroupEditViewModel.cs`, `ViewModels/WorkGroupsViewModel.cs`, `ViewModels/FolderShortcutsViewModel.cs`, `ViewModels/AboutViewModel.cs`(AppName)
    - 주: `src/WorkGroup.App/Views/WorkGroupsPage.xaml.cs`, `Views/TrayMenuPage.xaml.cs`, `Views/FolderEditDialog.xaml.cs`, `Views/FolderListPopupWindow.xaml.cs`, `Views/FolderContentsPopupWindow.xaml.cs`, `Views/GroupPopupWindow.xaml.cs`
    - 주: `src/WorkGroup.App/Services/TrayIconService.cs`(트레이 "열기"/"종료" + 툴팁 "작업 관리")
    - 동반: `src/WorkGroup.App/Strings/*/Resources.resw`
  - **Edge Cases**:
    - 동적 메시지 `{ex.Message}`(예: "불러오기 실패: {0}") → 자리표시자 1개 포맷.
    - `GroupPopupWindow` "{group.Name} (멤버 없음)" → 포맷 키.
    - 접근 경로(D5b): **ViewModel은 `LocalizationService` 생성자 주입**(ServiceConfiguration의 transient 등록은 DI가 신규 의존성 자동 해석), **코드비하인드 View/TrayIconService는 static `LocalizationService.Current`**(DI 비대상). AboutViewModel은 무인자→주입 인자 추가.
  - **Halt Forecast**:
    - "VM 의존성 주입 vs static?" → D5b 확정: VM 주입, 코드비하인드/Tray static.
    - "TrayIconService(Win32, `new`로 생성) 접근?" → `LocalizationService.Current.Get`(static, App 생성자에서 설정 완료).
  - **Depends on**: T1, T2

- [x] T8. Infrastructure ILocalizer 주입 + 에러 메시지 로컬라이즈
  - **Type**: D
  - **Acceptance**: 빌드 성공. `dotnet test WorkGroup.slnx` 통과. 6개 서비스 생성자 **맨 뒤에 `ILocalizer? = null` 선택 인자** 추가(null→`NullLocalizer`), 사용자 표시 `Result.Fail`/`.lnk` 설명이 키 기반 치환. `ServiceConfiguration`이 등록 시 ILocalizer 전달(또는 DI 자동 해석). `JsonFolderShortcutRepositoryTests` L72/L96 단언이 키 기반으로 갱신되어 통과.
  - **Files**:
    - 주: `src/WorkGroup.Infrastructure/Shortcuts/ShortcutService.cs`(Fail 3 + `.lnk` 설명 접미사 "작업 관리"), `Launch/AppLauncher.cs`(Fail 3), `Inventory/InstalledAppInventory.cs`(Fail 3), `Icons/IconService.cs`(Fail 2), `Persistence/JsonGroupRepository.cs`(Fail 1), `Persistence/JsonFolderShortcutRepository.cs`(Fail 3)
    - 동반: `src/WorkGroup.App/ServiceConfiguration.cs`(6개 등록에 ILocalizer 전달), `src/WorkGroup.App/Strings/*/Resources.resw`(Infra 키)
    - 테스트: `tests/WorkGroup.Application.Tests/JsonFolderShortcutRepositoryTests.cs`(L72/L96 단언 → `Infra_Folder_Duplicate`/`Infra_Folder_NotFound` 키로 갱신). 그 외 인프라 테스트 무수정(선택 인자).
  - **Edge Cases**:
    - `ILocalizer` null → `NullLocalizer`(키 반환). DI 경로는 항상 실제 주입.
    - `LaunchAsAdmin`/IconService/InstalledAppInventory/ShortcutService 테스트는 `IsFailure`/`IsSuccess`/`ArgumentException`만 단언 → 키화해도 통과(실측, 4-C).
    - 생성자 가드 `throw`(개발자용)는 **변경 안 함**(Out of Scope).
  - **Halt Forecast**:
    - "인프라 직접 `new` 사용처?" → 착수 시 전수 grep 재확인(4-C에 식별 완료: 모두 선택 인자라 무영향, 텍스트 단언은 JsonFolderShortcutRepositoryTests 2곳).
    - "DI 등록 방식?" → type-based 등록(`AddSingleton<IAppInventory, InstalledAppInventory>`)은 ILocalizer를 DI가 자동 해석; factory 등록(repos/ShortcutService)은 `sp.GetRequiredService<ILocalizer>()` 명시 전달.
  - **Depends on**: T1

- [x] T9. 매니페스트 ms-resource 다국어화
  - **Type**: D
  - **Acceptance**: `dotnet build WorkGroup.slnx` 및 패키지 빌드 성공(resources.pri 생성). `DisplayName`/`VisualElements DisplayName`·`Description`/`StartupTask DisplayName`이 `ms-resource:///Resources/App_DisplayName`·`App_Description` 치환. 키 4개 언어 존재.
  - **Files**:
    - 주: `src/WorkGroup.App/Package.appxmanifest`
    - 동반: `src/WorkGroup.App/Strings/*/Resources.resw`(App_DisplayName/App_Description 값 확정 — T1 시드)
  - **Edge Cases**:
    - ms-resource 맵 경로 오류 시 셸이 패키지명 표시 → 전체 경로 `ms-resource:///Resources/...` 사용(Risks).
    - 비패키지 실행에는 매니페스트 무관.
  - **Halt Forecast**:
    - "런타임 셸 표시 검증?" → 헤드리스 불가. 빌드/패키지 성공으로 1차 검증, 수동 배포 확인은 Verification 명시.
  - **Depends on**: T1

- [x] T10. resw 키 패리티/빈 값 검증 테스트
  - **Type**: C
  - **Acceptance**: `dotnet test WorkGroup.slnx` 통과. 4개 resw 키 집합 동일·모든 값 비어있지 않음 단언. 누락/미번역 시 실패로 검출.
  - **Files**:
    - 주: `tests/WorkGroup.Application.Tests/ResourceParityTests.cs`(신규)
  - **Edge Cases**:
    - resw 경로 탐색 실패 → AppContext.BaseDirectory에서 상위로 올라가며 `src/WorkGroup.App/Strings` 탐색, 못 찾으면 명확한 실패 메시지.
    - resw XML `<data name><value>` 구조 파싱(주석/메타 무시).
  - **Halt Forecast**:
    - "테스트 실행 시 resw 위치?" → 리포지토리 루트 상대 탐색(Edge Cases).
  - **Depends on**: T4, T5, T6, T7, T8, T9

- [ ] T11. 문서 갱신(README/notes)
  - **Type**: A
  - **Acceptance**: README에 다국어 지원(4개 언어)·설정 "언어" 항목(기본 시스템 언어, 변경 시 재시작) 반영. notes.md 최상단 변경 항목 추가, 1개월 초과 항목 정리.
  - **Files**:
    - 주: `README.md`, `notes.md`
  - **Depends on**: T1~T10

## Glossary (번역 일관성 — 핵심 용어)
| 한국어 | English | 日本語 | 中文(简体) |
|---|---|---|---|
| 작업 그룹 | Work Group | ワークグループ | 工作组 |
| 그룹 | Group | グループ | 组 |
| 폴더 | Folder | フォルダー | 文件夹 |
| 앱 | App | アプリ | 应用 |
| 작업 표시줄 | taskbar | タスクバー | 任务栏 |
| 트레이 | tray | トレイ | 托盘 |
| 핀(고정) | pin | ピン留め | 固定 |
| 설정 | Settings | 設定 | 设置 |
| 정보 | About | バージョン情報 | 关于 |
| 테마 | Theme | テーマ | 主题 |
| 추가/수정/삭제/저장/확인/취소 | Add/Edit/Delete/Save/OK/Cancel | 追加/編集/削除/保存/OK/キャンセル | 添加/编辑/删除/保存/确定/取消 |
| 시스템 언어 | System language | システム言語 | 系统语言 |
| 관리자 권한으로 실행 | Run as administrator | 管理者として実行 | 以管理员身份运行 |
- 톤: 한국어 존댓말("~합니다") 기준에 맞춰 각 언어 정중체. ko 값은 기존 문구 그대로(소스 진실).

## 분할 권고 (task 11개 — 8개 초과)
이 plan은 11개 task로 큼. i18n은 응집적(부분 추출 시 혼재 상태)이라 단일 plan 진행을 권장하되, 컨텍스트 누적 대비:
- **권장 A) 단일 plan 진행** + implement-task가 2 task마다 Progress Log 갱신(후반 품질 보존).
- **B) 2개로 분할**: Plan-1(T1~T6: 인프라+XAML), Plan-2(T7~T11: C#+Infra+매니페스트+테스트+문서). T1(인프라)이 모든 후속의 선행이라 경계가 깨끗함.
- 사용자가 승인 시 A/B 택일. 미지정 시 A로 진행.

## Known Workarounds / Follow-ups
- **Domain 레이어 Result.Fail 한국어 문구(범위 외)**: 구현 중 발견 — `AppGroup.Create`("그룹 이름은 필수입니다."), `AppGroup.AddApp/RemoveApp`("이미 추가된 앱입니다: {앱}", "그룹에 없는 앱입니다."), `FolderShortcut.Create`("폴더 이름은 필수입니다.", "폴더 경로는 필수입니다.")가 `Result.Fail`로 한국어를 반환한다. 이들은 UI에 도달 가능하나 **App 레이어 검증(GroupEditViewModel/FolderEditDialog)이 먼저 같은 조건을 잡아 로컬라이즈된 메시지를 표시**하므로 실사용에서 거의 가려진다. AGENTS.md "Domain 외부 의존 0" 원칙상 Domain에 ILocalizer 주입은 부적절 → 근본 해결은 Domain이 **에러 코드/키**를 반환하고 App이 매핑하는 방식(계약 변경, 별도 승인 필요). **승인된 본 plan 범위 밖**이라 follow-up으로 남긴다.

- **증분 빌드의 stale resources.pri (빌드 도구 특성)**: T9 검증 중 발견 — 증분 빌드가 신규/변경된 `.resw`를 `resources.pri`에 재생성하지 않아, 한때 컴파일된 PRI에 문자열 키가 빠져 있었다. **클린 재빌드**(`dotnet clean` + obj/bin 제거 후 재빌드) 시 PRI에 4개 언어 키가 `Resources` 맵으로 정상 포함됨을 makepri dump로 확인(`ms-resource://{pkg}/Resources/App_DisplayName`). 코드 결함 아님. **resw 변경 후에는 클린 빌드 권장**(VS F5 배포는 대개 정상). README/notes에 안내.

## Verification Strategy
- 빌드: `dotnet build WorkGroup.slnx` — 경고/에러 0 목표(마크업 익스텐션/생성자 변경 컴파일 검증).
- 단위 테스트: `dotnet test WorkGroup.slnx` — 기존 + `AppLauncherTests`(갱신) + `ResourceParityTests`(신규) 통과.
- resw 키 패리티: T10이 4개 언어 키 동일성·빈 값 자동 검출.
- 수동 검증(헤드리스 불가, 사용자/VS): (1) 설정 언어 변경→재시작 후 UI 언어 반영, (2) "시스템 언어"→OS 언어 따름, (3) 시작 메뉴/매니페스트 표시 이름 언어 반영(MSIX 배포 후).

## Progress Log
<!-- implement-task가 2 task마다 갱신 -->
- T1-T2 완료 (커밋 b4049e2, 2294f85): 리소스 인프라(ILocalizer/NullLocalizer/LocalizationService/{loc:Localize} 마크업/4개 resw) + LanguageService(영속·적용·재시작). 마크업 PoC 빌드 게이트 통과(직접+부착 속성). PRIResource는 SDK 자동 포함(명시 불필요). 빌드 0/0, 테스트 115/115. BASE for reviews 진행: 각 task 직전 커밋.
- T3-T4 완료 (커밋 9c79388, 25ed1fd): 설정 언어 변경 UI(ConfirmRestartAsync 콜백/재시작 다이얼로그) + XAML 배치 A(MainShell/SettingsPage/AboutPage 전수 치환). resw 누적 29키, 4개 언어 키 집합 완전 일치(자체 diff 검증). 빌드 0/0, 테스트 115/115.
- T5-T6 완료 (커밋 46b4216, 5d90573): XAML 배치 B(WorkGroups/TrayMenu/GroupEdit) + 배치 C(FolderEdit/FolderPopupSettings/FolderListPopup/GroupPopup). Window 루트 마크업도 빌드 검증. ComboBoxItem 변환은 SelectedIndex 기반이라 안전. resw 누적 78키, 4개 언어 동일. 빌드 0/0, 테스트 115/115. 남은 XAML 하드코딩 없음(코드비하인드/인프라 C#은 T7/T8).
- T7-T8 완료 (커밋 d3093ab, +T8): App 레이어 C#(VM 주입 + 코드비하인드/Tray static) + Infra 6개 서비스 ILocalizer 선택 인자 주입. 6개 서비스 모두 ServiceConfiguration에서 ILocalizer 명시 전달(DI 선택인자 자동해석 비의존). JsonFolderShortcutRepositoryTests 단언 키로 갱신. resw 누적 122키, 4개 언어 동일. 빌드 0/0, 테스트 115/115. Domain Result.Fail 한국어는 범위 외 follow-up(Known Workarounds).

## Next Steps
<!-- 세션 종료/체크포인트 시 갱신 -->

## Open Questions
- (없음 — 모든 결정 해결됨. T8 인프라 직접 `new` 사용처는 구현 시 grep 재확인으로 처리, 결정 분기 아님.)
