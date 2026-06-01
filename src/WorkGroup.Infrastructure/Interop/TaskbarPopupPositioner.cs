namespace WorkGroup.Infrastructure.Interop;

/// <summary>화면 좌표 사각형(픽셀).</summary>
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>팝업 배치 위치(좌상단, 픽셀).</summary>
public readonly record struct PopupPlacement(int X, int Y);

/// <summary>작업 표시줄 변.</summary>
public enum TaskbarEdge { Bottom, Top, Left, Right }

/// <summary>
/// 클릭 좌표와 모니터/작업영역 사각형으로 작업 표시줄 변 위에 팝업을 배치하는 순수 계산(plan.md D4).
/// 팝업은 항상 작업영역 안에 완전히 포함되도록 클램프된다.
/// </summary>
public static class TaskbarPopupPositioner
{
    /// <summary>모니터 전체 영역과 작업영역의 차이로 작업 표시줄이 붙은 변을 판정한다.</summary>
    public static TaskbarEdge DetectEdge(ScreenRect monitor, ScreenRect work)
    {
        if (work.Bottom < monitor.Bottom) return TaskbarEdge.Bottom;
        if (work.Top > monitor.Top) return TaskbarEdge.Top;
        if (work.Left > monitor.Left) return TaskbarEdge.Left;
        if (work.Right < monitor.Right) return TaskbarEdge.Right;
        return TaskbarEdge.Bottom; // 판정 불가 시 하단 기본
    }

    /// <summary>
    /// 커서 좌표 기준으로 작업 표시줄 변에 접한 팝업 좌상단 위치를 계산한다.
    /// </summary>
    public static PopupPlacement Compute(
        ScreenRect monitor, ScreenRect work, int cursorX, int cursorY, int popupWidth, int popupHeight)
    {
        var edge = DetectEdge(monitor, work);

        return edge switch
        {
            // 수평 변(하단/상단): 커서 X를 팝업 가로 중심으로, 변에 접하게 세로 배치
            TaskbarEdge.Bottom => new PopupPlacement(
                ClampHorizontal(cursorX - popupWidth / 2, work, popupWidth),
                work.Bottom - popupHeight),
            TaskbarEdge.Top => new PopupPlacement(
                ClampHorizontal(cursorX - popupWidth / 2, work, popupWidth),
                work.Top),

            // 수직 변(좌/우): 커서 Y를 팝업 세로 중심으로, 변에 접하게 가로 배치
            TaskbarEdge.Left => new PopupPlacement(
                work.Left,
                ClampVertical(cursorY - popupHeight / 2, work, popupHeight)),
            TaskbarEdge.Right => new PopupPlacement(
                work.Right - popupWidth,
                ClampVertical(cursorY - popupHeight / 2, work, popupHeight)),

            _ => new PopupPlacement(
                ClampHorizontal(cursorX - popupWidth / 2, work, popupWidth),
                work.Bottom - popupHeight)
        };
    }

    private static int ClampHorizontal(int x, ScreenRect work, int popupWidth)
        => Clamp(x, work.Left, work.Right - popupWidth);

    private static int ClampVertical(int y, ScreenRect work, int popupHeight)
        => Clamp(y, work.Top, work.Bottom - popupHeight);

    private static int Clamp(int value, int min, int max)
    {
        // 팝업이 작업영역보다 큰 경우 min이 max보다 커질 수 있으므로 min을 우선한다.
        if (max < min) return min;
        return Math.Clamp(value, min, max);
    }
}
