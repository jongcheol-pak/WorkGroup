using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WorkGroup.App.Services;
using WorkGroup.Application.Groups;
using WorkGroup.Application.Inventory;
using WorkGroup.Domain.Groups;
// 전역 using의 Microsoft.UI.Xaml.Controls.IconSource와 충돌하므로 도메인 타입으로 별칭 고정.
using IconSource = WorkGroup.Domain.Groups.IconSource;

namespace WorkGroup.App.ViewModels;

/// <summary>
/// 그룹 추가/수정 다이얼로그 ViewModel(plan.md T6). 설치 앱 체크 선택 + 이름/아이콘 설정 후
/// GroupAppService로 저장한다. 신규/편집 모드를 모두 다룬다.
/// </summary>
public sealed partial class GroupEditViewModel : ObservableObject
{
    private readonly IAppInventory _inventory;
    private readonly IGroupAppService _groupService;

    private readonly List<SelectableAppItem> _allApps = new();
    private GroupId? _editingId;

    public GroupEditViewModel(IAppInventory inventory, IGroupAppService groupService)
    {
        _inventory = inventory;
        _groupService = groupService;

        Title = "그룹 추가";
        EditingName = string.Empty;
        SearchText = string.Empty;
        SelectedIconOption = "기본";
        StatusMessage = string.Empty;
    }

    /// <summary>검색어로 필터링된 설치 앱 목록(체크박스 선택).</summary>
    public ObservableCollection<SelectableAppItem> Apps { get; } = new();

    /// <summary>아이콘 소스 선택지(표시명 = 키, plan.md DU3 기존 옵션 유지).</summary>
    public IReadOnlyList<string> IconOptions { get; } =
        new[] { "기본", "빨강", "초록", "주황", "보라", "첫 멤버 앱", "이미지 선택..." };

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string EditingName { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    public partial string SelectedIconOption { get; set; }

    [ObservableProperty]
    public partial string? CustomImagePath { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusMessage { get; set; }

    /// <summary>미리보기 배경색(내장 색상 아이콘일 때).</summary>
    [ObservableProperty]
    public partial Brush? PreviewColor { get; set; }

    /// <summary>미리보기 이미지(멤버 앱/사용자 이미지일 때).</summary>
    [ObservableProperty]
    public partial ImageSource? PreviewImage { get; set; }

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>신규(group=null) 또는 편집 모드로 초기화하고 설치 앱을 로드한다(plan.md DU8 lazy 로드).</summary>
    public async Task InitializeAsync(AppGroup? group)
    {
        IsLoading = true;
        try
        {
            _editingId = group?.Id;
            Title = group is null ? "그룹 추가" : "그룹 수정";
            EditingName = group?.Name ?? string.Empty;
            (SelectedIconOption, CustomImagePath) = group is null
                ? ("기본", null)
                : DescribeIcon(group.Icon);

            var selectedTargets = group is null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : group.Apps.Select(a => a.LaunchTarget).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var apps = await _inventory.GetInstalledAppsAsync();
            _allApps.Clear();
            foreach (var app in apps)
            {
                var item = new SelectableAppItem(app) { IsSelected = selectedTargets.Contains(app.LaunchTarget) };
                _allApps.Add(item);
                _ = item.LoadIconAsync();
            }
            ApplyFilter();
            await RefreshPreviewAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"앱 목록을 불러오지 못했습니다: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>확인 클릭 시 호출. 저장 성공 시 true(다이얼로그 닫힘), 실패 시 false(유지).</summary>
    public async Task<bool> SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(EditingName))
        {
            StatusMessage = "그룹 이름을 입력하세요.";
            return false;
        }

        var selected = _allApps.Where(a => a.IsSelected).Select(a => a.App).ToList();
        var icon = BuildIconSource(selected);

        AppGroup group;
        if (_editingId is null)
        {
            var created = AppGroup.Create(EditingName.Trim(), icon);
            if (created.IsFailure)
            {
                StatusMessage = created.Error ?? "그룹 생성 실패";
                return false;
            }
            group = created.Value;
            foreach (var app in selected)
                group.AddApp(app);
        }
        else
        {
            group = AppGroup.Restore(_editingId, EditingName.Trim(), icon, selected);
        }

        var result = await _groupService.SaveAsync(group);
        if (result.IsFailure)
        {
            StatusMessage = result.Error ?? "저장 실패";
            return false;
        }
        return true;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    async partial void OnSelectedIconOptionChanged(string value)
    {
        // async partial void는 이벤트 핸들러처럼 동작하므로 예외를 자체 흡수한다(미관측 시 프로세스 종료 위험).
        try { await RefreshPreviewAsync(); }
        catch (Exception ex) { StatusMessage = $"미리보기 갱신 실패: {ex.Message}"; }
    }

    async partial void OnCustomImagePathChanged(string? value)
    {
        try { await RefreshPreviewAsync(); }
        catch (Exception ex) { StatusMessage = $"미리보기 갱신 실패: {ex.Message}"; }
    }

    private void ApplyFilter()
    {
        Apps.Clear();
        IEnumerable<SelectableAppItem> query = _allApps;
        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(a => a.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        foreach (var item in query)
            Apps.Add(item);
    }

    private async Task RefreshPreviewAsync()
    {
        PreviewImage = null;
        PreviewColor = null;

        switch (SelectedIconOption)
        {
            case "이미지 선택..." when !string.IsNullOrWhiteSpace(CustomImagePath) && File.Exists(CustomImagePath):
                PreviewImage = new BitmapImage(new Uri(CustomImagePath!, UriKind.Absolute));
                break;
            case "첫 멤버 앱":
                var first = _allApps.FirstOrDefault(a => a.IsSelected);
                if (first is not null)
                    PreviewImage = await AppIconLoader.LoadAsync(first.App);
                break;
            case "이미지 선택...":
                break;
            default:
                PreviewColor = new SolidColorBrush(ColorForOption(SelectedIconOption));
                break;
        }
    }

    private IconSource BuildIconSource(IReadOnlyList<AppEntry> selected) => SelectedIconOption switch
    {
        "이미지 선택..." when !string.IsNullOrWhiteSpace(CustomImagePath) => IconSource.FromCustomImage(CustomImagePath!),
        "첫 멤버 앱" when selected.Count > 0 => IconSource.FromMemberApp(selected[0].LaunchTarget),
        "빨강" => IconSource.BuiltIn("red"),
        "초록" => IconSource.BuiltIn("green"),
        "주황" => IconSource.BuiltIn("orange"),
        "보라" => IconSource.BuiltIn("purple"),
        _ => IconSource.DefaultBuiltIn
    };

    private static (string Option, string? Custom) DescribeIcon(IconSource icon) => icon.Kind switch
    {
        IconSourceKind.CustomImage => ("이미지 선택...", icon.Value),
        IconSourceKind.MemberApp => ("첫 멤버 앱", null),
        _ => (icon.Value switch
        {
            "red" => "빨강",
            "green" => "초록",
            "orange" => "주황",
            "purple" => "보라",
            _ => "기본"
        }, null)
    };

    // 미리보기 색상(내장 색상 아이콘의 대략적 표현). 실제 .ico는 IconService가 생성한다.
    private static Windows.UI.Color ColorForOption(string option) => option switch
    {
        "빨강" => new Windows.UI.Color { A = 255, R = 0xE8, G = 0x11, B = 0x23 },
        "초록" => new Windows.UI.Color { A = 255, R = 0x10, G = 0x7C, B = 0x10 },
        "주황" => new Windows.UI.Color { A = 255, R = 0xF7, G = 0x63, B = 0x0C },
        "보라" => new Windows.UI.Color { A = 255, R = 0x5C, G = 0x2D, B = 0x91 },
        _ => new Windows.UI.Color { A = 255, R = 0x51, G = 0x2B, B = 0xD4 }
    };
}
