using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;
using WorkGroup.App.Services;
using WorkGroup.Domain.Folders;

namespace WorkGroup.App.ViewModels;

/// <summary>폴더 바로가기 목록의 한 항목. 폴더 셸 아이콘을 로드해 제공한다.</summary>
public sealed partial class FolderShortcutItem : ObservableObject
{
    public FolderShortcutItem(FolderShortcut shortcut)
    {
        Shortcut = shortcut;
        CanReorder = true;
    }

    public FolderShortcut Shortcut { get; }

    public int Id => Shortcut.Id;
    public string Name => Shortcut.Name;
    public string Path => Shortcut.Path;

    /// <summary>폴더 셸 아이콘. 로드 실패 시 null(플레이스홀더).</summary>
    [ObservableProperty]
    public partial ImageSource? Icon { get; set; }

    /// <summary>
    /// 드래그 순서 변경 가능 여부. 검색 중에는 목록이 부분집합이라 전체 순서를 확정할 수 없어 false가 된다.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReorderHandleOpacity))]
    [NotifyPropertyChangedFor(nameof(ReorderHandleAccessibilityView))]
    public partial bool CanReorder { get; set; }

    /// <summary>
    /// 순서 변경 핸들의 불투명도. Visibility로 접으면 열 폭이 0이 되는데 Grid.ColumnSpacing은
    /// 그래도 적용돼 카드 내용이 좌우로 밀린다 — 자리를 유지한 채 보이지 않게 한다.
    /// </summary>
    public double ReorderHandleOpacity => CanReorder ? 1.0 : 0.0;

    /// <summary>보이지 않는 핸들은 접근성 트리에서도 뺀다(조작할 수 없는 것을 읽지 않도록).</summary>
    public AccessibilityView ReorderHandleAccessibilityView
        => CanReorder ? AccessibilityView.Content : AccessibilityView.Raw;

    public async Task LoadIconAsync()
    {
        try
        {
            Icon = await FolderIconLoader.LoadAsync(Shortcut.Path);
        }
        catch
        {
            // 실패 시 플레이스홀더 유지.
        }
    }
}
