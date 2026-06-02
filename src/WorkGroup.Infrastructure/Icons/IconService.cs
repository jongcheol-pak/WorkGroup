using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using WorkGroup.Application.Icons;
using WorkGroup.Domain.Common;
using WorkGroup.Domain.Groups;

namespace WorkGroup.Infrastructure.Icons;

/// <summary>
/// WIC(Windows.Graphics.Imaging)로 이미지를 디코드·리사이즈·PNG 인코딩하고 IcoWriter로 .ico를 만든다(plan.md D16).
/// 어떤 단계든 실패하면 기본 내장 아이콘으로 대체한다(plan.md T5 Edge Cases).
/// </summary>
public sealed class IconService : IIconService
{
    // .ico에 담을 다중 해상도. 256은 PNG 프레임으로 저장된다.
    private static readonly uint[] FrameSizes = { 256, 48, 32, 16 };
    private const int CanvasSize = 256;

    private readonly ILogger<IconService> _logger;

    public IconService(ILogger<IconService>? logger = null)
        => _logger = logger ?? NullLogger<IconService>.Instance;

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
            return Result<string>.Fail("출력 디렉터리가 지정되지 않았습니다.");

        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, groupId.Value + ".ico");

        try
        {
            var bitmap = await ResolveBitmapAsync(source, members, cancellationToken).ConfigureAwait(false);
            await WriteIcoAsync(bitmap, outputPath, cancellationToken).ConfigureAwait(false);
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
            return Result<string>.Ok(outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "기본 아이콘 생성에 실패했습니다: {Path}", outputPath);
            return Result<string>.Fail("아이콘을 생성하지 못했습니다.");
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
        // 패키지(Store/UWP) 앱은 셸 공식 로고를 우선 사용한다(package.Logo 경로가 없어도 아이콘 확보 — plan.md T2).
        if (member.Kind == AppKind.Packaged)
        {
            using var logo = await PackagedAppIcon
                .OpenIconStreamAsync(member.LaunchTarget, (uint)CanvasSize, cancellationToken).ConfigureAwait(false);
            if (logo is not null)
                return await DecodeStreamAsync(logo, cancellationToken).ConfigureAwait(false);
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
        var frames = new List<IconFrame>(FrameSizes.Length);
        foreach (var size in FrameSizes)
        {
            var png = await EncodePngAsync(bitmap, size, cancellationToken).ConfigureAwait(false);
            frames.Add(new IconFrame((int)size, png));
        }

        await IcoWriter.WriteAsync(outputPath, frames, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> EncodePngAsync(SoftwareBitmap bitmap, uint size, CancellationToken cancellationToken)
    {
        using var ras = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ras).AsTask(cancellationToken).ConfigureAwait(false);
        encoder.SetSoftwareBitmap(bitmap);
        encoder.BitmapTransform.ScaledWidth = size;
        encoder.BitmapTransform.ScaledHeight = size;
        encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
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
