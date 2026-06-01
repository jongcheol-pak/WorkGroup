# WorkGroup (작업 그룹 런처)

PC에 설치된 앱들을 "작업 그룹"으로 묶고, 그 그룹을 Windows 11 작업 표시줄에 핀하여 클릭 한 번으로 그룹 안의 앱들을 아이콘 그리드 팝업에서 실행하는 WinUI 3 데스크톱 앱.

> **상태**: 개발 초기(기반 구조 단계). 상세 계획·진행 상황은 [`plan.md`](plan.md) 참조.

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

## 계획된 핵심 기능 (구현 진행 중 — plan.md 기준)
- 설치 앱 목록 수집(Win32 + Store/UWP) 및 앱 그룹 구성
- 그룹 목록 표시 및 작업 표시줄 드래그 등록(.lnk 핀)
- 작업 표시줄 아이콘 클릭 시 아이콘 그리드 팝업 + 앱 실행
- 그룹 아이콘 설정(내장 세트 / 멤버 앱 / 사용자 이미지)

현재 존재하는 것은 위 기능을 담을 **솔루션 구조와 레이어 프로젝트**다. 각 기능은 `plan.md`의 task 완료에 따라 본 문서에 반영된다.
