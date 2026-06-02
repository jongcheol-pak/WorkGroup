# Plan: 그룹 추가 다이얼로그 전면 개편 (아이콘 + 선택앱 목록 + 검증)

> 이전 plan들은 완료(git 이력). 본 plan은 GroupEditDialog를 새 레이아웃(아이콘+이름 / 앱 추가 / 선택앱 목록 / 확인·취소)으로 재설계하고, 리소스 아이콘 91개를 번들한다.

## Goal
1. 아이콘 선택 ComboBox 제거.
2. 상단: [그룹 아이콘][그룹 이름 입력(15자 제한)].
3. 상단 아래: **선택한 앱 목록**(항목 = 앱 아이콘 + 이름 + 우측 삭제 버튼).
4. 앱 목록 위 "앱 추가" 버튼.
5. "앱 추가" → 설치 앱 목록 팝업 → 선택 시 앱 목록에 추가.
6. 그룹 아이콘 클릭 → "사용자 아이콘"/"리소스 아이콘". 사용자=파일 선택기, 리소스=번들 이미지 그리드(`C:\Users\jongc\Desktop\icon` PNG 91개 번들).
7. 하단 확인/취소. 확인 → 그룹 추가. **앱 목록이 비었거나 그룹 이름이 (다른 그룹과) 중복이면 추가 불가.**

## Out of Scope
- 도메인 `IconSource`(Kind/Value)·`AppGroup`·`groups.json` 직렬화 형식 변경 — **없음**(리소스 아이콘은 CustomImage+ms-appx URI 재사용, DI1).
- `IGroupAppService`/`GroupAppService` 시그니처·저장 흐름 — 불변(중복 검증은 VM/UI, DI10).
- 팝업 런처·셸·작업 그룹 페이지·다른 페이지 — 불변.
- 기존 저장 그룹의 BuiltIn/MemberApp 아이콘 — 도메인·IconService 계속 지원(표시·.ico). 단 선택 UI는 사용자/리소스만 제공.

## Investigation Log
- `ls C:\Users\jongc\Desktop\icon` → **PNG 91개**(flat). 7KB~224KB. appgroup.png 존재.
- Read(IconService.cs) → CustomImage→`DecodeImageFileAsync`가 `StorageFile.GetFileFromPathAsync`(실파일 경로 전용) 사용. ms-appx는 `GetFileFromApplicationUriAsync` 분기 필요. `IsImageFile`은 **MemberApp 분기에서만** 호출(CustomImage는 직행) → 미수정.
- grep(JsonGroupRepository) → IconSource 직렬화 = `IconDto(Kind:string, Value:string)`. ms-appx URI 문자열 저장 가능 → 직렬화 무변경. ms-appx는 설치/머신 독립적.
- Read(GroupEditViewModel) → 현재: `_allApps`/`Apps`(ObservableCollection<SelectableAppItem> 체크박스)+`SearchText`+아이콘 ComboBox 멤버(IconOptions/SelectedIconOption/CustomImagePath/PreviewColor/PreviewImage/RefreshPreviewAsync/BuildIconSource/DescribeIcon/ColorForOption/OnSelectedIconOptionChanged/OnCustomImagePathChanged). `IGroupAppService _groupService` 주입됨(GetAllAsync 사용 가능). InitializeAsync에서 인벤토리 로드 + 편집 복원.
- grep(SelectableAppItem) → 사용처 = `GroupEditDialog.xaml`(DataTemplate) + `GroupEditViewModel.cs`만. 체크박스 모델 제거 시 **SelectableAppItem 제거 가능**.
- Read(PopupAppItem) → `AppEntry App` + `DisplayName` + `ImageSource? Icon` + `LoadIconAsync()`. 아이콘+이름 항목으로 **선택앱/설치앱 picker 양쪽 재사용 가능**.
- Read(GroupEditDialog.xaml/.cs) → ContentDialog(PrimaryButtonText="확인"/CloseButtonText="취소" 이미 존재, PrimaryButtonClick=OnPrimaryButtonClick deferral 검증). 본체에 이름 TextBox + 아이콘 ComboBox/미리보기 + 설치앱 체크 ListView + 검색.
- WinUI 제약: ContentDialog는 동시에 하나 → 설치앱/아이콘 팝업은 **Flyout**.

## Risks & Unknowns
| 위험 | 영향 | 완화책 |
|---|---|---|
| ms-appx 디코드 미지원 → 리소스 아이콘 .ico 실패 | 리소스 아이콘 불능 | T2: `DecodeImageFileAsync` ms-appx 분기. 실패 시 기본 폴백. 패키지 런타임 수동 확인. |
| `Package.Current.InstalledLocation` 열거가 테스트/비패키지 실패 | 리소스 그리드 빈 | T3 try/catch 빈 목록. |
| VM 멤버 제거 시 Dialog 바인딩 깨짐(체크박스/콤보) | 빌드 실패 | DI: **VM+Dialog 단일 task(T4)** 동시 교체(task별 빌드 게이트). |
| 리소스 ImageSource 백그라운드 생성 시 UI 스레드 위반 | 런타임 예외 | DI3: 카탈로그는 URI 문자열만, BitmapImage는 VM이 UI 스레드(InitializeAsync 진입부)에서 생성. |
| 중복 이름 검사 race/대소문자 | 잘못된 차단/허용 | DI10: 대소문자 무시(OrdinalIgnoreCase), 편집 시 자기 그룹 제외. 단일 사용자 데스크톱이라 race 무시. |
| 편집 모드 legacy(BuiltIn/MemberApp) 아이콘 미리보기 | 공백 | DI5: 생성된 `{id}.ico`(GroupIconLoader)로 미리보기. SelectedIcon=group.Icon 유지. |

## Impact Analysis
UI(다이얼로그·VM) + Infrastructure(IconService ms-appx) + 자산. 도메인/직렬화/앱서비스 무변경.

### 4-A. 변경 심볼·사용처(전수)
| 심볼/대상 | 사용처 | 처리 |
|---|---|---|
| `GroupEditViewModel` 구 멤버(Apps/_allApps/SearchText/ApplyFilter/OnSearchTextChanged + 아이콘 콤보 멤버 IconOptions/SelectedIconOption/CustomImagePath/PreviewColor/RefreshPreviewAsync/BuildIconSource/DescribeIcon/ColorForOption/OnSelectedIconOptionChanged/OnCustomImagePathChanged) | `GroupEditDialog.xaml`(바인딩)·`.xaml.cs`(OnIconOptionChanged) | **VM+Dialog 단일 task(T4)** 에서 전부 교체. |
| `SelectableAppItem`(클래스) | `GroupEditDialog.xaml`·`GroupEditViewModel.cs`만 | **제거**(PopupAppItem 재사용, DI11). |
| `PopupAppItem` | 팝업 런처·그룹목록 멤버아이콘 + (신규)다이얼로그 | **재사용**(변경 없음). |
| `IconService.DecodeImageFileAsync`(private, 2인자 path/ct) | IconService 내부 **2곳**: CustomImage(L80)·MemberApp(L101) | T2 ms-appx 분기(시그니처 불변). MemberApp은 실파일만 전달→분기 미진입(B1, 회귀 없음). |
| `IGroupAppService.GetAllAsync`/`SaveAsync` | GroupEditViewModel | **재사용**(중복검사용 GetAllAsync, 저장 SaveAsync). 시그니처 불변. |
| `WorkGroup.App.csproj` Content | - | Assets\GroupIcons\*.png wildcard(현재 자산은 개별 Content, glob 없음 → 중복 없음). |
| 신규 `ResourceIconCatalog`(App/Services) | GroupEditViewModel | 신규 + DI 등록. |
| 신규 `ResourceIconItem`(App/ViewModels) | GroupEditViewModel/Dialog | 신규(별도 파일). |

### 4-B. 계약·직렬화 변경
- **없음.** IconSource 직렬화 그대로. 리소스 아이콘 = `Kind=CustomImage, Value="ms-appx:///Assets/GroupIcons/{name}.png"`.

### 4-C. 영향 받는 테스트
- `IconServiceTests`(파일경로 CustomImage) → ms-appx 분기 추가해도 유지·통과. ms-appx는 Package.Current 필요라 테스트 호스트 불가 → 미추가(런타임 수동).
- VM/Dialog 단위 테스트 없음(UI). GroupAppService/Domain 테스트 무관(불변) → 80/80 회귀 없음.

### Verified by
- IconService/JsonGroupRepository/GroupEditViewModel/PopupAppItem/SelectableAppItem Read + grep 전수.

## Decisions

### DI1. 리소스 아이콘 = CustomImage(ms-appx URI) 재사용
- **Chosen**: `IconSource.FromCustomImage("ms-appx:///Assets/GroupIcons/{name}.png")`. 도메인 enum·직렬화 무변경. IconService만 ms-appx 디코드 확장.
- **Source**: JsonGroupRepository/IconService Read.

### DI2. 기본 아이콘 = appgroup.png
- **Chosen**: 미선택 새 그룹 = `FromCustomImage("ms-appx:///Assets/GroupIcons/appgroup.png")`(사용자 확정). 누락 시 IconService 기본 폴백.
- **Source**: 사용자 확인.

### DI3. ResourceIconCatalog — URI 문자열만(UI 스레드 BitmapImage)
- **Chosen**: `GetIconUrisAsync()` → `IReadOnlyList<string>`(ms-appx URI). UI 객체 미생성. `BitmapImage`는 VM이 **UI 스레드(InitializeAsync 진입부, 인벤토리 await 이전)** 에서 생성. Singleton(URI 캐시).
- **Source**: WinUI BitmapImage 스레드 어피니티.

### DI4. 아이콘 선택 = 미리보기 버튼 + 단일 Flyout(사용자/리소스 토글)
- **Chosen(구현 시 확정)**: 아이콘 미리보기 `Button`의 단일 `Flyout`에 [사용자 아이콘][리소스 아이콘] 두 버튼 + 리소스 `GridView`(초기 접힘). "리소스 아이콘" 클릭→VM `ShowResourceGrid=true`로 그리드 펼침(Visibility는 x:Bind — Flyout 내 x:Name code-behind 접근 회피). 항목 클릭→`SetResourceIcon`+Flyout 닫기. "사용자 아이콘"→Flyout 닫고 `FileOpenPicker`→`SetUserImage`. Flyout Opening마다 그리드 접음.
- **당초 MenuFlyout 미채택 사유**: `MenuFlyout`은 `MenuFlyoutItem`만 담을 수 있어 리소스 `GridView`를 직접 넣을 수 없고, 별도 ShowAt Flyout은 namescope/표시 복잡. 단일 Flyout+토글이 더 단순하고 ContentDialog 중첩 제약도 충족.
- **Source**: WinUI ContentDialog/Flyout/MenuFlyout 제약(구현 시 확정).

### DI5. 미리보기·편집 복원
- **Chosen**: VM `IconSource SelectedIcon`+`ImageSource? PreviewImage`. 신규→기본 리소스(DI2)+그 이미지; 편집→`SelectedIcon=group.Icon`+미리보기(CustomImage면 그 이미지, 그 외 legacy면 `GroupIconLoader.GetIconPath(id)` .ico). SaveAsync는 SelectedIcon 사용.
- **Source**: GroupEditViewModel/GroupIconLoader Read.

### DI6. 자산 번들
- **Chosen**: 91 PNG → `src/WorkGroup.App/Assets/GroupIcons/`. csproj `<Content Include="Assets\GroupIcons\**\*.png" />`.
- **Source**: 사용자 요구.

### DI7. 도메인 IconSource 유지
- **Chosen**: BuiltIn/MemberApp/CustomImage 유지(기존 저장 그룹 호환). 선택 UI만 사용자/리소스.

### DI8. 다이얼로그 레이아웃
- **Chosen**: ContentDialog 본체 =
  - 1행: [아이콘 미리보기 Button(좌)] [그룹 이름 TextBox(우, `MaxLength=15`)].
  - 2행: "앱 추가" Button(목록 위).
  - 3행: 선택앱 ListView(항목 = 아이콘 + 이름 + 우측 삭제 Button).
  - 상태 InfoBar(검증 메시지).
  - 하단: ContentDialog 기본 PrimaryButton("확인")/CloseButton("취소").
- **Source**: 사용자 요구.

### DI9. 설치앱 picker = "앱 추가" Flyout
- **Chosen**: "앱 추가" 버튼 클릭 → `Flyout`(검색 TextBox + 설치앱 ListView). 항목 클릭 → `AddApp(app)`(SelectedApps에 추가). picker 목록은 **이미 추가된 앱 제외**. 클릭 후 Flyout 유지(다중 추가 가능), 검색 필터. 설치앱은 InitializeAsync에서 `IAppInventory.GetInstalledAppsAsync()` 1회 로드.
- **Source**: 사용자 요구, WinUI Flyout.

### DI10. 검증(확인 차단 조건)
- **Chosen**: `OnPrimaryButtonClick`(deferral)에서 검증 — (a) `SelectedApps.Count >= 1`, (b) 이름 비어있지 않음(trim), (c) 이름이 **다른 그룹과 중복 아님**. 실패 시 `args.Cancel=true` + InfoBar 메시지. 중복 검사: **InitializeAsync 1회 스냅샷**(`_groupService.GetAllAsync()`로 기존 그룹명 집합, 편집 시 **자기 그룹 제외**)을 사용하고 **확인 시 재조회하지 않는다**(M2). `OrdinalIgnoreCase` 비교.
- **Rationale**: UI 레벨 검증으로 GroupAppService 계약·테스트 불변. 빈 멤버/중복 차단은 요구사항.
- **Source**: 사용자 요구.

### DI11. 항목 타입 = PopupAppItem 재사용, SelectableAppItem 제거
- **Chosen**: 선택앱·설치앱 picker 항목 모두 `PopupAppItem`(아이콘+이름) 재사용. 체크박스 모델 `SelectableAppItem`은 제거(미사용).
- **Source**: grep(SelectableAppItem 사용처 한정).

### DI12. 이름 15자 제한
- **Chosen**: TextBox `MaxLength="15"`. 저장 시 trim(기존 AppGroup.Create trim).
- **Source**: 사용자 요구.

## Tasks

> 공통: 한글 주석, UTF-8(BOM 없음), 빌드 `dotnet build WorkGroup.slnx` 0/0, 테스트 80/80(baseline 확인) 회귀 없음. 자율 종료=빌드/테스트, 시각·팝업 동작은 사용자 수동(패키지 실행).

- [x] **T1. 리소스 아이콘 91개 번들** *(~1h)*
  - **Type**: C
  - **Acceptance**: `C:\Users\jongc\Desktop\icon` PNG 91개를 `src/WorkGroup.App/Assets/GroupIcons/`에 복사. csproj `<Content Include="Assets\GroupIcons\**\*.png" />` 추가. 빌드 0/0. (P단계: 기존 csproj Content/Assets glob 확인 — 중복 회피.)
  - **Files**: 주: `src/WorkGroup.App/WorkGroup.App.csproj`, `src/WorkGroup.App/Assets/GroupIcons/*.png`(91 신규)
  - **Edge Cases**: 기존 Content와 중복 glob→duplicate(P단계 확인, 현재 개별 Content라 무중복). 대용량 무해. 영문 파일명.
  - **Halt Forecast**: "복사?" → PowerShell Copy-Item. "중복?" → 현재 개별 Content(glob 없음). "그래도 duplicate Content 경고 시?"(m3) → wildcard 제거하고 SDK 암묵 포함에 의존(또는 개별 Content 나열 유지) — 빌드 0/0 되는 방식 채택.
  - **Depends on**: -

- [x] **T2. IconService ms-appx 디코드 지원** *(~1.5h)*
  - **Type**: D
  - **Acceptance**: `DecodeImageFileAsync(string path, CancellationToken ct)`(정확 시그니처) 내부에서 `path.StartsWith("ms-appx:", OrdinalIgnoreCase)`면 `StorageFile.GetFileFromApplicationUriAsync(new Uri(path)).AsTask(ct)`, 아니면 기존 `GetFileFromPathAsync(path).AsTask(ct)`로 파일을 연 뒤 동일 디코드(ct 전파). `IsImageFile` **미수정**(MemberApp 전용, M1). 파일경로 동작·폴백 불변. 빌드 0/0, 기존 IconServiceTests 통과. ms-appx 테스트 미추가(런타임 전용, 4-C).
  - **호출자 2곳 확인(B1)**: `DecodeImageFileAsync`는 CustomImage 분기(L80, ms-appx 가능)와 **MemberApp 분기(L101)** 에서 호출된다. MemberApp의 `location`은 항상 실파일 경로(IsImageFile 가드 통과)라 `ms-appx:` 분기에 **진입하지 않음 → 회귀 없음**. 분기는 path 접두사 판정뿐이라 MemberApp 동작 불변.
  - **Files**: 주: `src/WorkGroup.Infrastructure/Icons/IconService.cs`
  - **Edge Cases**: ms-appx 부재/잘못된 URI/비패키지→예외→기본 폴백(기존 catch).
  - **Halt Forecast**: "ms-appx 읽기?" → `GetFileFromApplicationUriAsync`. "분기?" → `StartsWith("ms-appx:")`.
  - **Depends on**: -

- [x] **T3. ResourceIconCatalog(리소스 URI 열거)** *(~1h)*
  - **Type**: C
  - **Acceptance**: `ResourceIconCatalog.GetIconUrisAsync()`가 `Package.Current.InstalledLocation`의 `Assets\GroupIcons` PNG를 열거해 `IReadOnlyList<string>`(ms-appx URI) 반환(UI 객체 미생성). 실패→빈 목록(try/catch). 1회 캐시. DI Singleton 등록. 빌드 0/0.
  - **Files**: 주: `src/WorkGroup.App/Services/ResourceIconCatalog.cs`(신규), `src/WorkGroup.App/ServiceConfiguration.cs`(등록)
  - **Edge Cases**: 폴더 없음/비패키지→빈 목록. 91개→캐시.
  - **Halt Forecast**: "열거?" → `InstalledLocation.GetFolderAsync("Assets\\GroupIcons").GetFilesAsync()`. "URI?" → `ms-appx:///Assets/GroupIcons/{file.Name}`.
  - **Depends on**: T1
  - **Sub-skill**: 직접 구현.

- [x] **T4. GroupEditViewModel + GroupEditDialog 전면 재구성(병합)** *(~5h)*
  - **Type**: D
  - **병합 사유**: VM 구 멤버 제거와 Dialog 바인딩 전환을 **한 task에서 동시**(task별 빌드 게이트).
  - **Acceptance(VM)**:
    - 제거: `Apps`/`_allApps`/`SearchText`/`ApplyFilter`/`OnSearchTextChanged` + 아이콘 콤보 멤버(IconOptions/SelectedIconOption/CustomImagePath/PreviewColor/RefreshPreviewAsync/BuildIconSource/DescribeIcon/ColorForOption/OnSelectedIconOptionChanged/OnCustomImagePathChanged). `SelectableAppItem` 클래스 제거.
    - 신규: `SelectedIcon`(IconSource)+`PreviewImage`(ImageSource?)+`ObservableCollection<ResourceIconItem> ResourceIcons`+`ObservableCollection<PopupAppItem> SelectedApps`+`ObservableCollection<PopupAppItem> AvailableApps`(picker, 선택 제외)+`SetUserImageAsync(path)`/`SetResourceIcon(uri)`/`AddApp(AppEntry)`/`RemoveApp(PopupAppItem)`/`PickerSearch`(검색)+`CanConfirm` 검증.
    - InitializeAsync(UI 스레드): ResourceIcons용 BitmapImage를 **인벤토리 await 이전**에 카탈로그 URI로 생성(M2). 기존 그룹명 집합 로드(`_groupService.GetAllAsync`, 편집 자기 제외). 인벤토리 로드→AvailableApps 구성. 신규→SelectedIcon=기본 리소스(DI2)/SelectedApps 비움; 편집→SelectedIcon=group.Icon+미리보기(DI5)/SelectedApps=group.Apps 각 AppEntry를 `AddApp(entry)`로 래핑(PopupAppItem, m1 — IReadOnlyList<AppEntry>→ObservableCollection<PopupAppItem>).
    - `ValidateAndBuildAsync()`(확인용): 선택앱≥1·이름 비어있지않음·이름 중복아님(DI10) 검증 후 AppGroup 생성/복원(SelectedIcon, SelectedApps) → `_groupService.SaveAsync`. 실패 사유는 StatusMessage.
  - **Acceptance(Dialog)**:
    - 레이아웃 DI8: 1행 [아이콘 Button][이름 TextBox MaxLength=15], 2행 "앱 추가" Button, 3행 선택앱 ListView(아이콘+이름+삭제 Button), InfoBar.
    - 아이콘 Button: MenuFlyout(사용자/리소스) → 사용자=FileOpenPicker→SetUserImageAsync, 리소스=리소스 GridView Flyout→SetResourceIcon.
    - "앱 추가" Button: Flyout(검색+AvailableApps ListView, 항목 클릭→AddApp). 삭제 Button→RemoveApp.
    - PrimaryButtonClick(deferral)→ValidateAndBuildAsync, 실패 시 args.Cancel(InfoBar 메시지).
    - 빌드 0/0, 테스트 80/80.
  - **Files**: 주: `src/WorkGroup.App/ViewModels/GroupEditViewModel.cs`, `src/WorkGroup.App/Views/GroupEditDialog.xaml`, `src/WorkGroup.App/Views/GroupEditDialog.xaml.cs`. 신규: `src/WorkGroup.App/ViewModels/ResourceIconItem.cs`. 제거: `src/WorkGroup.App/ViewModels/SelectableAppItem.cs`.
  - **Edge Cases**: 빈 앱목록→확인 차단+메시지. 중복 이름→차단+메시지. 이름 미입력→차단. 사용자 이미지 취소→기존 유지. 리소스 빈→그리드 빈(사용자/기본 동작). 편집 legacy→.ico 미리보기. 같은 앱 중복 추가→AvailableApps 제외로 방지. picker 빈 결과→안내. Flyout 열린 채 확인→정상.
  - **Halt Forecast**: "중첩 ContentDialog?" → DI4/DI9(Flyout). "중복 검사 소스?" → DI10(GetAllAsync). "BitmapImage 스레드?" → DI3(InitializeAsync 진입부). "항목 타입?" → DI11(PopupAppItem). "FileOpenPicker HWND?" → App.MainWindow 이식.
  - **Depends on**: T2, T3

- [x] **T5. 문서 갱신** *(~0.3h)*
  - **Type**: A
  - **Acceptance**: `README.md`(그룹 구성 — 아이콘 사용자/리소스, 앱 추가/삭제, 검증) + `notes.md` 갱신. 전체 빌드 0/0 + 테스트 80/80 최종 확인.
  - **Files**: 문서: `README.md`, `notes.md`
  - **Depends on**: T4

## Verification Strategy
- 빌드 `dotnet build WorkGroup.slnx` → 0/0. 테스트 `dotnet test` → 80/80.
- 수동(GUI, 패키지 실행 — 자율 불가): ① 상단 아이콘+이름(15자 제한) ② 아이콘 클릭→사용자/리소스→적용 ③ 리소스 그리드(91) 선택 ④ "앱 추가"→설치앱 팝업→선택→목록 추가 ⑤ 항목 삭제 ⑥ 빈 목록/중복 이름→확인 차단 ⑦ 정상→그룹 추가 ⑧ 편집 시 기존 아이콘/앱 복원 ⑨ 아이콘 Flyout→사용자 파일 선택기→다이얼로그 정상 복귀(m2).

## Progress Log
<!-- implement-task가 갱신 -->
- **T1-T3 완료** (커밋 f76d050, 79873fb, 65e2f3f): T1=리소스 PNG 91개 번들(Assets/GroupIcons, build.appxrecipe 91개 확인). T2=IconService.DecodeImageFileAsync ms-appx 분기(GetFileFromApplicationUriAsync, IsImageFile 미수정, MemberApp 회귀 없음). T3=ResourceIconCatalog(URI 문자열 열거, 캐시, 빈목록 폴백)+DI Singleton. 빌드 0/0, 테스트 80/80. spec/quality OK.
- **T4-T5 완료** (커밋 5770064, 다음): T4=GroupEditViewModel+GroupEditDialog 전면 재구성(아이콘 미리보기+단일 Flyout 사용자/리소스, 앱 추가 Flyout, 선택앱 목록+삭제, 이름 15자, 빈목록·중복 검증 ValidateAndSaveAsync; SelectableAppItem 제거, PopupAppItem 재사용; B1 namescope/M2 rename 수정). T5=문서(README/notes). 빌드 0/0, 테스트 80/80. spec/quality OK. **plan 전체 완료(T1~T5).**

## Next Steps
- **현재 상태(2026-06-02)**: ✅ 그룹 추가 다이얼로그 전면 개편 완료(T1~T5). 빌드 0/0, 테스트 80/80. 도메인/직렬화 무변경.
- **GUI 수동 검증 필요**(패키지 실행, 자율 불가): ① 상단 아이콘+이름(15자) ② 아이콘 클릭→사용자/리소스→적용 ③ 리소스 그리드(91) ④ 앱 추가 Flyout→선택→목록 ⑤ 항목 삭제 ⑥ 빈목록/중복 이름→확인 차단 ⑦ 편집 복원 ⑧ 저장된 .ico가 리소스 이미지로 생성.
- 권장 다음 액션: 사용자 GUI 검증 → 정상 시 PR 생성.
- Suggested skills: 공식 /code-review, /security-review.

## Open Questions (모두 해결됨)
- [x] 기본 아이콘 → **appgroup.png**(사용자).
- [x] 리소스 아이콘 표현 → CustomImage(ms-appx URI) 재사용(도메인/직렬화 무변경, DI1).
- [x] 설치앱 선택 방식 → "앱 추가" Flyout 클릭-추가(DI9). 항목 타입 PopupAppItem 재사용(DI11).
- [x] 검증 위치 → VM/UI(확인 시 빈목록·중복이름 차단, GroupAppService 불변, DI10).
