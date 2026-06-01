using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WorkGroup.Domain.Groups;
using WorkGroup.Infrastructure.Icons;
using Xunit;

namespace WorkGroup.Application.Tests;

/// <summary>IconService가 각 소스에서 유효한 .ico 파일을 생성하는지 실제 검증.</summary>
public sealed class IconServiceTests : IDisposable
{
    private readonly string _dir;

    public IconServiceTests()
        => _dir = Path.Combine(Path.GetTempPath(), "WorkGroupIconTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private static void AssertValidIco(string path)
    {
        Assert.True(File.Exists(path), "ico 파일이 생성되어야 한다.");
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 6, "ico 크기가 헤더보다 커야 한다.");
        // ICONDIR magic: reserved=0, type=1
        Assert.Equal(0, bytes[0]);
        Assert.Equal(0, bytes[1]);
        Assert.Equal(1, bytes[2]);
        Assert.Equal(0, bytes[3]);
        // 프레임 수 = 4(256/48/32/16)
        Assert.Equal(4, bytes[4]);
    }

    [Fact]
    public async Task BuiltIn_소스로_ico_생성()
    {
        var sut = new IconService();
        var groupId = GroupId.New();

        var result = await sut.CreateGroupIconAsync(groupId, IconSource.BuiltIn("green"),
            Array.Empty<AppEntry>(), _dir);

        Assert.True(result.IsSuccess);
        Assert.Equal(Path.Combine(_dir, groupId.Value + ".ico"), result.Value);
        AssertValidIco(result.Value);
    }

    [Fact]
    public async Task CustomImage_소스로_ico_생성()
    {
        var pngPath = await CreateTempPngAsync(64, (0x20, 0x80, 0xF0, 0xFF));
        var sut = new IconService();
        var groupId = GroupId.New();

        var result = await sut.CreateGroupIconAsync(groupId, IconSource.FromCustomImage(pngPath),
            Array.Empty<AppEntry>(), _dir);

        Assert.True(result.IsSuccess);
        AssertValidIco(result.Value);
    }

    [Fact]
    public async Task CustomImage_없는_파일이면_기본아이콘으로_대체()
    {
        var sut = new IconService();
        var groupId = GroupId.New();

        var result = await sut.CreateGroupIconAsync(groupId,
            IconSource.FromCustomImage(Path.Combine(_dir, "missing.png")),
            Array.Empty<AppEntry>(), _dir);

        // 실패 대신 기본 아이콘으로 .ico가 생성되어야 한다.
        Assert.True(result.IsSuccess);
        AssertValidIco(result.Value);
    }

    [Fact]
    public async Task MemberApp_exe에서_아이콘_추출_경로_동작()
    {
        // 실제 시스템 exe로 셸 썸네일 추출 경로를 운동시킨다(추출 실패 시 기본 아이콘으로 대체되어도 유효 .ico).
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
        Assert.True(File.Exists(exe), "테스트 전제: notepad.exe 존재");

        var member = new AppEntry("메모장", exe, AppKind.Win32, exe);
        var sut = new IconService();

        var result = await sut.CreateGroupIconAsync(GroupId.New(),
            IconSource.FromMemberApp(exe), new[] { member }, _dir);

        Assert.True(result.IsSuccess);
        AssertValidIco(result.Value);
    }

    [Fact]
    public async Task 생성된_ico는_디코더로_열린다()
    {
        var sut = new IconService();
        var result = await sut.CreateGroupIconAsync(GroupId.New(), IconSource.DefaultBuiltIn,
            Array.Empty<AppEntry>(), _dir);

        Assert.True(result.IsSuccess);
        var bytes = await File.ReadAllBytesAsync(result.Value);
        using var ras = new InMemoryRandomAccessStream();
        await ras.WriteAsync(bytes.AsBuffer());
        ras.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(ras);
        Assert.True(decoder.PixelWidth > 0 && decoder.PixelHeight > 0);
    }

    private async Task<string> CreateTempPngAsync(int size, (byte B, byte G, byte R, byte A) color)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, $"img_{Guid.NewGuid():N}.png");

        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i + 0] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = color.A;
        }

        var bmp = SoftwareBitmap.CreateCopyFromBuffer(
            pixels.AsBuffer(), BitmapPixelFormat.Bgra8, size, size, BitmapAlphaMode.Premultiplied);

        using var ras = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ras);
        encoder.SetSoftwareBitmap(bmp);
        await encoder.FlushAsync();

        var bytes = new byte[ras.Size];
        using var reader = new DataReader(ras.GetInputStreamAt(0));
        await reader.LoadAsync((uint)ras.Size);
        reader.ReadBytes(bytes);
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }
}
