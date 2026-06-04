using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using WorkGroup.Application.Icons;
using WorkGroup.Application.Localization;
using WorkGroup.Domain.Common;
using WorkGroup.Domain.Groups;

namespace WorkGroup.Infrastructure.Icons;

/// <summary>
/// WIC(Windows.Graphics.Imaging)로 이미지를 디코드·리사이즈·PNG 인코딩하고 IcoWriter로 .ico를 만든다(plan.md D16).
/// 어떤 단계든 실패하면 기본 내장 아이콘으로 대체한다(plan.md T5 Edge Cases).
/// </summary>
public sealed class IconService : IIconService
{
    // .ico 표준 해상도 후보. 실제로는 원본 크기 이하만 생성한다(업스케일 금지 → 흐림 방지).
    private static readonly int[] StandardSizes = { 16, 24, 32, 48, 64, 128, 256 };
    private const int CanvasSize = 256;

    private readonly ILogger<IconService> _logger;
    private readonly ILocalizer _localizer;

    public IconService(ILogger<IconService>? logger = null, ILocalizer? localizer = null)
    {
        _logger = logger ?? NullLogger<IconService>.Instance;
        _localizer = localizer ?? NullLocalizer.Instance;
    }

    public async Task<Result<string>> CreateGroupIconAsync(
        GroupId groupId,
        IconSource source,
        IReadOnlyList<AppEntry> members,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return Result<string>.Fail(_localizer.Get("Infra_Icon_NoOutputDir"));

        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, groupId.Value + ".ico");

        try
        {
            var bitmap = await ResolveBitmapAsync(source, members, cancellationToken).ConfigureAwait(false);
            await WriteIcoAsync(bitmap, outputPath, cancellationToken).ConfigureAwait(false);
            // 목록 표시용 PNG(원본 해상도)도 함께 저장한다 — .ico 프레임 디코드 없이 선명하게 표시하기 위함.
            await WritePngFileAsync(bitmap, Path.ChangeExtension(outputPath, ".png"), cancellationToken).ConfigureAwait(false);
            return Result<string>.Ok(outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "그룹 아이콘 생성 실패, 기본 아이콘으로 대체: {Group}", groupId.Value);
            return await CreateDefaultAsync(outputPath, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<Result<string>> CreateDefaultAsync(string outputPath, CancellationToken cancellationToken)
    {
        try
        {
            using var bitmap = CreateSolidBitmap(DefaultColor);
            await WriteIcoAsync(bitmap, outputPath, cancellationToken).ConfigureAwait(false);
            await WritePngFileAsync(bitmap, Path.ChangeExtension(outputPath, ".png"), cancellationToken).ConfigureAwait(false);
            return Result<string>.Ok(outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "기본 아이콘 생성에 실패했습니다: {Path}", outputPath);
            return Result<string>.Fail(_localizer.Get("Infra_Icon_CreateFailed"));
        }
    }

    // ----- 소스별 비트맵 해석 -----

    private async Task<SoftwareBitmap> ResolveBitmapAsync(
        IconSource source, IReadOnlyList<AppEntry> members, CancellationToken cancellationToken)
    {
        switch (source.Kind)
        {
            case IconSourceKind.CustomImage:
                return await DecodeImageFileAsync(source.Value, cancellationToken).ConfigureAwait(false);

            case IconSourceKind.MemberApp:
                var member = members.FirstOrDefault(m => m.SameTarget(source.Value)) ?? members.FirstOrDefault();
                if (member is null)
                    return CreateSolidBitmap(DefaultColor);
                return await ResolveMemberBitmapAsync(member, cancellationToken).ConfigureAwait(false);

            case IconSourceKind.BuiltIn:
            default:
                return CreateSolidBitmap(ColorForBuiltIn(source.Value));
        }
    }

    private async Task<SoftwareBitmap> ResolveMemberBitmapAsync(AppEntry member, CancellationToken cancellationToken)
    {
        // Win32·패키지 모두 셸 렌더 아이콘을 우선 사용한다(시작 메뉴와 동일, 누락/여백 편차 해소 — plan.md T7).
        var shellIcon = await ShellIcon
            .OpenForAppAsync(member, (uint)CanvasSize, cancellationToken).ConfigureAwait(false);
        if (shellIcon is not null)
        {
            using (shellIcon)
                return await DecodeStreamAsync(shellIcon, cancellationToken).ConfigureAwait(false);
        }

        var location = member.IconLocation;
        if (!string.IsNullOrWhiteSpace(location) && File.Exists(location))
        {
            // 이미지 파일(패키지 로고 PNG 등)이면 직접 디코드, 그 외(.exe/.dll 등)는 셸 썸네일로 추출.
            if (IsImageFile(location))
                return await DecodeImageFileAsync(location, cancellationToken).ConfigureAwait(false);

            return await DecodeThumbnailAsync(location, cancellationToken).ConfigureAwait(false);
        }

        return CreateSolidBitmap(DefaultColor);
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

    private static async Task<SoftwareBitmap> DecodeImageFileAsync(string path, CancellationToken cancellationToken)
    {
        // 번들 리소스 아이콘은 실파일 경로가 아니라 ms-appx 패키지 URI로 열어야 한다(GetFileFromPathAsync 불가).
        // MemberApp 분기는 File.Exists/IsImageFile 가드를 거친 실파일만 전달하므로 이 분기에 진입하지 않는다.
        // 비패키지(테스트/언패키지드) 환경에서는 GetFileFromApplicationUriAsync가 예외를 던지며, 상위 CreateGroupIconAsync catch에서 기본 폴백으로 흡수된다.
        var file = path.StartsWith("ms-appx:", StringComparison.OrdinalIgnoreCase)
            ? await StorageFile.GetFileFromApplicationUriAsync(new Uri(path)).AsTask(cancellationToken).ConfigureAwait(false)
            : await StorageFile.GetFileFromPathAsync(path).AsTask(cancellationToken).ConfigureAwait(false);
        using var stream = await file.OpenReadAsync().AsTask(cancellationToken).ConfigureAwait(false);
        return await DecodeStreamAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<SoftwareBitmap> DecodeThumbnailAsync(string path, CancellationToken cancellationToken)
    {
        var file = await StorageFile.GetFileFromPathAsync(path).AsTask(cancellationToken).ConfigureAwait(false);
        using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, CanvasSize)
            .AsTask(cancellationToken).ConfigureAwait(false);
        return await DecodeStreamAsync(thumb, cancellationToken).ConfigureAwait(false);
    }

    // 패키지 로고·이미지 파일·셸 썸네일 모두 동일한 디코드 경로를 거쳐 인코더 호환 포맷(BGRA8)으로 통일한다.
    private static async Task<SoftwareBitmap> DecodeStreamAsync(IRandomAccessStream stream, CancellationToken cancellationToken)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
        var bitmap = await decoder.GetSoftwareBitmapAsync().AsTask(cancellationToken).ConfigureAwait(false);
        return ToBgra8(bitmap);
    }

    private static SoftwareBitmap ToBgra8(SoftwareBitmap bitmap)
    {
        // 인코더는 BGRA8(Premultiplied)를 요구한다.
        if (bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8 && bitmap.BitmapAlphaMode == BitmapAlphaMode.Premultiplied)
            return bitmap;
        return SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }

    // ----- 인코딩/.ico 작성 -----

    private static async Task WriteIcoAsync(SoftwareBitmap bitmap, string outputPath, CancellationToken cancellationToken)
    {
        // 원본보다 큰 프레임은 만들지 않는다(업스케일=흐림). 최상위 프레임은 원본 크기로 두어 선명도를 보존한다.
        var native = Math.Min(256, Math.Max(bitmap.PixelWidth, bitmap.PixelHeight));
        var sizes = StandardSizes.Where(s => s <= native).ToList();
        if (sizes.Count == 0 || sizes[^1] != native)
            sizes.Add(native);

        // 큰 프레임부터 기록한다 → 크기 미지정 디코드(목록 표시)가 frame 0(가장 큰 프레임)을 선택해 선명하게 보이도록.
        var frames = new List<IconFrame>(sizes.Count);
        foreach (var size in sizes.OrderByDescending(s => s))
        {
            var png = await EncodeFrameAsync(bitmap, size, cancellationToken).ConfigureAwait(false);
            frames.Add(new IconFrame(size, png));
        }

        await IcoWriter.WriteAsync(outputPath, frames, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>목록 표시용 PNG를 원본 해상도 그대로 저장한다(GPU 축소로 선명, .ico 프레임 디코드 회피).</summary>
    private static async Task WritePngFileAsync(SoftwareBitmap bitmap, string path, CancellationToken cancellationToken)
    {
        var bytes = await EncodePngAsync(bitmap, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>종횡비를 보존(업스케일 금지)해 축소하고 size×size 투명 캔버스 중앙에 배치한 PNG 프레임을 만든다.</summary>
    private static async Task<byte[]> EncodeFrameAsync(SoftwareBitmap source, int size, CancellationToken cancellationToken)
    {
        int srcW = source.PixelWidth, srcH = source.PixelHeight;
        var scale = Math.Min((double)size / srcW, (double)size / srcH);
        if (scale > 1.0)
            scale = 1.0; // 업스케일 금지(흐림 방지)
        int tw = Math.Max(1, (int)Math.Round(srcW * scale));
        int th = Math.Max(1, (int)Math.Round(srcH * scale));

        using var scaled = await ScaleAsync(source, (uint)tw, (uint)th, cancellationToken).ConfigureAwait(false);

        // 정사각 프레임 중앙에 배치(byte 단위 복사, BGRA8 스트라이드 = width*4).
        var scaledBytes = new byte[tw * th * 4];
        scaled.CopyToBuffer(scaledBytes.AsBuffer());

        var canvas = new byte[size * size * 4]; // 0 = 완전 투명
        int left = (size - tw) / 2, top = (size - th) / 2;
        for (var r = 0; r < th; r++)
            Array.Copy(scaledBytes, r * tw * 4, canvas, ((top + r) * size + left) * 4, tw * 4);

        using var canvasBmp = SoftwareBitmap.CreateCopyFromBuffer(
            canvas.AsBuffer(), BitmapPixelFormat.Bgra8, size, size, BitmapAlphaMode.Premultiplied);
        return await EncodePngAsync(canvasBmp, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>소스를 지정 크기로 고품질(Fant) 축소한 BGRA8 SoftwareBitmap을 만든다(PNG 라운드트립).</summary>
    private static async Task<SoftwareBitmap> ScaleAsync(SoftwareBitmap source, uint width, uint height, CancellationToken cancellationToken)
    {
        using var ras = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ras).AsTask(cancellationToken).ConfigureAwait(false);
        encoder.SetSoftwareBitmap(source);
        encoder.BitmapTransform.ScaledWidth = width;
        encoder.BitmapTransform.ScaledHeight = height;
        encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);

        ras.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(ras).AsTask(cancellationToken).ConfigureAwait(false);
        var bitmap = await decoder.GetSoftwareBitmapAsync().AsTask(cancellationToken).ConfigureAwait(false);
        return ToBgra8(bitmap);
    }

    private static async Task<byte[]> EncodePngAsync(SoftwareBitmap bitmap, CancellationToken cancellationToken)
    {
        using var ras = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ras).AsTask(cancellationToken).ConfigureAwait(false);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);

        var bytes = new byte[ras.Size];
        using var reader = new DataReader(ras.GetInputStreamAt(0));
        await reader.LoadAsync((uint)ras.Size).AsTask(cancellationToken).ConfigureAwait(false);
        reader.ReadBytes(bytes);
        return bytes;
    }

    // ----- 내장/기본 단색 아이콘 -----

    // 기본 아이콘 색(BGRA): 짙은 파랑 계열.
    private static (byte B, byte G, byte R, byte A) DefaultColor => (0xB0, 0x6A, 0x33, 0xFF);

    private static (byte B, byte G, byte R, byte A) ColorForBuiltIn(string id) => id switch
    {
        "red" => (0x47, 0x44, 0xE0, 0xFF),
        "green" => (0x5A, 0xB0, 0x4C, 0xFF),
        "orange" => (0x16, 0x9A, 0xF5, 0xFF),
        "purple" => (0xB0, 0x47, 0x88, 0xFF),
        _ => DefaultColor
    };

    private static SoftwareBitmap CreateSolidBitmap((byte B, byte G, byte R, byte A) color)
    {
        var pixels = new byte[CanvasSize * CanvasSize * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i + 0] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = color.A;
        }

        return SoftwareBitmap.CreateCopyFromBuffer(
            pixels.AsBuffer(), BitmapPixelFormat.Bgra8, CanvasSize, CanvasSize, BitmapAlphaMode.Premultiplied);
    }
}
