using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;
using WorkGroup.Domain.Groups;

namespace WorkGroup.Infrastructure.Icons;

/// <summary>
/// 설치 앱(Win32·패키지)의 아이콘을 셸이 렌더한 비트맵으로 추출한다(plan.md T5/T7).
/// 탐색기/시작 메뉴와 동일한 IShellItemImageFactory를 사용해, 콘솔/스크립트 .lnk나 로고 여백 편차 없이 균일한 아이콘을 얻는다.
/// WinUI 타입(ImageSource)에 의존하지 않도록 WinRT 스트림(PNG)까지만 책임지고, 소비자가 자기 타입으로 변환한다.
/// </summary>
public static class ShellIcon
{
    // 요청 크기 → 시도 크기 캐스케이드(DevDashboard 동일). 큰 자산부터 시도해 선명한 아이콘을 우선.
    private static int[] SizeCascade(uint size) =>
        size <= 32 ? [32] : size <= 48 ? [48, 32] : [256, 128, 64, 48, 32];

    // 아이콘만(썸네일/오버레이 배제) 우선, 실패 시 일반 플래그로 폴백.
    private static readonly SIIGBF[] IconFlags =
        [SIIGBF.SIIGBF_BIGGERSIZEOK | SIIGBF.SIIGBF_ICONONLY, SIIGBF.SIIGBF_BIGGERSIZEOK];

    /// <summary>
    /// 앱 종류에 맞는 셸 파싱 경로(패키지=shell:AppsFolder\AUMID, Win32=파일 경로)로 아이콘 PNG 스트림을 연다.
    /// 실패하면 예외 없이 null(호출자 폴백).
    /// </summary>
    public static Task<IRandomAccessStream?> OpenForAppAsync(AppEntry app, uint size, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        var parsingName = app.Kind == AppKind.Packaged
            ? "shell:AppsFolder\\" + app.LaunchTarget
            : app.LaunchTarget;
        return OpenStreamAsync(parsingName, size, cancellationToken);
    }

    /// <summary>
    /// 파일/폴더 경로의 셸 항목에서 아이콘 PNG 스트림을 연다(파일=파일 아이콘, 폴더=셸 폴더 아이콘).
    /// 실패하면 예외 없이 null(호출자 폴백). 폴더 바로가기 기능에서 사용한다.
    /// </summary>
    public static Task<IRandomAccessStream?> OpenForPathAsync(string parsingName, uint size, CancellationToken cancellationToken = default)
        => OpenStreamAsync(parsingName, size, cancellationToken);

    private static async Task<IRandomAccessStream?> OpenStreamAsync(string parsingName, uint size, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parsingName))
            return null;

        try
        {
            // 셸/GDI 호출은 동기이므로 UI 스레드를 막지 않도록 오프로드한다. PNG 인코딩은 비동기라 밖에서 처리.
            var bitmap = await Task.Run(() => ExtractShellIcon(parsingName, size), cancellationToken).ConfigureAwait(false);
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

    /// <summary>파싱 경로의 셸 항목에서 렌더 아이콘(HBITMAP)을 얻어 SoftwareBitmap으로 변환한다(동기).</summary>
    private static SoftwareBitmap? ExtractShellIcon(string parsingName, uint size)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        var hr = PInvoke.SHCreateItemFromParsingName(parsingName, null, iid, out object ppv);
        if (hr.Failed || ppv is not IShellItemImageFactory factory)
            return null;

        try
        {
            foreach (var trySize in SizeCascade(size))
            {
                foreach (var flags in IconFlags)
                {
                    try
                    {
                        // GetImage는 실패 시 예외를 던진다(out SafeHandle). 다음 크기/플래그로 폴백.
                        factory.GetImage(new SIZE { cx = trySize, cy = trySize }, flags, out var hbmp);
                        using (hbmp)
                        {
                            var bitmap = ConvertHBitmap(hbmp);
                            if (bitmap is not null)
                                return bitmap;
                        }
                    }
                    catch
                    {
                        // 이 크기/플래그 조합 실패 → 다음 조합 시도.
                    }
                }
            }

            return null;
        }
        finally
        {
            if (Marshal.IsComObject(factory))
                Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>GDI HBITMAP(32bpp DIBSection, premultiplied BGRA)을 top-down SoftwareBitmap으로 복사한다.</summary>
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

        // 32bpp 스트라이드는 width*4(이미 4바이트 정렬). SoftwareBitmap은 top-down 타이트 패킹을 가정한다.
        int stride = ds.dsBm.bmWidthBytes;
        var buffer = new byte[stride * height];
        var src = (nint)ds.dsBm.bmBits;

        // biHeight 양수 = bottom-up DIB(셸은 보통 top-down이나 방어적으로 처리) → 행을 역순 복사해 top-down으로 정규화.
        bool bottomUp = ds.dsBmih.biHeight > 0;
        for (int row = 0; row < height; row++)
        {
            int srcRow = bottomUp ? (height - 1 - row) : row;
            Marshal.Copy(src + (srcRow * stride), buffer, row * stride, stride);
        }

        return SoftwareBitmap.CreateCopyFromBuffer(
            buffer.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
    }

    /// <summary>SoftwareBitmap을 PNG로 인메모리 스트림에 인코딩해 반환한다(호출자가 dispose).</summary>
    private static async Task<IRandomAccessStream> EncodePngAsync(SoftwareBitmap bitmap, CancellationToken cancellationToken)
    {
        var stream = new InMemoryRandomAccessStream();
        try
        {
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream).AsTask(cancellationToken).ConfigureAwait(false);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
            stream.Seek(0);
            return stream;
        }
        catch
        {
            // 인코딩 실패 시 스트림을 즉시 해제하고 상위에서 null로 흡수하게 한다.
            stream.Dispose();
            throw;
        }
    }
}
