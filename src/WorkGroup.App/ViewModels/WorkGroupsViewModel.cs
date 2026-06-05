using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WorkGroup.App.Services;
using WorkGroup.Application.Groups;
using WorkGroup.Domain.Groups;

namespace WorkGroup.App.ViewModels;

/// <summary>
/// 작업 그룹 페이지 ViewModel(plan.md T7). 저장된 그룹 목록을 보여주고 삭제를 위임한다.
/// 추가/수정 다이얼로그 표시와 작업 표시줄 드래그는 XamlRoot/HWND가 필요해 페이지 코드비하인드가 담당한다.
/// </summary>
public sealed partial class WorkGroupsViewModel : ObservableObject
{
    private readonly IGroupAppService _groupService;
    private readonly LocalizationService _loc;

    // 전체 그룹(검색 필터의 원본). 아이콘은 로드 시 1회만 받아 둔다(폴더 검색과 동일 패턴).
    private readonly List<GroupListItem> _all = new();

    public WorkGroupsViewModel(IGroupAppService groupService, LocalizationService loc)
    {
        _groupService = groupService;
        _loc = loc;
        StatusMessage = string.Empty;
        SearchText = string.Empty;
    }

    /// <summary>검색 필터가 적용된 표시용 목록.</summary>
    public ObservableCollection<GroupListItem> Groups { get; } = new();

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    [NotifyPropertyChangedFor(nameof(StatusVisibility))]
    public partial string StatusMessage { get; set; }

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>메시지가 없으면 InfoBar 자체를 접어 헤더-목록 간격이 벌어지지 않게 한다.</summary>
    public Visibility StatusVisibility => HasStatus ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>등록된 그룹 수 표시(예: "3 그룹"). 전체 개수 기준(검색과 무관). LoadAsync에서 갱신 통지한다.</summary>
    public string GroupCountText => _loc.Get("WorkGroups_CountFormat", _all.Count);

    /// <summary>빈 상태 안내 표시 여부(Visibility 바인딩용 — x:Bind는 bool→Visibility 자동변환 없음).</summary>
    public Visibility EmptyVisibility => IsEmpty ? Visibility.Visible : Visibility.Collapsed;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>저장된 그룹을 다시 불러온다.</summary>
    public async Task LoadAsync()
    {
        var groups = await _groupService.GetAllAsync();
        _all.Clear();
        foreach (var g in groups)
        {
            var item = new GroupListItem(g);
            _all.Add(item);
            _ = item.LoadAsync();
        }

        ApplyFilter();
        OnPropertyChanged(nameof(GroupCountText));
    }

    /// <summary>그룹을 삭제하고 목록을 갱신한다(별도 안내 메시지는 표시하지 않는다).</summary>
    public async Task DeleteAsync(AppGroup group)
    {
        await _groupService.DeleteAsync(group.Id);
        await LoadAsync();
    }

    /// <summary>
    /// id로 그룹을 찾는다(검색 필터와 무관하게 원본 전체 기준 — 검색 중에도 외부 편집 요청이 대상을 찾도록).
    /// 아직 로드 전이면 1회 로드한 뒤 조회한다.
    /// </summary>
    public async Task<GroupListItem?> FindByIdAsync(string groupId)
    {
        if (_all.Count == 0)
            await LoadAsync();
        return _all.FirstOrDefault(g => g.Group.Id.Value == groupId);
    }

    // 검색어(그룹 이름/멤버 앱 이름 부분일치, 대소문자 무시)로 표시 목록을 재구성한다.
    private void ApplyFilter()
    {
        var query = SearchText?.Trim() ?? string.Empty;
        Groups.Clear();
        foreach (var item in _all)
        {
            if (query.Length == 0
                || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Group.Apps.Any(a => a.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                Groups.Add(item);
            }
        }

        IsEmpty = _all.Count == 0;
    }
}
