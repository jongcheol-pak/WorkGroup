using CommunityToolkit.Mvvm.ComponentModel;
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
    [NotifyPropertyChangedFor(nameof(ReorderHandleVisibility))]
    public partial bool CanReorder { get; set; }

    /// <summary>순서 변경 핸들 표시 여부(x:Bind는 bool→Visibility 자동변환 없음).</summary>
    public Visibility ReorderHandleVisibility => CanReorder ? Visibility.Visible : Visibility.Collapsed;

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
