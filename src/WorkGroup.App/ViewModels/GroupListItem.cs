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
        // CustomImage(사용자/리소스 이미지)는 원본을 직접 로드한다 → 편집 미리보기와 동일하게 선명
        // (.ico의 32/48 프레임은 미리 축소·재인코딩돼 흐림).
        if (TryLoadCustomImage())
            return;

        var path = GroupIconLoader.GetIconPath(Group.Id);
        if (File.Exists(path))
        {
            var bmp = new BitmapImage
            {
                // 표시 크기(32 logical) 기준으로 DPI에 맞춰 디코드 → 확대 블러 방지.
                DecodePixelType = DecodePixelType.Logical,
                DecodePixelWidth = 32,
                DecodePixelHeight = 32,
                // 같은 경로(.ico)로 재로드 시 캐시된 옛 아이콘이 나오지 않도록 캐시를 무시한다(수정 즉시 반영).
                CreateOptions = BitmapCreateOptions.IgnoreImageCache
            };
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

    /// <summary>CustomImage(ms-appx 리소스/사용자 파일) 원본을 네이티브 해상도로 로드한다(선명). 성공 시 true.</summary>
    private bool TryLoadCustomImage()
    {
        if (Group.Icon.Kind != IconSourceKind.CustomImage)
            return false;

        var value = Group.Icon.Value;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        // ms-appx 리소스는 절대 URI, 사용자 이미지는 실파일 경로. 파일은 존재할 때만.
        var isMsAppx = value.StartsWith("ms-appx", StringComparison.OrdinalIgnoreCase);
        if (!isMsAppx && !File.Exists(value))
            return false;

        try
        {
            // DecodePixelWidth 미지정(네이티브 디코드) → 32px 타일로 축소 표시되어도 선명(편집 미리보기와 동일).
            var bmp = new BitmapImage { CreateOptions = BitmapCreateOptions.IgnoreImageCache };
            bmp.ImageFailed += (_, _) => _ = ApplyFallbackAsync();
            bmp.UriSource = new Uri(value, UriKind.Absolute);
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
