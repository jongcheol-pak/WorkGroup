using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WorkGroup.Infrastructure.Icons;

namespace WorkGroup.App.Services;

/// <summary>
/// 폴더/파일 경로의 셸 아이콘을 WinUI ImageSource로 로드한다(폴더 바로가기 목록/팝업용).
/// AppIconLoader와 동일하게 실패 시 null(호출자 플레이스홀더).
/// </summary>
public static class FolderIconLoader
{
    public static async Task<ImageSource?> LoadAsync(string path, uint size = 48)
    {
        try
        {
            using var stream = await ShellIcon.OpenForPathAsync(path, size);
            if (stream is not null)
            {
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                return bitmap;
            }
        }
        catch (Exception)
        {
            // 아이콘 로드 실패는 무시하고 플레이스홀더(null)로 처리.
        }

        return null;
    }
}
