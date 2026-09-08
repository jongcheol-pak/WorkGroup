using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.Imaging;
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

    /// <summary>
    /// 드래그 비주얼 최대 폭(논리 px) — `ContentMaxWidth`(1024)와 같은 값이라 현재 레이아웃에서는
    /// 축소가 걸리지 않고 카드가 원본 크기로 따라온다(그룹 수정 화면의 내장 재정렬과 같은 모습).
    /// 이보다 작게 잡으면 넓은 창에서 카드가 판독 불가능한 띠로 줄어든다 — 폭 1024 카드를 320으로 줄이면
    /// 높이가 25px가 되어 14px 이름이 4px로 렌더된다. 향후 레이아웃이 더 넓어질 때를 위한 안전판이다.
    /// </summary>
    private const int MaxDragVisualWidth = 1024;

    /// <summary>커서 위치(목록 기준 좌표)로 끼워 넣을 자리와 표시선 위치를 함께 구한다.</summary>
    public static DropTarget ResolveDropTarget(ListView list, Point position)
    {
        var realized = GetRealizedItems(list);
        var slot = ListInsertionPoint.Resolve(realized.Bounds, position.Y);

        // 슬롯은 "실현된 것들 중 몇 번째"이므로 전체 컬렉션 인덱스로 되돌린다(가상화 보정은 순수 계산 쪽).
        var insertionIndex = ListInsertionPoint.ResolveActualIndex(realized.Indexes, slot, list.Items.Count);
        return new DropTarget(insertionIndex, ListInsertionPoint.IndicatorOffset(realized.Bounds, slot));
    }

    /// <summary>
    /// 끌고 있는 항목의 카드 모습을 그대로 드래그 비주얼로 지정한다(핸들만 따라다니면 무엇을 옮기는지 보이지 않는다).
    /// 화면에 쓰이는 라이브 이미지를 넘기면 드래그 표면에 빈 그림으로 렌더되므로(notes.md 2026-06-02 실측),
    /// 컨테이너를 새로 렌더해 <see cref="SoftwareBitmap"/>(BGRA8 Premultiplied)으로 넘긴다.
    /// 실패하면 아무것도 지정하지 않는다 — 비주얼은 부가 표시라, 여기서 막으면 순서 변경 자체가 퇴행한다.
    /// </summary>
    public static async Task SetDragVisualFromItemAsync(ListView list, int index, DragStartingEventArgs e)
    {
        if (list.ContainerFromIndex(index) is not ListViewItem container)
            return;

        try
        {
            var (width, height) = ScaleToMaxWidth(container.ActualWidth, container.ActualHeight);
            if (width <= 0 || height <= 0)
                return;

            var rendered = new RenderTargetBitmap();
            await rendered.RenderAsync(container, width, height);

            var pixels = await rendered.GetPixelsAsync();
            var surface = SoftwareBitmap.CreateCopyFromBuffer(
                pixels, BitmapPixelFormat.Bgra8, rendered.PixelWidth, rendered.PixelHeight,
                BitmapAlphaMode.Premultiplied);
            e.DragUI.SetContentFromSoftwareBitmap(surface);
        }
        catch
        {
            /* 렌더 실패는 무음 — 드래그는 기본 비주얼로 계속된다 */
        }
    }

    /// <summary>최대 폭을 넘는 카드만 종횡비를 보존해 줄인다(업스케일 금지).</summary>
    private static (int Width, int Height) ScaleToMaxWidth(double width, double height)
    {
        if (width <= MaxDragVisualWidth)
            return ((int)Math.Round(width), (int)Math.Round(height));

        return (MaxDragVisualWidth, (int)Math.Round(height * (MaxDragVisualWidth / width)));
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
