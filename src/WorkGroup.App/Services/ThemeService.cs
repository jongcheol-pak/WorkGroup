using Windows.Storage;

namespace WorkGroup.App.Services;

/// <summary>
/// 앱 테마(시스템/다크/라이트)를 적용·영속한다(plan.md T2/DU5).
/// 영속은 ApplicationData.LocalSettings의 "AppTheme" 키, 런타임 전환은 루트 FrameworkElement.RequestedTheme로 한다.
/// (Application.RequestedTheme은 시작 시 1회만 가능하므로 루트 요소에 적용한다.)
/// </summary>
public sealed class ThemeService
{
    private const string Key = "AppTheme";

    // 메인 창의 루트 요소(설정 변경 시 이 루트의 테마를 바꿔 전체에 반영).
    private FrameworkElement? _root;

    /// <summary>앱 시작 시 메인 루트를 등록하고 저장된 테마를 적용한다.</summary>
    public void Initialize(FrameworkElement root)
    {
        _root = root;
        root.RequestedTheme = Read();
    }

    /// <summary>현재 저장된 테마를 반환한다(없거나 접근 실패면 시스템 기본).</summary>
    public ElementTheme Read()
    {
        try
        {
            return (ApplicationData.Current.LocalSettings.Values[Key] as string) switch
            {
                "Dark" => ElementTheme.Dark,
                "Light" => ElementTheme.Light,
                "System" => ElementTheme.Default,
                _ => ElementTheme.Default
            };
        }
        catch
        {
            // 비패키지 실행 등으로 LocalSettings 접근 실패 시 시스템 기본.
            return ElementTheme.Default;
        }
    }

    /// <summary>테마를 즉시 적용하고 저장한다(설정 페이지에서 호출).</summary>
    public void Set(ElementTheme theme)
    {
        // Initialize 전(루트 미등록)에는 적용을 건너뛰고 저장만 한다(다음 시작 시 반영).
        if (_root is not null)
            _root.RequestedTheme = theme;
        Save(theme);
    }

    private static void Save(ElementTheme theme)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[Key] = theme switch
            {
                ElementTheme.Dark => "Dark",
                ElementTheme.Light => "Light",
                _ => "System"
            };
        }
        catch
        {
            // 접근 실패는 무시한다(테마는 메모리상으로는 적용됨).
        }
    }
}
