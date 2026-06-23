using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkGroup.Infrastructure.Activation;

namespace WorkGroup.App.Services;

/// <summary>
/// 스토어/Windows 업데이트로 OS가 앱을 강제 종료한 뒤 자동으로 다시 시작하도록 등록한다.
/// full-trust 데스크톱 MSIX 앱은 RegisterApplicationRestart 등록이 없으면 업데이트 후
/// 트레이로 복귀하지 못하고 종료된 채 남는다(공식 문서: desktop-to-uwp 업데이트 후 재시작).
/// </summary>
public static class UpdateRestartService
{
    // 크래시(미처리 예외)·행(무응답)으로 종료될 때는 재시작하지 않는다 — 무한 재시작 루프 방지.
    // 업데이트(패치)·재부팅으로 종료될 때는 재시작한다(RESTART_NO_PATCH/RESTART_NO_REBOOT를 설정하지 않음).
    private const uint RESTART_NO_CRASH = 1;
    private const uint RESTART_NO_HANG = 2;

    /// <summary>현재 프로세스를 업데이트 후 자동 재시작 대상으로 등록한다(상주 시작 시 1회).</summary>
    public static void RegisterForRestart()
    {
        // 재시작 시 트레이에만 상주(메인 창 미표시)하도록 무음 시작 플래그를 명령줄로 전달한다.
        // 실행 파일 이름은 API가 자동으로 붙이므로 인자만 지정한다.
        var hr = RegisterApplicationRestart(GroupArgs.SilentFlag, RESTART_NO_CRASH | RESTART_NO_HANG);
        if (hr != 0) // S_OK = 0
        {
            // 등록 실패는 앱 동작을 막지 않는다(best-effort). 다만 업데이트 후 재시작이 안 될 수 있으므로
            // 현장 추적이 가능하도록 경고만 남긴다.
            // 정적 클래스는 ILogger<T>의 형식 인수로 쓸 수 없어 App을 카테고리로 사용한다(기존 로깅 패턴과 동일).
            App.Services?.GetService<ILogger<App>>()
                ?.LogWarning("RegisterApplicationRestart 실패: HRESULT=0x{Hr:X8}", (uint)hr);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterApplicationRestart(string? pwzCommandline, uint dwFlags);
}
