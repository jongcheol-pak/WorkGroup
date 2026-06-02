using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkGroup.Application.Groups;
using WorkGroup.Application.Inventory;
using WorkGroup.Domain.Groups;
// 전역 using의 Microsoft.UI.Xaml.Controls.IconSource와 충돌하므로 도메인 타입으로 별칭 고정.
using IconSource = WorkGroup.Domain.Groups.IconSource;

namespace WorkGroup.App.ViewModels;

/// <summary>
/// 메인 화면 ViewModel(plan.md T9). 설치 앱 목록·그룹 목록을 보여주고,
/// 그룹 생성/수정/삭제를 GroupAppService로 위임한다.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IAppInventory _inventory;
    private readonly IGroupAppService _groupService;

    private readonly List<AppEntry> _allApps = new();
    private GroupId? _editingId;

    public MainViewModel(IAppInventory inventory, IGroupAppService groupService)
    {
        _inventory = inventory;
        _groupService = groupService;

        // partial property는 선언부 초기화가 불가하므로 기본값을 생성자에서 설정.
        SearchText = string.Empty;
        EditingName = string.Empty;
        SelectedIconOption = "기본";
        StatusMessage = string.Empty;
    }

    /// <summary>검색어로 필터링된 설치 앱 목록.</summary>
    public ObservableCollection<AppEntry> InstalledApps { get; } = new();

    /// <summary>저장된 그룹 목록.</summary>
    public ObservableCollection<AppGroup> Groups { get; } = new();

    /// <summary>편집 중 그룹의 멤버 목록.</summary>
    public ObservableCollection<AppEntry> EditingApps { get; } = new();

    /// <summary>내장 아이콘 선택지(표시명 = 키).</summary>
    public IReadOnlyList<string> IconOptions { get; } =
        new[] { "기본", "빨강", "초록", "주황", "보라", "첫 멤버 앱", "이미지 선택..." };

    // WinUI/CsWinRT AOT 호환을 위해 [ObservableProperty]는 partial property로 선언한다(MVVMTK0045).
    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    public partial string EditingName { get; set; }

    [ObservableProperty]
    public partial string SelectedIconOption { get; set; }

    [ObservableProperty]
    public partial string? CustomImagePath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>상태 메시지 표시 여부(InfoBar.IsOpen 바인딩용).</summary>
    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>설치 앱과 그룹을 불러온다.</summary>
    public async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            _allApps.Clear();
            _allApps.AddRange(await _inventory.GetInstalledAppsAsync());
            ApplyFilter();
            await ReloadGroupsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"불러오기 실패: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        InstalledApps.Clear();
        var query = _allApps.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(a => a.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        foreach (var app in query)
            InstalledApps.Add(app);
    }

    private async Task ReloadGroupsAsync()
    {
        var groups = await _groupService.GetAllAsync();
        Groups.Clear();
        foreach (var g in groups)
            Groups.Add(g);
    }

    [RelayCommand]
    private void NewGroup()
    {
        _editingId = null;
        EditingName = string.Empty;
        EditingApps.Clear();
        SelectedIconOption = "기본";
        CustomImagePath = null;
        StatusMessage = "새 그룹을 구성하세요.";
    }

    [RelayCommand]
    private void EditGroup(AppGroup? group)
    {
        if (group is null) return;
        _editingId = group.Id;
        EditingName = group.Name;
        EditingApps.Clear();
        foreach (var a in group.Apps)
            EditingApps.Add(a);
        (SelectedIconOption, CustomImagePath) = DescribeIcon(group.Icon);
    }

    [RelayCommand]
    private void AddApp(AppEntry? app)
    {
        if (app is null) return;
        if (EditingApps.Any(a => a.SameTarget(app.LaunchTarget)))
            return;
        EditingApps.Add(app);
    }

    [RelayCommand]
    private void RemoveApp(AppEntry? app)
    {
        if (app is null) return;
        var existing = EditingApps.FirstOrDefault(a => a.SameTarget(app.LaunchTarget));
        if (existing is not null)
            EditingApps.Remove(existing);
    }

    [RelayCommand]
    private async Task SaveGroup()
    {
        if (string.IsNullOrWhiteSpace(EditingName))
        {
            StatusMessage = "그룹 이름을 입력하세요.";
            return;
        }

        var icon = BuildIconSource();
        var group = _editingId is null
            ? CreateNew(icon)
            : AppGroup.Restore(_editingId, EditingName.Trim(), icon, EditingApps);

        if (group is null)
            return;

        IsBusy = true;
        try
        {
            var result = await _groupService.SaveAsync(group);
            if (result.IsFailure)
            {
                StatusMessage = result.Error ?? "저장 실패";
                return;
            }
            StatusMessage = $"'{group.Name}' 저장됨.";
            _editingId = group.Id;
            await ReloadGroupsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteGroup(AppGroup? group)
    {
        if (group is null) return;
        IsBusy = true;
        try
        {
            await _groupService.DeleteAsync(group.Id);
            if (_editingId == group.Id)
                NewGroup();
            await ReloadGroupsAsync();
            StatusMessage = $"'{group.Name}' 삭제됨.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private AppGroup? CreateNew(IconSource icon)
    {
        var created = AppGroup.Create(EditingName.Trim(), icon);
        if (created.IsFailure)
        {
            StatusMessage = created.Error ?? "그룹 생성 실패";
            return null;
        }
        var group = created.Value;
        foreach (var app in EditingApps)
            group.AddApp(app);
        return group;
    }

    private IconSource BuildIconSource() => SelectedIconOption switch
    {
        "이미지 선택..." when !string.IsNullOrWhiteSpace(CustomImagePath) => IconSource.FromCustomImage(CustomImagePath!),
        "첫 멤버 앱" when EditingApps.Count > 0 => IconSource.FromMemberApp(EditingApps[0].LaunchTarget),
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
}
