using Windows.Foundation;
using WorkGroup.Infrastructure.Ui;

namespace WorkGroup.App.Views;

/// <summary>
/// 목록 드래그 재정렬의 ListView 어댑터. 컨테이너에서 항목 경계를 뽑아
/// <see cref="ListInsertionPoint"/>(순수 계산)에 넘기는 얇은 껍데기다.
/// 두 목록 페이지(작업 그룹·트레이 메뉴)가 이 코드를 공유해 조작 결과가 갈리지 않게 한다.
/// </summary>
internal static class ReorderDrop
{
    /// <summary>
    /// 재정렬 드래그를 식별하는 데이터 포맷. 값은 끌고 있는 항목의 인덱스(문자열)다.
    /// 이 포맷이 없는 드래그(외부 파일, 그룹 카드의 작업 표시줄 핀 등)는 드롭 대상으로 받지 않는다.
    /// </summary>
    public const string IndexFormat = "WorkGroup/ReorderIndex";

    /// <summary>커서 위치(목록 기준 좌표)로 끼워 넣을 자리를 고른다.</summary>
    public static int ResolveInsertionIndex(ListView list, Point position)
        => ListInsertionPoint.Resolve(GetItemBounds(list), position.Y);

    /// <summary>삽입 표시선을 놓을 세로 오프셋(목록 기준 좌표)을 돌려준다.</summary>
    public static double GetIndicatorOffset(ListView list, int insertionIndex)
        => ListInsertionPoint.IndicatorOffset(GetItemBounds(list), insertionIndex);

    /// <summary>
    /// 삽입 자리를 실제 이동 대상 인덱스로 바꾼다 — 끌던 항목이 목록에서 빠지면서
    /// 그보다 뒤의 자리는 한 칸씩 당겨지기 때문이다. 제자리면 null.
    /// </summary>
    public static int? ResolveMoveTarget(int fromIndex, int insertionIndex, int itemCount)
    {
        var to = insertionIndex > fromIndex ? insertionIndex - 1 : insertionIndex;
        if (to == fromIndex || to < 0 || to >= itemCount)
            return null;
        return to;
    }

    // 실현된 컨테이너만 좌표를 갖는다. 가상화로 아직 만들어지지 않은 항목은 건너뛰므로,
    // 화면 밖까지 끌어 스크롤된 경우에도 보이는 범위 기준으로 자리를 고른다.
    private static List<ItemBounds> GetItemBounds(ListView list)
    {
        var bounds = new List<ItemBounds>(list.Items.Count);
        for (var i = 0; i < list.Items.Count; i++)
        {
            if (list.ContainerFromIndex(i) is not ListViewItem container)
                continue;

            var top = container.TransformToVisual(list).TransformPoint(new Point(0, 0)).Y;
            bounds.Add(new ItemBounds(top, container.ActualHeight));
        }

        return bounds;
    }
}
