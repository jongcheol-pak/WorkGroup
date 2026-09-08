# Intent: WorkGroup.App ViewModel 테스트 프로젝트 신설
Author: 사용자. Status: approved.

## Problem
원문 요청: "WorkGroup.App의 ViewModel 테스트 프로젝트 신설 — tests/의 두 프로젝트 모두 WorkGroup.App을 참조하지 않아, ApplyFilter가 CanReorder를 갱신하는지·검색 중 MoveAsync가 무동작인지·실패가 StatusMessage에 담기는지를 재는 자리가 없다."
지금 하는 이유: 직전 회차(순서 변경 드래그 비주얼)가 T1·T2의 acceptance를 `[면제 ④]`(러너가 닿지 않음)로 닫아야 했고, 그 원인이 바로 이 빈자리다. App 레이어의 목록 동작은 회차마다 손대는데 지울 수 있는 코드가 계속 green이다.

## Proposed outcome
`tests/`에 `WorkGroup.App`을 참조하는 xUnit 프로젝트가 있고, `dotnet test WorkGroup.slnx` 한 번에 두 목록 ViewModel의 검색 필터·순서 변경·상태 메시지 동작이 함께 측정된다. 앱의 실행 동작(패키지 실행 시 WinAppSDK 초기화 포함)은 지금과 같다.

## Affected users and systems
이 레포에서 App 레이어를 고치는 개발자. `WorkGroupsViewModel`·`FolderShortcutsViewModel`과 그 항목 클래스, 새 테스트 프로젝트, `WorkGroup.slnx`, 그리고 `WorkGroup.App.csproj`의 WinAppSDK 자동 초기화 설정.

## Constraints
DDD 의존 방향 유지(App → Infrastructure → Application → Domain) · 기존 173건 테스트가 계속 통과할 것 · 새 런타임 의존성을 앱에 추가하지 않을 것 · 패키지(MSIX) 실행 동작을 보존할 것 · 테스트는 GUI·UI 스레드 없이 헤드리스로 돌 것.

## Open questions
없음.
