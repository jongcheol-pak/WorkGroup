using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WorkGroup.App.Services;
using WorkGroup.App.ViewModels;

namespace WorkGroup.App.Views;

/// <summary>정보 페이지(plan.md T4). 앱 이름·버전 + 오픈소스 라이선스 목록.</summary>
public sealed partial class AboutPage : Page
{
    public AboutViewModel ViewModel { get; }

    public AboutPage()
    {
        InitializeComponent();
        ViewModel = App.Services.GetRequiredService<AboutViewModel>();
    }

    // 라이선스 카드 클릭 시 해당 프로젝트 링크를 기본 브라우저로 연다.
    private async void OnLicenseClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LicenseInfo info)
            await Windows.System.Launcher.LaunchUriAsync(info.Link);
    }
}
