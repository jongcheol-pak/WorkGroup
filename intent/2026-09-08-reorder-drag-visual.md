# Intent: 순서 변경 드래그에 항목 카드 비주얼 표시
Author: 사용자. Status: approved.

## Problem
원문 요청: "그룹 수정 화면에서 목록 이동 방식을 작업 그룹/트레이 메뉴에 적용" → 확인 결과 "현재는 항목의 앞에 버튼을 누르면서 드래그 하도록 하고 있는데 이때 마우스에 항목 ui를 표시 하도록만 해줘".
작업 그룹·트레이 메뉴 목록의 순서 변경 핸들을 끌면 마우스를 따라다니는 그림이 핸들 아이콘(≡)뿐이라, 지금 무엇을 옮기고 있는지 보이지 않는다. 그룹 수정 화면의 앱 목록은 ListView 내장 재정렬이라 항목 모습이 그대로 따라다닌다 — 두 화면의 시각 피드백이 다르다.

## Proposed outcome
두 목록 페이지에서 핸들을 잡고 끄는 동안 그 항목의 카드 전체(아이콘·이름·버튼 포함)가 마우스를 따라다닌다. 재정렬 조작법(핸들 드래그·삽입 표시선·검색 중 비활성)과 작업 표시줄 핀 드래그의 비주얼(그룹 아이콘)은 바뀌지 않는다.

## Affected users and systems
두 목록에서 순서를 바꾸는 앱 사용자. `WorkGroupsPage`·`TrayMenuPage`의 `OnReorderDragStarting`과 두 페이지가 공유하는 `ReorderDrop` 어댑터.

## Constraints
DDD 의존 방향 유지(App→Infrastructure→Application→Domain) · 두 목록 페이지의 조작법과 코드 구조를 같게 유지 · 새 리소스 키·새 의존성을 만들지 않는다 · 작업 표시줄 핀 드래그 경로는 건드리지 않는다.

## Open questions
없음.
