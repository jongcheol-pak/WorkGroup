using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WorkGroup.App.Services;
using WorkGroup.Domain.Groups;

namespace WorkGroup.App.ViewModels;

/// <summary>팝업 그리드의 앱 항목(아이콘 비동기 로드).</summary>
public sealed partial class PopupAppItem : ObservableObject
{
    public PopupAppItem(AppEntry app) => App = app;

    public AppEntry App { get; }

    public string DisplayName => App.DisplayName;

    /// <summary>관리자 권한 실행 가능 여부(Packaged 앱은 불가 → 메뉴 항목 비활성용).</summary>
    public bool CanRunAsAdmin => App.Kind == AppKind.Win32;

    [ObservableProperty]
    public partial ImageSource? Icon { get; set; }

    /// <summary>"앱 추가" 팝업에서 이미 선택된 항목인지(체크 표시용).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionGlyphVisibility))]
    public partial bool IsSelected { get; set; }

    /// <summary>선택 시에만 체크 아이콘을 보이게 한다.</summary>
    public Visibility SelectionGlyphVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;

    // 아이콘 지연 로드 1회 보장 플래그(UI 스레드 단일 호출 전제 — 컨테이너 실현/추가 모두 UI 스레드).
    private bool _iconRequested;

    public async Task LoadIconAsync() => Icon = await AppIconLoader.LoadAsync(App);

    /// <summary>아이콘을 1회만 지연 로드한다(중복 호출 무시). 재사용/신규 경로의 중복 로드를 모두 흡수한다.</summary>
    public void EnsureIconLoad()
    {
        if (_iconRequested) return;
        _iconRequested = true;
        _ = LoadIconAsync();
    }
}
