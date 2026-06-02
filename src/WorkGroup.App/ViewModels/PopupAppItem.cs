using CommunityToolkit.Mvvm.ComponentModel;
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

    [ObservableProperty]
    public partial ImageSource? Icon { get; set; }

    public async Task LoadIconAsync() => Icon = await AppIconLoader.LoadAsync(App);
}
