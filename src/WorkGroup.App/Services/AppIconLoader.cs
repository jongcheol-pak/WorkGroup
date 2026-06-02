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
    /// <summary>이미지 파일이면 직접, 그 외(.exe/.lnk)는 셸 썸네일로 로드. 실패 시 null(플레이스홀더).</summary>
    public static async Task<ImageSource?> LoadAsync(AppEntry app)
    {
        try
        {
            // 패키지(Store/UWP) 앱은 셸 공식 로고를 우선 사용한다(package.Logo 경로가 없어도 아이콘 확보 — plan.md T3).
            if (app.Kind == AppKind.Packaged)
            {
                using var logo = await PackagedAppIcon.OpenIconStreamAsync(app.LaunchTarget, 48);
                if (logo is not null)
                {
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(logo);
                    return bitmap;
                }
            }

            // 패키지 로고 등 이미지 파일은 직접 사용.
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
