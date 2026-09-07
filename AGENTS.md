# AGENTS.md — WorkGroup

WinUI 3 / .NET 10 / MSIX 기반 "작업 그룹 런처" 프로젝트의 에이전트 작업 가이드.

## Plan Location
- `plan.md` (저장소 루트, 덮어쓰기 방식). 계획·진행·결정은 모두 여기.

## 빌드 / 테스트 명령
- 빌드(솔루션): `dotnet build WorkGroup.slnx` — 플랫폼 미지정으로 실행한다. `-p:Platform=x64`를 강제하면 slnx 솔루션 구성 매핑 오류가 난다.
- 앱 단독 RID 빌드(필요 시): `dotnet build src/WorkGroup.App/WorkGroup.App.csproj -p:Platform=x64`
- 테스트: `dotnet test WorkGroup.slnx` (Domain/Application 단위 테스트)
- 패키지 앱 GUI 실행은 수동(Visual Studio F5 MSIX 배포). 헤드리스 자율 실행에서는 GUI 관찰 불가.

## 아키텍처 (DDD 레이어)
- `src/WorkGroup.Domain` (net10.0) — 순수 도메인 모델/불변식. 외부 의존 0.
- `src/WorkGroup.Application` (net10.0) — use case 서비스 + 인프라 인터페이스 정의. Domain만 의존.
- `src/WorkGroup.Infrastructure` (net10.0-windows10.0.19041.0) — WinRT/Win32 interop 구현. Application·Domain 의존.
- `src/WorkGroup.App` (WinUI3, net10.0-windows10.0.19041.0) — DI 조립 + View/ViewModel + 활성화/런처.
- `tests/WorkGroup.Domain.Tests` (net10.0, xUnit), `tests/WorkGroup.Application.Tests` (net10.0-windows10.0.19041.0, xUnit — Infrastructure를 참조해 windows TFM).
- 의존 방향: App → Infrastructure → Application → Domain (역방향 금지).

## 코딩 규칙
- 주석·문서는 **한글**. "왜"를 적고 자명한 "무엇"은 생략.
- 파일 **1500라인 내외**. 초과 시 기능 단위 분리.
- 파일 인코딩 **UTF-8 (BOM 없음)**.
- 비즈니스 로직은 Domain/Application 중심(DDD). YAGNI — 3회 반복 확인된 것만 공통화.
- 에러 처리: Domain/Application은 Result 패턴, 인프라 경계에서 예외→Result 변환(plan.md D14).
- 승인 없이 구조 변경·공개 API 변경·의존성 추가·대량 수정 금지.

## 승인된 의존성
- Microsoft.WindowsAppSDK, CommunityToolkit.Mvvm, Microsoft.Extensions.DependencyInjection/Logging,
  Microsoft.Windows.CsWin32, WinUIEx, CommunityToolkit.WinUI.Controls.SettingsControls, xUnit.
- UI는 WinUI 3 Gallery / Fluent 디자인 가이드 준수. 디자인 토큰은 `Resources/Spacing.xaml`·`ControlStyles.xaml`(CardStyle 등),
  설정/정보 항목은 `SettingsCard`, 페이지는 공통 레이아웃(ScrollViewer+PageContentPadding+ContentMaxWidth+헤더), 셸은 커스텀 TitleBar + NavigationView(280 pane).
