# WorkGroup (작업 그룹 런처)

PC에 설치된 앱들을 "작업 그룹"으로 묶고, 그 그룹을 Windows 11 작업 표시줄에 핀하여 클릭 한 번으로 그룹 안의 앱들을 아이콘 그리드 팝업에서 실행하는 WinUI 3 데스크톱 앱.

> **상태**: Plan 1(코어·인프라) 완료, Plan 2(UI·런처) 구현 완료(시각 검증 진행). 상세는 [`plan.md`](plan.md) 참조.

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

## 핵심 기능
- **설치 앱 수집**: Win32(시작 메뉴 .lnk) + Store/UWP(PackageManager) 앱을 현재 사용자 기준으로 수집·검색.
- **그룹 구성**: 설치 앱을 더블클릭으로 묶어 그룹 생성/편집/삭제. 그룹 아이콘은 내장 세트 / 멤버 앱 / 사용자 이미지(.png·.ico 등) 중 선택.
- **작업 표시줄 드래그 등록**: 그룹을 작업 표시줄로 끌어 핀(.lnk). 핀된 아이콘은 실행 별칭 `WorkGroup.exe`를 `--group {id}` 인자로 호출.
- **그룹 팝업 런처**: 핀된 아이콘 클릭 시 작업 표시줄 변 위에 멤버 앱 아이콘 그리드 팝업 → 클릭 시 앱 실행(Win32 셸 / 패키지 AUMID).
- **자동 시작 / 트레이**: 로그인 시 자동 시작(토글), 알림 영역 트레이 아이콘(열기/종료).

## 데이터 위치
- 셸이 접근하는 `.lnk`/`.ico`와 `groups.json`은 MSIX 가상화를 피해 `%USERPROFILE%\WorkGroup\` 아래에 저장된다.

## 아키텍처 메모
- 그룹 클릭 시 별칭 exe가 새 인스턴스로 떠 팝업만 표시 후 종료(상주 불필요). 관리 화면/트레이는 별도 상주 인스턴스.
