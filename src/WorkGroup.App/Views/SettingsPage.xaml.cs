using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using WorkGroup.App.ViewModels;

namespace WorkGroup.App.Views;

/// <summary>설정 페이지(plan.md T3). 자동 시작 토글 + 앱 테마 전환.</summary>
public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }
}
