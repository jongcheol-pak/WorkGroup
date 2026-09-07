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
