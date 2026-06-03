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
/// 그룹 추가/수정 다이얼로그 ViewModel(plan.md T4). 상단 아이콘+이름(15자), 선택 앱 목록(추가/삭제),
/// 확인 시 빈 목록·이름 중복을 검증하고 GroupAppService로 저장한다. 아이콘은 사용자 이미지/리소스 아이콘.
/// </summary>
public sealed partial class GroupEditViewModel : ObservableObject
{
    // 미선택 새 그룹의 기본 아이콘(번들 리소스, plan.md DI2).
    private const string DefaultIconUri = "ms-appx:///Assets/GroupIcons/appgroup.png";

    private readonly IAppInventory _inventory;
    private readonly IGroupAppService _groupService;
    private readonly ResourceIconCatalog _resourceIcons;

    // 설치 앱 항목(아이콘 1회 로드 후 선택/후보 목록에서 재사용).
    private readonly List<PopupAppItem> _installedItems = new();
    // 중복 검사용 기존 그룹명 스냅샷(편집 시 자기 제외). 확인 시 재조회하지 않는다(plan.md DI10/M2).
    private HashSet<string> _existingNames = new(StringComparer.OrdinalIgnoreCase);
    private GroupId? _editingId;
    // 편집 시작 시 원래 이름(이름 변경 감지용). 신규는 빈 문자열.
    private string _originalName = string.Empty;

    // 리소스 아이콘/설치앱 picker는 각 Flyout이 처음 열릴 때 지연 로드한다(오픈/취소 성능 — plan.md Debug 섹션 참조).
    private bool _resourceLoaded;
    private bool _pickerLoaded;

    public GroupEditViewModel(IAppInventory inventory, IGroupAppService groupService, ResourceIconCatalog resourceIcons)
    {
        _inventory = inventory;
        _groupService = groupService;
        _resourceIcons = resourceIcons;

        Title = "그룹 추가";
        EditingName = string.Empty;
        PickerSearch = string.Empty;
        StatusMessage = string.Empty;
        ShowPopupHeader = true;
        SelectedIcon = IconSource.FromCustomImage(DefaultIconUri);

        // 선택 앱 개수 표시(AppCountText)는 목록 변경(추가/삭제/Clear)마다 갱신한다. 구독은 생성자에서 단 1회.
        SelectedApps.CollectionChanged += (_, _) => OnPropertyChanged(nameof(AppCountText));
    }

    /// <summary>그룹에 포함된(선택된) 앱 목록.</summary>
    public ObservableCollection<PopupAppItem> SelectedApps { get; } = new();

    /// <summary>"앱 추가" 팝업에 표시할 후보 설치 앱(이미 선택된 앱 제외, 검색 필터).</summary>
    public ObservableCollection<PopupAppItem> AvailableApps { get; } = new();

    /// <summary>리소스 아이콘 그리드 항목.</summary>
    public ObservableCollection<ResourceIconItem> ResourceIcons { get; } = new();

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRenameWarning))]
    public partial string EditingName { get; set; }

    /// <summary>편집 모드 여부(신규=false). 이름 읽기전용 표시·핀 재등록 경고는 편집 모드에만 적용.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRenameWarning))]
    public partial bool IsEditMode { get; set; }

    /// <summary>이름을 입력창(편집)으로 전환했는지. 신규는 처음부터 true, 편집은 이름 클릭 시 true.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameDisplayVisibility))]
    [NotifyPropertyChangedFor(nameof(NameEditVisibility))]
    public partial bool IsNameEditing { get; set; }

    /// <summary>편집 모드에서 이름이 원래 이름과 달라졌을 때만 핀 재등록 경고를 표시한다.</summary>
    public bool ShowRenameWarning
        => IsEditMode && !string.Equals(EditingName.Trim(), _originalName, StringComparison.Ordinal);

    /// <summary>이름 읽기전용 표시 영역의 표시 여부(편집 전환 시 숨김).</summary>
    public Visibility NameDisplayVisibility => IsNameEditing ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>이름 입력창의 표시 여부(편집 전환 시 표시).</summary>
    public Visibility NameEditVisibility => IsNameEditing ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    public partial string PickerSearch { get; set; }

    /// <summary>"앱 추가" 팝업이 설치 앱을 로드 중인지(ProgressRing).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PickerListVisibility))]
    public partial bool IsPickerLoading { get; set; }

    /// <summary>로딩 중에는 후보 목록을 숨겨 ProgressRing과 겹치지 않게 한다.</summary>
    public Visibility PickerListVisibility => IsPickerLoading ? Visibility.Collapsed : Visibility.Visible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusMessage { get; set; }

    /// <summary>현재 선택된 아이콘 소스(저장 대상).</summary>
    public IconSource SelectedIcon { get; private set; }

    /// <summary>아이콘 미리보기 이미지.</summary>
    [ObservableProperty]
    public partial ImageSource? PreviewImage { get; set; }

    /// <summary>아이콘 Flyout에서 리소스 그리드를 펼쳤는지(사용자/리소스 선택 후 그리드 표시).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResourceGridVisibility))]
    public partial bool ShowResourceGrid { get; set; }

    /// <summary>리소스 그리드 표시 여부(Visibility 바인딩 — Flyout 내 x:Name code-behind 접근 회피).</summary>
    public Visibility ResourceGridVisibility => ShowResourceGrid ? Visibility.Visible : Visibility.Collapsed;

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>핀 팝업에 그룹 이름 헤더를 표시할지(토글). 저장 시 AppGroup에 반영된다.</summary>
    [ObservableProperty]
    public partial bool ShowPopupHeader { get; set; }

    /// <summary>선택한 앱 개수 표시 텍스트(목록 위 카운트).</summary>
    public string AppCountText => $"앱 {SelectedApps.Count}개";

    /// <summary>
    /// 신규/편집 모드로 초기화. 무거운 설치앱 인벤토리·리소스 그리드는 여기서 로드하지 않고
    /// 각 Flyout이 열릴 때 지연 로드한다(편집 멤버는 즉시 복원 — plan.md Debug 섹션 참조).
    /// </summary>
    public async Task InitializeAsync(AppGroup? group)
    {
        ShowResourceGrid = false;
        _resourceLoaded = false;
        _pickerLoaded = false;
        try
        {
            _editingId = group?.Id;
            // 이름 변경 감지·읽기전용 전환 상태는 EditingName 설정 전에 먼저 정한다
            // (EditingName 설정이 유발하는 ShowRenameWarning 통지 시점에 올바른 값이 보이도록).
            IsEditMode = group is not null;
            _originalName = group?.Name ?? string.Empty;
            IsNameEditing = group is null; // 신규는 즉시 입력, 수정은 읽기전용부터
            Title = group is null ? "그룹 추가" : "그룹 수정";
            EditingName = group?.Name ?? string.Empty;
            ShowPopupHeader = group?.ShowPopupHeader ?? true; // 편집은 그룹 값, 신규는 표시(true)
            OnPropertyChanged(nameof(ShowRenameWarning)); // 초기 상태 보정

            // 중복 검사용 기존 그룹명 스냅샷(편집 시 자기 제외).
            var groups = await _groupService.GetAllAsync();
            _existingNames = groups
                .Where(g => _editingId is null || g.Id != _editingId)
                .Select(g => g.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 아이콘 초기값 + 미리보기.
            SelectedIcon = group?.Icon ?? IconSource.FromCustomImage(DefaultIconUri);
            ResolvePreview(group);

            // 편집 멤버는 느린 설치앱 인벤토리를 기다리지 않고 그룹 데이터에서 즉시 복원한다.
            SelectedApps.Clear();
            _installedItems.Clear();
            if (group is not null)
                foreach (var app in group.Apps)
                {
                    var item = new PopupAppItem(app);
                    SelectedApps.Add(item);
                    _ = item.LoadIconAsync();
                }
        }
        catch (Exception ex)
        {
            StatusMessage = $"불러오기 실패: {ex.Message}";
        }
    }

    /// <summary>아이콘 Flyout이 처음 열릴 때 리소스 아이콘 그리드를 지연 로드한다(UI 스레드).</summary>
    public async Task EnsureResourceIconsAsync()
    {
        if (_resourceLoaded) return;
        try
        {
            var uris = await _resourceIcons.GetIconUrisAsync();
            ResourceIcons.Clear();
            foreach (var uri in uris)
                ResourceIcons.Add(new ResourceIconItem(uri, new BitmapImage(new Uri(uri))));
            _resourceLoaded = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"리소스 아이콘을 불러오지 못했습니다: {ex.Message}";
        }
    }

    /// <summary>"앱 추가" Flyout이 처음 열릴 때 설치 앱을 지연 로드한다(아이콘 1회 로드).</summary>
    public async Task EnsurePickerLoadedAsync()
    {
        if (_pickerLoaded) return;
        IsPickerLoading = true;
        try
        {
            _installedItems.Clear();
            foreach (var app in await _inventory.GetInstalledAppsAsync())
            {
                var item = new PopupAppItem(app);
                _installedItems.Add(item);
                _ = item.LoadIconAsync();
            }
            RefreshAvailable();
            _pickerLoaded = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"앱 목록을 불러오지 못했습니다: {ex.Message}";
        }
        finally
        {
            IsPickerLoading = false;
        }
    }

    /// <summary>사용자가 고른 이미지 파일을 아이콘으로 설정한다.</summary>
    public void SetUserImage(string path)
    {
        SelectedIcon = IconSource.FromCustomImage(path);
        SetPreviewFromUri(path);
    }

    /// <summary>리소스 아이콘(ms-appx URI)을 아이콘으로 설정한다.</summary>
    public void SetResourceIcon(string uri)
    {
        SelectedIcon = IconSource.FromCustomImage(uri);
        SetPreviewFromUri(uri);
    }

    /// <summary>앱을 선택 목록에 추가한다(중복 무시). 설치 목록의 항목이면 재사용, 아니면 새로 만든다.</summary>
    public void AddApp(AppEntry app)
    {
        if (SelectedApps.Any(i => i.App.SameTarget(app.LaunchTarget)))
            return;

        var item = _installedItems.FirstOrDefault(i => i.App.SameTarget(app.LaunchTarget));
        if (item is null)
        {
            // 설치 목록에 없는 멤버(제거된 앱 등)도 표시할 수 있도록 새로 만든다.
            item = new PopupAppItem(app);
            _ = item.LoadIconAsync();
        }
        SelectedApps.Add(item);
        RefreshAvailable();
    }

    /// <summary>앱을 선택 목록에서 제거한다.</summary>
    public void RemoveApp(PopupAppItem item)
    {
        SelectedApps.Remove(item);
        RefreshAvailable();
    }

    /// <summary>"앱 추가" 팝업 항목 클릭 토글: 이미 선택된 앱이면 제거, 아니면 추가(LaunchTarget 기준).</summary>
    public void ToggleApp(AppEntry app)
    {
        var existing = SelectedApps.FirstOrDefault(i => i.App.SameTarget(app.LaunchTarget));
        if (existing is not null)
            RemoveApp(existing);
        else
            AddApp(app);
    }

    /// <summary>확인 클릭 시 호출. 빈 목록·이름 중복 검증 통과·저장 성공 시 true(닫힘), 실패 시 false(유지 + 메시지).</summary>
    public async Task<bool> ValidateAndSaveAsync()
    {
        var name = EditingName.Trim();
        if (SelectedApps.Count == 0)
        {
            StatusMessage = "앱을 1개 이상 추가하세요.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "그룹 이름을 입력하세요.";
            return false;
        }
        if (_existingNames.Contains(name))
        {
            StatusMessage = "이미 같은 이름의 그룹이 있습니다.";
            return false;
        }

        var apps = SelectedApps.Select(i => i.App).ToList();
        AppGroup group;
        if (_editingId is null)
        {
            var created = AppGroup.Create(name, SelectedIcon, ShowPopupHeader);
            if (created.IsFailure)
            {
                StatusMessage = created.Error ?? "그룹 생성 실패";
                return false;
            }
            group = created.Value;
            foreach (var app in apps)
                group.AddApp(app);
        }
        else
        {
            group = AppGroup.Restore(_editingId, name, SelectedIcon, apps, ShowPopupHeader);
        }

        var result = await _groupService.SaveAsync(group);
        if (result.IsFailure)
        {
            StatusMessage = result.Error ?? "저장 실패";
            return false;
        }
        return true;
    }

    partial void OnPickerSearchChanged(string value) => RefreshAvailable();

    private void RefreshAvailable()
    {
        AvailableApps.Clear();
        // 추가된 항목도 목록에 남겨 체크로 표시한다(제외하지 않음). 선택 여부는 LaunchTarget 기준.
        var selectedTargets = SelectedApps.Select(i => i.App.LaunchTarget).ToHashSet(StringComparer.OrdinalIgnoreCase);
        IEnumerable<PopupAppItem> query = _installedItems;
        if (!string.IsNullOrWhiteSpace(PickerSearch))
            query = query.Where(i => i.DisplayName.Contains(PickerSearch, StringComparison.OrdinalIgnoreCase));
        foreach (var item in query)
        {
            item.IsSelected = selectedTargets.Contains(item.App.LaunchTarget);
            AvailableApps.Add(item);
        }
    }

    private void ResolvePreview(AppGroup? group)
    {
        // CustomImage(사용자/리소스)는 그 이미지를, 그 외 legacy 아이콘은 생성된 .ico를 미리보기로 쓴다(plan.md DI5).
        if (SelectedIcon.Kind == IconSourceKind.CustomImage)
        {
            SetPreviewFromUri(SelectedIcon.Value);
            return;
        }

        PreviewImage = null;
        if (group is null) return;
        try
        {
            var ico = GroupIconLoader.GetIconPath(group.Id);
            if (File.Exists(ico))
                PreviewImage = new BitmapImage(new Uri(ico, UriKind.Absolute));
        }
        catch
        {
            // .ico 로드 실패 → 미리보기 없음(표시만 영향).
            PreviewImage = null;
        }
    }

    private void SetPreviewFromUri(string pathOrUri)
    {
        try
        {
            PreviewImage = new BitmapImage(new Uri(pathOrUri, UriKind.Absolute));
        }
        catch
        {
            // 잘못된 경로/URI → 미리보기 없음(표시만 영향).
            PreviewImage = null;
        }
    }
}
