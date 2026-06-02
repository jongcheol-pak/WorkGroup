using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;

namespace WorkGroup.Infrastructure.Icons;

/// <summary>
/// 패키지(Store/UWP) 앱의 아이콘을 셸이 렌더한 비트맵으로 추출한다(plan.md T5).
/// 시작 메뉴와 동일한 IShellItemImageFactory를 사용해 매니페스트 로고 여백 편차 없이 균일한 아이콘을 얻는다.
/// WinUI 타입(ImageSource)에 의존하지 않도록 WinRT 스트림(PNG)까지만 책임지고, 소비자가 자기 타입으로 변환한다.
/// </summary>
public static class PackagedAppIcon
{
    /// <summary>
    /// AUMID로 셸 아이콘을 추출해 PNG 스트림으로 연다. 실패하면 예외 없이 null(호출자 폴백).
    /// </summary>
    /// <param name="aumid">패키지 앱의 AUMID(형식: PackageFamilyName!AppId).</param>
    /// <param name="size">요청 아이콘 크기(셸이 가장 가까운 크기를 렌더).</param>
    public static async Task<IRandomAccessStream?> OpenIconStreamAsync(
        string aumid, uint size, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(aumid))
            return null;

        try
        {
            // 셸/GDI 호출은 동기이므로 UI 스레드를 막지 않도록 오프로드한다. PNG 인코딩은 비동기라 밖에서 처리.
            var bitmap = await Task.Run(() => ExtractShellIcon(aumid, size), cancellationToken).ConfigureAwait(false);
            if (bitmap is null)
                return null;

            using (bitmap)
                return await EncodePngAsync(bitmap, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // COM/GDI 실패·미발견·권한 등 모든 실패는 null로 흡수하고 호출자 폴백에 맡긴다.
            return null;
        }
    }

    /// <summary>shell:AppsFolder\AUMID 항목에서 셸 렌더 아이콘(HBITMAP)을 얻어 SoftwareBitmap으로 변환한다(동기).</summary>
    private static SoftwareBitmap? ExtractShellIcon(string aumid, uint size)
    {
        var path = "shell:AppsFolder\\" + aumid;
        var iid = typeof(IShellItemImageFactory).GUID;
        var hr = PInvoke.SHCreateItemFromParsingName(path, null, iid, out object ppv);
        if (hr.Failed || ppv is not IShellItemImageFactory factory)
            return null;

        try
        {
            var requested = new SIZE { cx = (int)size, cy = (int)size };
            // 요청보다 큰 자산 허용 + 아이콘만(썸네일/오버레이 배제)으로 시작 메뉴와 동일한 아이콘을 얻는다.
            // GetImage는 실패 시 예외를 던지며(out SafeHandle), 상위 try/catch가 흡수한다.
            factory.GetImage(requested, SIIGBF.SIIGBF_BIGGERSIZEOK | SIIGBF.SIIGBF_ICONONLY, out var hbmp);
            using (hbmp)
                return ConvertHBitmap(hbmp);
        }
        finally
        {
            if (Marshal.IsComObject(factory))
                Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>GDI HBITMAP(32bpp top-down DIBSection, premultiplied BGRA)을 SoftwareBitmap으로 복사한다.</summary>
    private static unsafe SoftwareBitmap? ConvertHBitmap(SafeHandle hbmp)
    {
        Span<byte> raw = stackalloc byte[sizeof(DIBSECTION)];
        if (PInvoke.GetObject(hbmp, raw) == 0)
            return null;

        var ds = MemoryMarshal.Read<DIBSECTION>(raw);
        int width = ds.dsBm.bmWidth;
        int height = ds.dsBm.bmHeight;
        if (width <= 0 || height <= 0 || ds.dsBm.bmBitsPixel != 32 || ds.dsBm.bmBits == null)
            return null;

        // IShellItemImageFactory는 top-down 32bpp DIB를 반환하므로 행 순서를 그대로 복사한다.
        int byteCount = ds.dsBm.bmWidthBytes * height;
        var buffer = new byte[byteCount];
        Marshal.Copy((nint)ds.dsBm.bmBits, buffer, 0, byteCount);

        return SoftwareBitmap.CreateCopyFromBuffer(
            buffer.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
    }

    /// <summary>SoftwareBitmap을 PNG로 인메모리 스트림에 인코딩해 반환한다(호출자가 dispose).</summary>
    private static async Task<IRandomAccessStream> EncodePngAsync(SoftwareBitmap bitmap, CancellationToken cancellationToken)
    {
        var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream).AsTask(cancellationToken).ConfigureAwait(false);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
        stream.Seek(0);
        return stream;
    }
}
