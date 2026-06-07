using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using WorkGroup.Domain.Groups;
using WorkGroup.Infrastructure.Icons;

namespace WorkGroup.App.Services;

/// <summary>AppEntry의 아이콘을 WinUI ImageSource로 로드한다(plan.md T11 팝업 그리드용).</summary>
public static class AppIconLoader
{
    // 실행 대상(LaunchTarget) 단위 아이콘 결과를 프로세스 수명 동안 재사용한다(팝업 재표시·목록 pop-in 완화).
    // 모든 호출이 UI 스레드라 별도 락 없이 사용한다(동시 동일 키 중복 로드는 무해 — 마지막 값 유지).
    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>이미지 파일이면 직접, 그 외(.exe/.lnk)는 셸 썸네일로 로드. 실패 시 null(플레이스홀더). 성공 결과는 LaunchTarget 기준 캐시.</summary>
    public static async Task<ImageSource?> LoadAsync(AppEntry app)
    {
        if (Cache.TryGetValue(app.LaunchTarget, out var cached))
            return cached;

        try
        {
            // Win32·패키지 모두 셸 렌더 아이콘을 우선 사용한다(시작 메뉴와 동일, .lnk 아이콘 누락 해소 — plan.md T7).
            using (var shellIcon = await ShellIcon.OpenForAppAsync(app, 48))
            {
                if (shellIcon is not null)
                {
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(shellIcon);
                    Cache[app.LaunchTarget] = bitmap;
                    return bitmap;
                }
            }

            // 패키지 로고 등 이미지 파일은 직접 사용. 이 경로의 BitmapImage(Uri)는 디코드가 지연되어
            // 성공 여부를 확인할 수 없으므로 캐시에 저장하지 않는다(디코드 실패한 깨진 이미지의 영구 캐시 방지).
            // 직접 생성이라 비용이 낮아 매 호출 재생성해도 무방하다.
            if (!string.IsNullOrWhiteSpace(app.IconLocation) && IsImageFile(app.IconLocation) && File.Exists(app.IconLocation))
                return new BitmapImage(new Uri(app.IconLocation));

            // Win32 실행 파일/바로가기는 셸 썸네일로 추출.
            var path = app.Kind == AppKind.Win32 ? app.LaunchTarget : app.IconLocation;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 48);
                if (thumb is not null && thumb.Type == ThumbnailType.Image)
                {
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(thumb);
                    Cache[app.LaunchTarget] = bitmap;
                    return bitmap;
                }
            }
        }
        catch (Exception)
        {
            // 아이콘 로드 실패는 무시하고 플레이스홀더(null)로 처리.
        }

        return null;
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase);
    }
}
