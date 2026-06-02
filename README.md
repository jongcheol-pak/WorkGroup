# WorkGroup (작업 그룹 런처)

PC에 설치된 앱들을 "작업 그룹"으로 묶고, 그 그룹을 Windows 11 작업 표시줄에 핀하여 클릭 한 번으로 그룹 안의 앱들을 아이콘 그리드 팝업에서 실행하는 WinUI 3 데스크톱 앱.

> **상태**: 코어·인프라·런처 + UI 전면 개편(NavigationView 셸 + Fluent) 완료. 상세는 [`plan.md`](plan.md) 참조.

## 기술 스택
- C# / .NET 10
- WinUI 3 (Windows App SDK), MSIX 패키징
- Windows 11

## 아키텍처 (DDD 레이어)
| 프로젝트 | TFM | 역할 |
|---|---|---|
| `src/WorkGroup.Domain` | net10.0 | 도메인 모델/불변식 (외부 의존 없음) |
| `src/WorkGroup.Application` | net10.0 | use case 서비스 + 인프라 인터페이스 |
| `src/WorkGroup.Infrastructure` | net10.0-windows10.0.19041.0 | WinRT/Win32 interop 구현 |
| `src/WorkGroup.App` | net10.0-windows10.0.19041.0 | WinUI 3 UI + DI + 런처 |

의존 방향: `App → Infrastructure → Application → Domain`

## 빌드 / 실행
```sh
# 빌드
dotnet build WorkGroup.slnx

# 단위 테스트
dotnet test WorkGroup.slnx
```
- 패키지 앱 GUI 실행: Visual Studio에서 `WorkGroup.App`을 시작 프로젝트로 두고 F5(MSIX 배포·디버그).

## 화면 구성 (NavigationView 셸)
좌측 메뉴 + 우측 컨텐츠 레이아웃. WinUI 3 Gallery / Fluent 디자인(커스텀 TitleBar + Mica 백드롭, 라이트/다크 테마, `SettingsCard`, 공통 페이지 레이아웃). 디자인 토큰은 `src/WorkGroup.App/Resources/`(Spacing/ControlStyles).
- **작업 그룹**: 그룹 목록(그룹 아이콘 + 이름/멤버 앱 아이콘 2라인 + 수정·삭제 아이콘 버튼), 상단 "그룹 추가".
- **트레이 메뉴**: 추후 추가 예정(placeholder).
- **설정**(하단): 로그인 시 자동 시작 토글, 앱 테마(시스템/라이트/다크) 전환.
- **정보**(하단): 앱 이름·버전, 오픈소스 라이선스 목록(이름+종류+링크).

## 핵심 기능
- **설치 앱 수집**: Win32(시작 메뉴 .lnk) + Store/UWP(PackageManager) 앱을 현재 사용자 기준으로 수집·검색.
- **앱 아이콘 추출**: Win32(.lnk/.exe 파일 경로)·Store/UWP(`shell:AppsFolder\{AUMID}`) 앱 모두 셸이 렌더한 아이콘(`IShellItemImageFactory`)을 추출한다 — 탐색기/시작 메뉴와 동일해 아이콘 누락이나 앱별 로고 여백 편차 없이 균일. 추출 실패 시 셸 썸네일/단색·기본 아이콘으로 폴백.
- **그룹 구성**: "그룹 추가/수정" 다이얼로그에서 상단 [아이콘][이름(15자)], "앱 추가"로 설치 앱을 골라 선택 목록에 담고(항목별 삭제) 그룹 생성/편집/삭제. 그룹 아이콘은 클릭 → **사용자 이미지**(파일 선택) 또는 **리소스 아이콘**(번들 이미지 그리드) 선택. 앱이 없거나 이름이 중복이면 추가가 차단됨.
- **작업 표시줄 드래그 등록**: 그룹 항목을 작업 표시줄로 끌어 핀(.lnk). 핀된 아이콘은 실행 별칭 `WorkGroup.exe`를 `--group {id}` 인자로 호출.
- **그룹 팝업 런처**: 핀된 아이콘 클릭 시 작업 표시줄 변 위에 멤버 앱 아이콘 그리드 팝업 → 클릭 시 앱 실행(Win32 셸 / 패키지 AUMID).
- **테마**: 시스템/라이트/다크 선택, 설정에 영속(다음 실행에도 유지). 팝업 창에도 동일 적용.
- **자동 시작 / 트레이**: 로그인 시 자동 시작(토글), 알림 영역 트레이 아이콘(열기/종료).

## 데이터 위치
- 셸이 접근하는 `.lnk`/`.ico`와 `groups.json`은 MSIX 가상화를 피해 `%USERPROFILE%\WorkGroup\` 아래에 저장된다.

## 아키텍처 메모
- 그룹 클릭 시 별칭 exe가 새 인스턴스로 떠 팝업만 표시 후 종료(상주 불필요). 관리 화면/트레이는 별도 상주 인스턴스.
