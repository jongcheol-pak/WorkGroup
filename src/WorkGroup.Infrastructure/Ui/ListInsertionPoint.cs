namespace WorkGroup.Infrastructure.Ui;

/// <summary>목록 항목 하나의 세로 위치(컨테이너 기준, DIP).</summary>
public readonly record struct ItemBounds(double Top, double Height)
{
    public double Bottom => Top + Height;
    public double Center => Top + (Height / 2);
}

/// <summary>
/// 드래그 중인 커서의 세로 좌표로 "몇 번째 자리에 끼워 넣을 것인가"를 계산하는 순수 로직.
/// UI 타입(ListView·Point)에 의존하지 않아 단위 테스트할 수 있고, 두 목록 페이지가 같은 결과를 쓴다.
/// </summary>
public static class ListInsertionPoint
{
    /// <summary>
    /// 삽입 인덱스를 돌려준다 — 항목의 세로 중점보다 위에 있으면 그 항목 앞자리,
    /// 모든 항목의 중점보다 아래면 맨 끝자리(= 항목 수)다. 빈 목록은 0.
    /// </summary>
    public static int Resolve(IReadOnlyList<ItemBounds> items, double y)
    {
        ArgumentNullException.ThrowIfNull(items);

        for (var i = 0; i < items.Count; i++)
        {
            if (y < items[i].Center)
                return i;
        }

        return items.Count;
    }

    /// <summary>
    /// 삽입 표시선을 그릴 세로 오프셋 — 그 자리 항목의 위쪽 경계이고,
    /// 끝자리면 마지막 항목의 아래쪽 경계다. 빈 목록은 0.
    /// </summary>
    public static double IndicatorOffset(IReadOnlyList<ItemBounds> items, int insertionIndex)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
            return 0;
        if (insertionIndex >= items.Count)
            return items[^1].Bottom;

        return items[Math.Max(insertionIndex, 0)].Top;
    }

    /// <summary>
    /// 삽입할 자리를 실제 이동 대상 인덱스로 바꾼다 — 끌던 항목이 목록에서 빠지면서
    /// 그보다 뒤의 자리는 한 칸씩 당겨지기 때문이다. 제자리이거나 범위를 벗어나면 null.
    /// </summary>
    public static int? ResolveMoveTarget(int fromIndex, int insertionIndex, int itemCount)
    {
        var to = insertionIndex > fromIndex ? insertionIndex - 1 : insertionIndex;
        if (to == fromIndex || to < 0 || to >= itemCount)
            return null;
        return to;
    }
}
