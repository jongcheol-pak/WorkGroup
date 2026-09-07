using Windows.Foundation;
using WorkGroup.Infrastructure.Ui;

namespace WorkGroup.App.Views;

/// <summary>드롭 지점 — 끼워 넣을 자리(전체 컬렉션 기준)와 표시선을 놓을 세로 오프셋.</summary>
internal readonly record struct DropTarget(int InsertionIndex, double IndicatorOffset);

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

    /// <summary>커서 위치(목록 기준 좌표)로 끼워 넣을 자리와 표시선 위치를 함께 구한다.</summary>
    public static DropTarget ResolveDropTarget(ListView list, Point position)
    {
        var realized = GetRealizedItems(list);
        var slot = ListInsertionPoint.Resolve(realized.Bounds, position.Y);

        // 슬롯은 "실현된 것들 중 몇 번째"이므로 전체 컬렉션 인덱스로 되돌린다 —
        // 가상화로 앞쪽 항목이 재활용되면 둘이 어긋나고, 그대로 쓰면 엉뚱한 자리로 옮겨진다.
        var insertionIndex = slot < realized.Indexes.Count ? realized.Indexes[slot] : list.Items.Count;
        return new DropTarget(insertionIndex, ListInsertionPoint.IndicatorOffset(realized.Bounds, slot));
    }

    // 화면에 실현된 컨테이너만 좌표를 갖는다. 가상화로 아직 만들어지지 않았거나 재활용된 항목은
    // 건너뛰되, 그 항목들의 전체 컬렉션 인덱스를 함께 들고 나가 좌표계 혼선을 막는다.
    private static (List<ItemBounds> Bounds, List<int> Indexes) GetRealizedItems(ListView list)
    {
        var bounds = new List<ItemBounds>(list.Items.Count);
        var indexes = new List<int>(list.Items.Count);

        for (var i = 0; i < list.Items.Count; i++)
        {
            if (list.ContainerFromIndex(i) is not ListViewItem container)
                continue;

            var top = container.TransformToVisual(list).TransformPoint(new Point(0, 0)).Y;
            bounds.Add(new ItemBounds(top, container.ActualHeight));
            indexes.Add(i);
        }

        return (bounds, indexes);
    }
}
