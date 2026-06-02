using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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
        var path = GroupIconLoader.GetIconPath(Group.Id);
        if (File.Exists(path))
        {
            // DecodePixelWidth를 지정하면 .ico의 작은 프레임이 선택돼 흐려질 수 있다.
            // 크기를 지정하지 않고 큰 프레임을 네이티브로 디코드한 뒤 Image(32px)가 GPU로 축소 → 선명(편집 미리보기와 동일).
            // 같은 경로(.ico) 재로드 시 캐시된 옛 아이콘 방지를 위해 캐시는 무시한다(수정 즉시 반영).
            var bmp = new BitmapImage { CreateOptions = BitmapCreateOptions.IgnoreImageCache };
            // 디코드 실패 시 IconSource 기반 폴백으로 전환한다(plan.md M2).
            bmp.ImageFailed += (_, _) => _ = ApplyFallbackAsync();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            Icon = bmp;
        }
        else
        {
            await ApplyFallbackAsync();
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
