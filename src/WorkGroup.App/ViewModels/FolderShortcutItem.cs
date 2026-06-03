using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using WorkGroup.App.Services;
using WorkGroup.Domain.Folders;

namespace WorkGroup.App.ViewModels;

/// <summary>폴더 바로가기 목록의 한 항목. 폴더 셸 아이콘을 로드해 제공한다.</summary>
public sealed partial class FolderShortcutItem : ObservableObject
{
    public FolderShortcutItem(FolderShortcut shortcut) => Shortcut = shortcut;

    public FolderShortcut Shortcut { get; }

    public int Id => Shortcut.Id;
    public string Name => Shortcut.Name;
    public string Path => Shortcut.Path;

    /// <summary>폴더 셸 아이콘. 로드 실패 시 null(플레이스홀더).</summary>
    [ObservableProperty]
    public partial ImageSource? Icon { get; set; }

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
