# plan — 그룹 편집 화면에 ".exe/.lnk 파일 추가" 버튼

## 목표
그룹 추가/편집 다이얼로그에서 "앱 추가(+)" 버튼 오른쪽에 "파일 추가" 버튼을 두고, 클릭 시 파일 선택기로 실행 파일(.exe/.lnk)을 골라 선택 앱 목록에 추가한다.

## 범위
- IN: "파일 추가" 버튼 추가, 단일 파일 선택기(.exe/.lnk), 선택 파일을 앱 목록에 추가, 툴팁 로컬라이제이션.
- OUT: 다중 파일 동시 선택, 새 파일 형식 지원(이미지/폴더 등), 도메인/직렬화/인벤토리 로직 변경, 자동 테스트 신규 추가(인프라 `CreateManualEntry`는 이미 테스트됨).

## 결정 사항 (사용자 승인 완료)
- **D1 선택 개수**: 단일 파일(`PickSingleFileAsync`).
- **D2 파일 형식**: `.exe` + `.lnk`(인프라 `CreateManualEntry`가 이미 두 형식 허용, 오류 메시지도 .exe/.lnk 안내).
- **D3 중복 처리**: 이미 목록에 있는 대상은 기존 `AddApp`이 `SameTarget`(경로 기준)으로 조용히 무시(기존 동작 일관).
- **D4 재사용**: 신규 도메인/인프라 작성 없음 — 기존 `IAppInventory.CreateManualEntry`(검증+AppEntry 생성) + `GroupEditViewModel.AddApp`(중복 제거+아이콘 로드) 재사용.

## 영향 범위 전수 조사 (Impact Analysis)
- **재사용 자산(변경 없음, 직접 확인)**:
  - `IAppInventory.CreateManualEntry(string)` → `Result<AppEntry>`(`IAppInventory.cs:19`, 구현 `InstalledAppInventory.cs:57-73`): .exe/.lnk 검증 → `new AppEntry(파일명, 경로, AppKind.Win32, 경로)`. 이미 단위 테스트 존재(`InstalledAppInventoryTests.cs:54-82`).
  - `GroupEditViewModel.AddApp(AppEntry)`(`GroupEditViewModel.cs:243-255`): `SameTarget` 중복 무시 + `EnsureIconLoad` + `RefreshAvailable`.
  - `AppIconLoader.LoadAsync`(`AppIconLoader.cs:14-53`): Win32(LaunchTarget=exe 경로)는 셸 썸네일로 아이콘 추출 → exe/.lnk 아이콘 표시 보장.
  - `GroupEditViewModel`은 이미 `IAppInventory _inventory`를 주입받음(`GroupEditViewModel.cs:23,40`) → **DI 변경 불필요**.
- **신규 심볼**:
  - `GroupEditViewModel.AddManualFile(string path)` — 호출처: 신규 코드비하인드 핸들러 `OnAddFileClick` 1곳뿐(grep 시 신규 메서드라 기존 호출자 없음).
  - `GroupEditDialog.OnAddFileClick` — XAML `Click`에서만 참조.
- **로컬라이제이션 패리티**: `ResourceParityTests`가 4개 언어 `.resw` 키 동일성 강제 → 신규 키 `GroupEdit_AddFileTooltip`를 4개 파일 모두 추가.
- **파일 선택기 패턴 선례**: `GroupEditDialog.OnUserIconClick`(`GroupEditDialog.xaml.cs:59-78`)이 `FileOpenPicker`+`InitializeWithWindow`+`PickSingleFileAsync`를 이미 사용 → 동일 패턴 적용(`Windows.Storage.Pickers` using 기존 존재 `:4`).
- **오류 메시지 키**: `Infra_Inventory_EmptyPath`/`FileNotFound`/`InvalidType` 3개 키가 각각 4개 언어에 존재(확인) → `CreateManualEntry` 실패 시 `Result.Error`(이미 현지화) 그대로 표시.

## 위험
- 외부 의존: `FileOpenPicker`는 데스크톱 WinUI에서 HWND 초기화 필요 → 기존 `OnUserIconClick`과 동일하게 `App.MainWindow` HWND로 `InitializeWithWindow`(MainWindow null 가드 동일).
- 회귀: 기존 "앱 추가(+)" 버튼/Flyout과 독립(별도 버튼, Flyout 없음) → 기존 흐름 영향 없음.
- 동시성: VM 메서드는 UI 스레드 동기 호출(파일 선택은 await 후 경로만 전달) — 기존 `SetUserImage` 패턴과 동일.

## 검증 방법
- `dotnet build WorkGroup.slnx` — 경고/에러 0.
- `dotnet test WorkGroup.slnx` — 전체 통과(`ResourceParityTests` 포함). 신규 자동 테스트는 없음(재사용 인프라는 기존 테스트로 커버, VM/뷰는 GUI 수동 검증).

---

## 작업 분해

### T1. GroupEditViewModel에 파일 추가 메서드  — Type C
- [x] 구현 완료
- **파일**: `src/WorkGroup.App/ViewModels/GroupEditViewModel.cs`
- **변경**: `public void AddManualFile(string path)` 추가 — `_inventory.CreateManualEntry(path)` 호출, 실패 시 `StatusMessage = result.Error`, 성공 시 `StatusMessage = string.Empty` 후 `AddApp(result.Value)`.
- **Decision Points**:
  - 반환 타입: `void`(기존 `SetUserImage`/`AddApp` 패턴, 결과는 StatusMessage/목록으로 표현).
  - 실패 표시: `CreateManualEntry`의 현지화된 `Result.Error` 재사용(신규 키 불필요).
  - 중복: `AddApp`이 처리(별도 분기 없음 — D3).
- **Edge Cases**: 존재하지 않는 경로/지원하지 않는 형식 → `CreateManualEntry`가 `Fail` → StatusMessage 표시. 빈 경로 → `Fail`. 이미 추가된 대상 → `AddApp` 무시(목록 불변).
- **Halt Forecast**: 없음(기존 주입 의존성·기존 메서드 재사용).
- **Acceptance**: 빌드 성공. `AddManualFile`이 `CreateManualEntry`→(성공 시)`AddApp` 위임, 실패 시 `StatusMessage` 설정함(코드 리뷰로 확인).

### T2. 로컬라이제이션 키 추가(4개 언어)  — Type A
- [x] 구현 완료
- **파일**: `src/WorkGroup.App/Strings/{ko-KR,en-US,ja-JP,zh-Hans}/Resources.resw`
- **추가 키**: `GroupEdit_AddFileTooltip` — ko="파일 추가", en="Add file", ja="ファイル追加", zh="添加文件". (기존 `GroupEdit_AddAppTooltip` 인접 위치에 삽입)
- **Decision Points**: 키 접두사 `GroupEdit_*` 일관, 버튼은 아이콘만 표시하므로 툴팁 키만 추가.
- **Edge Cases**: 4개 파일 키 누락 시 `ResourceParityTests` 실패 → 전수 추가로 방지.
- **Acceptance**: `dotnet test`의 `ResourceParityTests` 통과(4개 언어 키 동일).

### T3. GroupEditDialog 버튼 + 파일 선택 핸들러  — Type C
- [x] 구현 완료
- **파일**:
  - `src/WorkGroup.App/Views/GroupEditDialog.xaml` — "앱 추가(+)" 버튼 오른쪽(같은 `StackPanel`, `Grid.Row=3` `Grid.Column=1`)에 "파일 추가" `Button` 추가: `SymbolIcon Symbol="OpenFile"` + `ToolTipService.ToolTip="{loc:Localize Key=GroupEdit_AddFileTooltip}"` + `Click="OnAddFileClick"`(Flyout 없음).
  - `src/WorkGroup.App/Views/GroupEditDialog.xaml.cs` — `OnAddFileClick`: `FileOpenPicker`에 `.exe`·`.lnk` 필터, `App.MainWindow` HWND로 `InitializeWithWindow`(null 가드), `PickSingleFileAsync`, 결과 비-null 시 `ViewModel.AddManualFile(file.Path)`(기존 `OnUserIconClick` 패턴 모방).
- **Decision Points**:
  - 버튼 아이콘: `SymbolIcon Symbol="OpenFile"`(파일 열기 의미, 기존 "+" `Symbol="Add"`와 시각적 구분).
  - 배치: 기존 `StackPanel`(Spacing=12) 마지막 자식으로 추가 → "+" 우측.
  - 파일 형식 필터: `.exe`, `.lnk`(D2).
- **Edge Cases**: 선택 취소(file null) → 무시. `App.MainWindow` null → 초기화 생략(기존 `OnUserIconClick` 동일). 잘못된 형식은 필터+`CreateManualEntry`로 이중 차단.
- **Halt Forecast**: 없음(기존 파일 선택기 선례 존재).
- **Acceptance**: 빌드 성공. 다이얼로그에 "+" 우측 "파일 추가" 버튼 노출, 클릭 시 .exe/.lnk 선택기 → 선택 파일이 앱 목록에 추가됨(헤드리스 빌드로 컴파일·바인딩 검증, 실제 동작은 F5 GUI 수동 확인).

## 작업 의존성
- T3 ← T1(VM 메서드), T2(loc 키). T1·T2는 상호 독립.

## 문서 갱신
- `README.md`: 그룹 편집의 앱 추가에 "파일(.exe/.lnk) 직접 추가" 기재(해당 기능 설명 위치).
- `notes.md`: 변경 내역 1줄 추가(최신 위).

## 승인 필요 항목
- 없음(공개 API 추가/변경 없음 — VM에 메서드 추가는 내부 UI 계층, 인터페이스 변경 아님). 신규 의존성 없음.

## Progress Log
- T1~T3 완료(미커밋): `GroupEditViewModel.AddManualFile`(CreateManualEntry→AddApp 위임), `GroupEdit_AddFileTooltip` 4언어, `GroupEditDialog.xaml` "+" 우측 "파일 추가" 버튼(`SymbolIcon OpenFile`, "+"의 Add와 구분) + `OnAddFileClick`(FileOpenPicker .exe/.lnk 단일). 빌드 0/0, 테스트 Domain 23/Application 98(ResourceParityTests 통과). 리뷰: spec-compliance 핵심 acceptance 전부 OK / code-quality MINOR 2건(기존 패턴 일치로 유지).
- 리뷰 scope 경고(M1/N1)는 **이번 task 결함 아님**: working tree에 (a)세션 시작 시점부터 있던 기존 ui-overhaul 변경(`PopupAppItem.cs` EnsureIconLoad, `ContainerContentChanging`), (b)앞 작업(설정 초기화)의 `Settings_*`/`Common_Reset` resw 키가 함께 있어 전체 diff에 혼재한 것. 커밋 시 기능별 선택 staging 필요.

## Next Steps
- 권장 다음 액션: 기능별 선택 staging 후 커밋(파일 추가 기능 / 설정 초기화 / 기존 ui-overhaul 분리). 실제 버튼·파일 선택·목록 추가 동작은 F5 MSIX GUI 수동 확인.
- Suggested skills: 공식 /verify (GUI 동작 확인), 공식 /code-review (커밋 후 diff 검토)
