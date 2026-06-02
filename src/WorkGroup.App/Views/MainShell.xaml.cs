using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WorkGroup.App.Views;

/// <summary>
/// 앱 셸(plan.md T1/DU1). NavigationView로 좌측 메뉴를 제공하고, 선택에 따라 내부 Frame을 전환한다.
/// </summary>
public sealed partial class MainShell : Page
{
    public MainShell()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 시작 시 "작업 그룹"을 기본 선택 → SelectionChanged가 발동해 첫 페이지를 navigate한다.
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;

        var pageType = item.Tag switch
        {
            "WorkGroups" => typeof(WorkGroupsPage),
            "TrayMenu" => typeof(TrayMenuPage),
            "Settings" => typeof(SettingsPage),
            "About" => typeof(AboutPage),
            _ => null
        };
        if (pageType is null) return;

        // 같은 페이지로의 재navigate는 건너뛴다.
        if (ContentFrame.Content?.GetType() == pageType) return;
        ContentFrame.Navigate(pageType);
    }
}
