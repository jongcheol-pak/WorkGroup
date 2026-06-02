using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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

    public WorkGroupsViewModel(IGroupAppService groupService)
    {
        _groupService = groupService;
        StatusMessage = string.Empty;
    }

    public ObservableCollection<GroupListItem> Groups { get; } = new();

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

    /// <summary>등록된 그룹 수 표시(예: "3 그룹"). 목록 변경 후 LoadAsync에서 갱신 통지한다.</summary>
    public string GroupCountText => $"{Groups.Count} 그룹";

    /// <summary>빈 상태 안내 표시 여부(Visibility 바인딩용 — x:Bind는 bool→Visibility 자동변환 없음).</summary>
    public Visibility EmptyVisibility => IsEmpty ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>저장된 그룹을 다시 불러온다.</summary>
    public async Task LoadAsync()
    {
        var groups = await _groupService.GetAllAsync();
        Groups.Clear();
        foreach (var g in groups)
        {
            var item = new GroupListItem(g);
            Groups.Add(item);
            _ = item.LoadAsync();
        }
        IsEmpty = Groups.Count == 0;
        OnPropertyChanged(nameof(GroupCountText));
    }

    /// <summary>그룹을 삭제하고 목록을 갱신한다(별도 안내 메시지는 표시하지 않는다).</summary>
    public async Task DeleteAsync(AppGroup group)
    {
        await _groupService.DeleteAsync(group.Id);
        await LoadAsync();
    }
}
