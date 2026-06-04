using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WorkGroup.App.Services;
using WorkGroup.Application.Folders;

namespace WorkGroup.App.ViewModels;

/// <summary>
/// 트레이 메뉴(폴더 바로가기 관리) 페이지 ViewModel. 등록 폴더 목록 + 검색 필터를 제공한다.
/// 추가/편집 다이얼로그·삭제 확인·폴더 선택은 XamlRoot/HWND가 필요해 페이지 코드비하인드가 담당한다.
/// </summary>
public sealed partial class FolderShortcutsViewModel : ObservableObject
{
    private readonly IFolderShortcutRepository _repository;
    private readonly LocalizationService _loc;

    // 전체 폴더(검색 필터의 원본). 아이콘은 로드 시 1회만 받아 둔다.
    private readonly List<FolderShortcutItem> _all = new();

    public FolderShortcutsViewModel(IFolderShortcutRepository repository, LocalizationService loc)
    {
        _repository = repository;
        _loc = loc;
        SearchText = string.Empty;
    }

    /// <summary>검색 필터가 적용된 표시용 목록.</summary>
    public ObservableCollection<FolderShortcutItem> Folders { get; } = new();

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyVisibility))]
    public partial bool IsEmpty { get; set; }

    /// <summary>등록된 폴더 수 표시(예: "3개 폴더"). 전체 개수 기준(검색과 무관).</summary>
    public string FolderCountText => _loc.Get("TrayMenu_FolderCountFormat", _all.Count);

    /// <summary>빈 상태 안내 표시 여부(전체 0개일 때).</summary>
    public Visibility EmptyVisibility => IsEmpty ? Visibility.Visible : Visibility.Collapsed;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>저장된 폴더를 다시 불러온다.</summary>
    public async Task LoadAsync()
    {
        var shortcuts = await _repository.LoadAllAsync();
        _all.Clear();
        foreach (var s in shortcuts)
        {
            var item = new FolderShortcutItem(s);
            _all.Add(item);
            _ = item.LoadIconAsync();
        }

        ApplyFilter();
        OnPropertyChanged(nameof(FolderCountText));
    }

    /// <summary>폴더를 삭제하고 목록을 갱신한다.</summary>
    public async Task DeleteAsync(int id)
    {
        await _repository.DeleteAsync(id);
        await LoadAsync();
    }

    // 검색어(이름/경로 부분일치, 대소문자 무시)로 표시 목록을 재구성한다.
    private void ApplyFilter()
    {
        var query = SearchText?.Trim() ?? string.Empty;
        Folders.Clear();
        foreach (var item in _all)
        {
            if (query.Length == 0
                || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                Folders.Add(item);
            }
        }

        IsEmpty = _all.Count == 0;
    }
}
