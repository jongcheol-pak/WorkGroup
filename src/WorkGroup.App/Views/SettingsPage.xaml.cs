using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using WorkGroup.App.Services;
using WorkGroup.App.ViewModels;

namespace WorkGroup.App.Views;

/// <summary>설정 페이지(plan.md T3). 자동 시작 토글 + 앱 테마 + 표시 언어 전환.</summary>
public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        // 언어 변경 시 재시작 확인 다이얼로그를 ViewModel에 위임(XamlRoot가 필요해 View에서 띄운다).
        ViewModel.ConfirmRestartAsync = ShowRestartConfirmAsync;
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    // 언어 변경 재시작 확인. 확인 시 true(ViewModel이 저장·재시작 수행).
    private async Task<bool> ShowRestartConfirmAsync()
    {
        // 페이지 언로드 등으로 XamlRoot가 없으면 다이얼로그를 띄울 수 없으므로 변경을 취소 처리.
        if (XamlRoot is null) return false;

        var loc = LocalizationService.Current;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = loc?.Get("Settings_Language_RestartTitle"),
            Content = loc?.Get("Settings_Language_RestartMessage"),
            PrimaryButtonText = loc?.Get("Settings_Language_RestartConfirm"),
            CloseButtonText = loc?.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
