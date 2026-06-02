using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
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
}
