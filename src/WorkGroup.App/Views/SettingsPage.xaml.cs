using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
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

    // 작업 그룹 목록 초기화 — 확인 후 ViewModel에 위임.
    private async void OnResetWorkGroupsClick(object sender, RoutedEventArgs e)
    {
        if (await ConfirmResetAsync("Settings_Reset_WorkGroups_ConfirmTitle", "Settings_Reset_WorkGroups_ConfirmMessage"))
            await ViewModel.ResetWorkGroupsAsync();
    }

    // 트레이 메뉴 목록 초기화 — 확인 후 ViewModel에 위임.
    private async void OnResetTrayMenuClick(object sender, RoutedEventArgs e)
    {
        if (await ConfirmResetAsync("Settings_Reset_TrayMenu_ConfirmTitle", "Settings_Reset_TrayMenu_ConfirmMessage"))
            await ViewModel.ResetTrayMenuAsync();
    }

    // 초기화 확인 다이얼로그(기본 버튼=취소로 실수 방지). XamlRoot 없으면 취소 처리.
    private async Task<bool> ConfirmResetAsync(string titleKey, string messageKey)
    {
        if (XamlRoot is null) return false;

        var loc = LocalizationService.Current;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = loc?.Get(titleKey),
            Content = loc?.Get(messageKey),
            PrimaryButtonText = loc?.Get("Common_Reset"),
            CloseButtonText = loc?.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
