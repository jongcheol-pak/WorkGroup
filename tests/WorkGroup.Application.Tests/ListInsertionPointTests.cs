using WorkGroup.Infrastructure.Ui;
using Xunit;

namespace WorkGroup.Application.Tests;

/// <summary>
/// 드래그 재정렬의 삽입 지점 계산 검증. 기대값은 구현식을 옮기지 않고 손으로 계산한 좌표 상수로 적는다.
/// 항목 3개: [0,40) [40,40) [80,40) → 중점은 각각 20 · 60 · 100.
/// </summary>
public class ListInsertionPointTests
{
    private static readonly ItemBounds[] ThreeItems =
    [
        new(0, 40),
        new(40, 40),
        new(80, 40),
    ];

    [Fact]
    public void 빈_목록은_항상_0번_자리()
    {
        Assert.Equal(0, ListInsertionPoint.Resolve([], 0));
        Assert.Equal(0, ListInsertionPoint.Resolve([], 500));
        Assert.Equal(0, ListInsertionPoint.IndicatorOffset([], 0));
    }

    [Theory]
    // 첫 항목 중점(20) 위 → 0번 자리
    [InlineData(0, 0)]
    [InlineData(19.9, 0)]
    // 첫 중점 이상 ~ 둘째 중점(60) 미만 → 1번 자리
    [InlineData(20, 1)]
    [InlineData(59.9, 1)]
    // 둘째 중점 이상 ~ 셋째 중점(100) 미만 → 2번 자리
    [InlineData(60, 2)]
    [InlineData(99.9, 2)]
    // 마지막 중점 아래 → 끝자리(3)
    [InlineData(100, 3)]
    [InlineData(500, 3)]
    public void 커서_세로좌표로_삽입할_자리를_고른다(double y, int expected)
    {
        Assert.Equal(expected, ListInsertionPoint.Resolve(ThreeItems, y));
    }

    [Theory]
    // 각 자리의 표시선은 그 자리 항목의 위쪽 경계
    [InlineData(0, 0)]
    [InlineData(1, 40)]
    [InlineData(2, 80)]
    // 끝자리는 마지막 항목의 아래쪽 경계(80 + 40)
    [InlineData(3, 120)]
    public void 표시선은_삽입할_자리의_위쪽_경계에_놓인다(int insertionIndex, double expected)
    {
        Assert.Equal(expected, ListInsertionPoint.IndicatorOffset(ThreeItems, insertionIndex));
    }

    [Theory]
    // 실현 목록이 전체와 같다(스크롤 없음, 전체 3개) — 슬롯이 그대로 인덱스다
    [InlineData(new[] { 0, 1, 2 }, 0, 3, 0)]
    [InlineData(new[] { 0, 1, 2 }, 2, 3, 2)]
    // 앞쪽이 재활용돼 3번부터만 실현됐다(전체 8개) — 슬롯 0은 3번이다
    [InlineData(new[] { 3, 4, 5 }, 0, 8, 3)]
    [InlineData(new[] { 3, 4, 5 }, 2, 8, 5)]
    // 마지막 실현 항목(5) 아래 = 슬롯 3 → 그 바로 뒷자리 6. 전체 끝(8)이 아니다
    [InlineData(new[] { 3, 4, 5 }, 3, 8, 6)]
    // 실현 목록이 전체 끝까지 간 경우엔 마지막 아래가 곧 전체 끝(3)이다 — 위 케이스와 값이 갈린다
    [InlineData(new[] { 0, 1, 2 }, 3, 3, 3)]
    public void 실현_목록의_자리를_전체_컬렉션_인덱스로_되돌린다(int[] realized, int slot, int totalCount, int expected)
    {
        Assert.Equal(expected, ListInsertionPoint.ResolveActualIndex(realized, slot, totalCount));
    }

    [Fact]
    public void 실현된_항목이_없으면_전체_끝으로_간다()
    {
        // 컨테이너가 아직 하나도 만들어지지 않은 상태(빈 목록·초기 로드) — 끝에 붙인다.
        Assert.Equal(8, ListInsertionPoint.ResolveActualIndex([], 0, totalCount: 8));
        Assert.Equal(0, ListInsertionPoint.ResolveActualIndex([], 5, totalCount: 0));
    }

    [Theory]
    // 항목 5개(0~4) 기준. 끌던 항목이 빠지면 그보다 뒤의 자리는 한 칸 당겨진다.
    // 1번을 4번 자리에 놓으면 → 3번 자리로 간다
    [InlineData(1, 4, 3)]
    // 3번을 1번 자리에 놓으면 → 앞으로 가는 이동은 보정이 없다
    [InlineData(3, 1, 1)]
    // 1번을 끝자리(5)에 놓으면 → 마지막(4번)
    [InlineData(1, 5, 4)]
    // 0번을 맨 앞(0)에 놓으면 제자리 → null
    [InlineData(0, 0, null)]
    // 2번을 자기 바로 뒤 자리(3)에 놓아도 제자리 → null
    [InlineData(2, 3, null)]
    public void 끌던_항목이_빠지는_만큼_대상_인덱스를_보정한다(int fromIndex, int insertionIndex, int? expected)
    {
        Assert.Equal(expected, ListInsertionPoint.ResolveMoveTarget(fromIndex, insertionIndex, itemCount: 5));
    }

    [Fact]
    public void 항목_높이가_제각각이어도_각자의_중점을_기준으로_한다()
    {
        // [0,20) [20,100) → 중점 10 · 70
        ItemBounds[] items = [new(0, 20), new(20, 100)];

        Assert.Equal(0, ListInsertionPoint.Resolve(items, 9));
        Assert.Equal(1, ListInsertionPoint.Resolve(items, 11));
        Assert.Equal(1, ListInsertionPoint.Resolve(items, 69));
        Assert.Equal(2, ListInsertionPoint.Resolve(items, 71));
        Assert.Equal(120, ListInsertionPoint.IndicatorOffset(items, 2));
    }
}
