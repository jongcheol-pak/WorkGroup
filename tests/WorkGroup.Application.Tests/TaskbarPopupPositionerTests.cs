using WorkGroup.Infrastructure.Interop;
using Xunit;

namespace WorkGroup.Application.Tests;

/// <summary>팝업 위치 계산(D4) 순수 로직 검증.</summary>
public class TaskbarPopupPositionerTests
{
    // 1920x1080 모니터, 하단 작업 표시줄 48px
    private static readonly ScreenRect Monitor = new(0, 0, 1920, 1080);
    private static readonly ScreenRect BottomWork = new(0, 0, 1920, 1032);

    [Fact]
    public void DetectEdge_하단_작업표시줄()
        => Assert.Equal(TaskbarEdge.Bottom, TaskbarPopupPositioner.DetectEdge(Monitor, BottomWork));

    [Fact]
    public void DetectEdge_좌측_작업표시줄()
        => Assert.Equal(TaskbarEdge.Left, TaskbarPopupPositioner.DetectEdge(Monitor, new ScreenRect(48, 0, 1920, 1080)));

    [Fact]
    public void DetectEdge_상단_작업표시줄()
        => Assert.Equal(TaskbarEdge.Top, TaskbarPopupPositioner.DetectEdge(Monitor, new ScreenRect(0, 48, 1920, 1080)));

    [Fact]
    public void DetectEdge_우측_작업표시줄()
        => Assert.Equal(TaskbarEdge.Right, TaskbarPopupPositioner.DetectEdge(Monitor, new ScreenRect(0, 0, 1872, 1080)));

    [Fact]
    public void Compute_하단_팝업은_작업영역_하단에_접하고_커서X_중심()
    {
        var p = TaskbarPopupPositioner.Compute(Monitor, BottomWork, cursorX: 960, cursorY: 1050, popupWidth: 300, popupHeight: 400);

        Assert.Equal(1032 - 400, p.Y);     // 작업영역 하단에 접함
        Assert.Equal(960 - 150, p.X);      // 커서 X 중심
    }

    [Fact]
    public void Compute_하단_좌측_경계에서_클램프()
    {
        var p = TaskbarPopupPositioner.Compute(Monitor, BottomWork, cursorX: 10, cursorY: 1050, popupWidth: 300, popupHeight: 400);

        Assert.Equal(0, p.X); // work.Left로 클램프
    }

    [Fact]
    public void Compute_하단_우측_경계에서_클램프()
    {
        var p = TaskbarPopupPositioner.Compute(Monitor, BottomWork, cursorX: 1915, cursorY: 1050, popupWidth: 300, popupHeight: 400);

        Assert.Equal(1920 - 300, p.X); // work.Right - width로 클램프
    }

    [Fact]
    public void Compute_좌측_작업표시줄이면_팝업은_좌변에_접한다()
    {
        var work = new ScreenRect(48, 0, 1920, 1080);
        var p = TaskbarPopupPositioner.Compute(Monitor, work, cursorX: 24, cursorY: 540, popupWidth: 300, popupHeight: 400);

        Assert.Equal(48, p.X);          // 좌변에 접함
        Assert.Equal(540 - 200, p.Y);   // 커서 Y 중심
    }

    [Fact]
    public void Compute_팝업이_작업영역보다_크면_좌상단으로()
    {
        var p = TaskbarPopupPositioner.Compute(Monitor, BottomWork, cursorX: 960, cursorY: 1050, popupWidth: 5000, popupHeight: 400);
        Assert.Equal(0, p.X); // 클램프 불가 시 work.Left
    }

    [Fact]
    public void Compute_팝업은_항상_작업영역_안에_포함된다()
    {
        var work = BottomWork;
        var p = TaskbarPopupPositioner.Compute(Monitor, work, cursorX: 1900, cursorY: 1050, popupWidth: 300, popupHeight: 400);

        Assert.True(p.X >= work.Left);
        Assert.True(p.X + 300 <= work.Right);
        Assert.True(p.Y >= work.Top);
        Assert.True(p.Y + 400 <= work.Bottom);
    }
}
