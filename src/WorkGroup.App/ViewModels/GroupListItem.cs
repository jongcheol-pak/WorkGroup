using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WorkGroup.App.Services;
using WorkGroup.Domain.Groups;

namespace WorkGroup.App.ViewModels;

/// <summary>
/// 작업 그룹 목록의 한 항목(plan.md T7/DU7). 그룹 아이콘(.ico)과 2번째 줄의 멤버 앱 미니 아이콘을 제공한다.
/// </summary>
public sealed partial class GroupListItem : ObservableObject
{
    // 2번째 줄에 표시할 멤버 아이콘 최대 개수(초과분은 "+N").
    private const int MaxMemberIcons = 8;

    public GroupListItem(AppGroup group)
    {
        Group = group;
        Name = group.Name;
        CanReorder = true;
    }

    public AppGroup Group { get; }

    public string Name { get; }

    /// <summary>그룹 아이콘 이미지(.ico). 로드 실패 시 null이며 <see cref="IconFallback"/> 색을 사용.</summary>
    [ObservableProperty]
    public partial ImageSource? Icon { get; set; }

    /// <summary>.ico를 못 쓸 때의 폴백 배경색.</summary>
    [ObservableProperty]
    public partial Brush? IconFallback { get; set; }

    /// <summary>멤버 앱 미니 아이콘(최대 8개).</summary>
    public ObservableCollection<PopupAppItem> MemberIcons { get; } = new();

    /// <summary>표시 상한을 넘는 멤버 수("+N", 없으면 null).</summary>
    [ObservableProperty]
    public partial string? MoreCount { get; set; }

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

    public async Task LoadAsync()
    {
        // fire-and-forget로 호출되므로 예외를 자체 흡수한다(미관측 시 위험). 실패해도 기본 색으로 표시.
        try
        {
            await LoadGroupIconAsync();
            LoadMemberIcons();
        }
        catch
        {
            IconFallback = new SolidColorBrush(GroupIconLoader.ColorForBuiltIn("default"));
        }
    }

    private async Task LoadGroupIconAsync()
    {
        // 목록 표시는 원본 해상도 PNG를 우선 사용한다(.ico 프레임 디코드 없이 GPU 축소 → 선명).
        // PNG가 없으면(옛 그룹 등) .ico로 폴백한다. 둘 다 캐시를 무시해 수정 즉시 반영.
        var png = GroupIconLoader.GetPngPath(Group.Id);
        if (File.Exists(png) && TrySetIconFromFile(png))
            return;

        var ico = GroupIconLoader.GetIconPath(Group.Id);
        if (File.Exists(ico) && TrySetIconFromFile(ico))
            return;

        await ApplyFallbackAsync();
    }

    /// <summary>파일(.png/.ico)을 BitmapImage로 로드해 Icon에 설정한다(캐시 무시, 실패 시 폴백). 성공 시 true.</summary>
    private bool TrySetIconFromFile(string path)
    {
        try
        {
            var bmp = new BitmapImage { CreateOptions = BitmapCreateOptions.IgnoreImageCache };
            // 디코드 실패 시 IconSource 기반 폴백으로 전환한다(plan.md M2).
            bmp.ImageFailed += (_, _) => _ = ApplyFallbackAsync();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            Icon = bmp;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ApplyFallbackAsync()
    {
        // ImageFailed 콜백에서도 호출되므로 예외를 자체 흡수한다(미관측 방지).
        try
        {
            Icon = null;
            switch (Group.Icon.Kind)
            {
                case IconSourceKind.MemberApp:
                    var first = Group.Apps.FirstOrDefault();
                    if (first is not null)
                        Icon = await AppIconLoader.LoadAsync(first);
                    else
                        IconFallback = new SolidColorBrush(GroupIconLoader.ColorForBuiltIn("default"));
                    break;

                case IconSourceKind.CustomImage:
                    if (File.Exists(Group.Icon.Value))
                        Icon = new BitmapImage(new Uri(Group.Icon.Value, UriKind.Absolute));
                    else
                        IconFallback = new SolidColorBrush(GroupIconLoader.ColorForBuiltIn("default"));
                    break;

                default: // BuiltIn
                    IconFallback = new SolidColorBrush(GroupIconLoader.ColorForBuiltIn(Group.Icon.Value));
                    break;
            }
        }
        catch
        {
            IconFallback = new SolidColorBrush(GroupIconLoader.ColorForBuiltIn("default"));
        }
    }

    private void LoadMemberIcons()
    {
        MemberIcons.Clear();
        foreach (var app in Group.Apps.Take(MaxMemberIcons))
        {
            var item = new PopupAppItem(app);
            MemberIcons.Add(item);
            _ = item.LoadIconAsync();
        }
        var overflow = Group.Apps.Count - MaxMemberIcons;
        MoreCount = overflow > 0 ? $"+{overflow}" : null;
    }
}
