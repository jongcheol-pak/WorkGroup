using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.AppLifecycle;
using WorkGroup.App.Activation;
using WorkGroup.App.Views;

namespace WorkGroup.App
{
    /// <summary>
    /// 애플리케이션 진입점. WorkGroup.Application 네임스페이스와의 이름 충돌을 피하기 위해
    /// 베이스 타입을 정규화하여 지정한다.
    /// </summary>
    public partial class App : Microsoft.UI.Xaml.Application
    {
        private Window? _window;

        public App()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// 활성화 인자에 그룹 id가 있으면(핀된 .lnk 클릭 / 프로토콜) 팝업을, 없으면 메인 창을 연다(plan.md T2).
        /// </summary>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            var groupId = ActivationParser.TryGetGroupId(activation);

            if (!string.IsNullOrWhiteSpace(groupId))
            {
                _window = new SpikePopupWindow(groupId);
                _window.Activate();
                return;
            }

            LaunchMainWindow(e);
        }

        private void LaunchMainWindow(LaunchActivatedEventArgs e)
        {
            _window ??= new Window();

            if (_window.Content is not Frame rootFrame)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                _window.Content = rootFrame;
            }

            _ = rootFrame.Navigate(typeof(MainPage), e.Arguments);
            _window.Activate();
        }

        private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }
    }
}
