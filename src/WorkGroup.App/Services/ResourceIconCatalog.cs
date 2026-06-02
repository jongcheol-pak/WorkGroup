using Windows.ApplicationModel;

namespace WorkGroup.App.Services;

/// <summary>
/// 번들된 리소스 그룹 아이콘(Assets/GroupIcons)을 열거한다(plan.md T3/DI3).
/// UI 객체(ImageSource)는 만들지 않고 ms-appx URI 문자열만 반환한다(스레드 무관). 결과는 1회 캐시.
/// </summary>
public sealed class ResourceIconCatalog
{
    private IReadOnlyList<string>? _cached;

    /// <summary>리소스 아이콘의 ms-appx URI 목록을 반환한다(열거 실패/비패키지 시 빈 목록).</summary>
    public async Task<IReadOnlyList<string>> GetIconUrisAsync()
    {
        if (_cached is not null)
            return _cached;

        try
        {
            var folder = await Package.Current.InstalledLocation.GetFolderAsync("Assets\\GroupIcons");
            var files = await folder.GetFilesAsync();
            _cached = files
                .Where(f => f.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Select(f => $"ms-appx:///Assets/GroupIcons/{f.Name}")
                .ToList();
        }
        catch
        {
            // 비패키지 실행이나 폴더 부재 시 빈 목록(사용자 아이콘·기본 아이콘은 동작).
            _cached = Array.Empty<string>();
        }

        return _cached;
    }
}
