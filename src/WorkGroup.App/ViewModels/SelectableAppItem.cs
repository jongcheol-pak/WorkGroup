using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using WorkGroup.App.Services;
using WorkGroup.Domain.Groups;

namespace WorkGroup.App.ViewModels;

/// <summary>그룹 편집 다이얼로그의 설치 앱 항목(체크박스 선택 + 아이콘 비동기 로드 — plan.md T6).</summary>
public sealed partial class SelectableAppItem : ObservableObject
{
    public SelectableAppItem(AppEntry app) => App = app;

    public AppEntry App { get; }

    public string DisplayName => App.DisplayName;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial ImageSource? Icon { get; set; }

    public async Task LoadIconAsync() => Icon = await AppIconLoader.LoadAsync(App);
}
