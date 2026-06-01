using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace WorkGroup.Infrastructure.Interop;

/// <summary>현재 커서가 위치한 모니터의 전체/작업영역과 커서 좌표를 제공한다(plan.md D4 좌표 수집).</summary>
public sealed class ScreenMetricsProvider
{
    public readonly record struct Metrics(int CursorX, int CursorY, ScreenRect Monitor, ScreenRect Work);

    /// <summary>활성화 시점에 호출해 커서 좌표와 해당 모니터 사각형을 캡처한다.</summary>
    public Metrics Capture()
    {
        PInvoke.GetCursorPos(out var pt);

        var hMonitor = PInvoke.MonitorFromPoint(pt, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        PInvoke.GetMonitorInfo(hMonitor, ref info);

        var monitor = ToScreenRect(info.rcMonitor);
        var work = ToScreenRect(info.rcWork);
        return new Metrics(pt.X, pt.Y, monitor, work);
    }

    private static ScreenRect ToScreenRect(RECT r) => new(r.left, r.top, r.right, r.bottom);
}
