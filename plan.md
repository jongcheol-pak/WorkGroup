# 계획: 스토어 업데이트 후 트레이 자동 재시작 복구

## 목표
트레이에 상주(자동 시작) 중인 앱이 스토어(MSIX) 업데이트로 OS에 강제 종료된 뒤,
업데이트 완료 후 **자동으로 트레이에 다시 상주**하도록 한다.

## 근본 원인 (systematic-debugging Phase 1~3, 확정)
- full-trust 데스크톱 MSIX 앱(`Windows.FullTrustApplication`)은 업데이트 적용 시 OS가 강제 종료한다.
- 업데이트 후 자동 재시작은 **`RegisterApplicationRestart`(kernel32) 등록**이 있어야 일어난다
  (공식 문서: desktop-to-uwp "Restart automatically after receiving an update from the Microsoft Store").
  `windows.updateTask` 매니페스트 확장은 UWP 전용이라 이 앱엔 해당 없음.
- 이 앱은 해당 API를 어디서도 호출하지 않음 → 업데이트 후 재시작되지 못하고 종료된 채 남음.
- `StartupTask`는 **로그인 시점**에만 트리거되므로 업데이트 직후 복귀를 보장하지 못함.

## 범위 / 승인
- 사용자 승인 완료(2026-06-23, "진행").
- 변경: 신규 정적 서비스 1개, `App.xaml.cs` 활성화 분기·상주 진입, `GroupArgs` 공개 메서드 추가(+테스트), `ActivationParser` 분기.
- 매니페스트·도메인·직렬화·DI 컨테이너 무변경(정적 호출).

## 작업 단계
- [x] T1: `GroupArgs`에 silent 시작 플래그(`--silent`) 상수 + `HasSilentFlag` 파서 추가, 단위 테스트 작성. (빌드 0/0, 테스트 통과)
- [x] T2: `ActivationParser`에 `IsSilentStart` 분기 추가(Launch 인자에서 silent 플래그 감지). (빌드 0/0)
- [x] T3: 신규 `Services/UpdateRestartService.cs` — `RegisterApplicationRestart` P/Invoke + `RegisterForRestart()`
      (플래그 `RESTART_NO_CRASH|RESTART_NO_HANG`, 재시작 인자=`--silent`). 리뷰 반영: HRESULT 실패 시 경고 로그(ILogger<App>). (빌드 0/0)
- [x] T4: `App.xaml.cs` — `BecomeResidentInstance`에서 `UpdateRestartService.RegisterForRestart()` 호출,
      `OnLaunched`/`OnAppInstanceActivated`의 메인 창 표시 조건에 `!IsSilentStart` 추가
      (StartupTask와 동일하게 silent 재시작은 메인 창 미표시). (빌드 0/0)

## 검증 결과
- 빌드: 솔루션(x64 Debug) 경고 0 / 오류 0.
- 테스트: Domain 23 + Application 117(silent 신규 8 포함) = 140, 실패 0.
- 실제 업데이트 후 재시작·트레이 복귀: Store 배포 의존이라 미검증(사용자 수동 검증 대상).

## 검증 방법
- `dotnet build`(x64) 경고/에러 0.
- `dotnet test` — 기존 132 + GroupArgs silent 신규 테스트 통과.
- 실제 스토어 업데이트 후 재시작은 Store 배포 의존이라 헤드리스 재현 불가 → **사용자 수동 검증(미검증으로 보고)**.
- 안전장치: 인자 전달이 누락돼 빈 인자로 재시작돼도 `EnsureTray`로 트레이 상주는 보장(핵심 "종료" 해결).

## 미검증/한계
- 패키지 MSIX 앱에서 `RegisterApplicationRestart`의 명령줄 인자 전달 신뢰성은 실측 미검증(위 안전장치로 대비).
