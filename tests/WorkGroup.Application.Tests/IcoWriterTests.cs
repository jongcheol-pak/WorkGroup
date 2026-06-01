using System.Buffers.Binary;
using WorkGroup.Infrastructure.Icons;
using Xunit;

namespace WorkGroup.Application.Tests;

/// <summary>IcoWriter의 .ico 컨테이너 구조 검증(순수 로직).</summary>
public class IcoWriterTests
{
    // 구조 검증용 더미 PNG 바이트(실제 디코딩하지 않으므로 임의 값으로 충분).
    private static byte[] FakePng(int len, byte fill) => Enumerable.Repeat(fill, len).ToArray();

    [Fact]
    public void Build_빈_프레임이면_예외()
    {
        Assert.Throws<ArgumentException>(() => IcoWriter.Build(Array.Empty<IconFrame>()));
    }

    [Fact]
    public void Build_헤더는_아이콘_타입과_프레임_수를_담는다()
    {
        var ico = IcoWriter.Build(new[]
        {
            new IconFrame(32, FakePng(10, 0xAA)),
            new IconFrame(48, FakePng(20, 0xBB))
        });

        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(0)));  // reserved
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)));  // type=아이콘
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4)));  // count
    }

    [Fact]
    public void Build_256_프레임의_치수는_0으로_표기된다()
    {
        var ico = IcoWriter.Build(new[] { new IconFrame(256, FakePng(5, 0x01)) });

        // 첫 엔트리는 오프셋 6부터. width/height 바이트가 0이어야 한다.
        Assert.Equal(0, ico[6]); // width
        Assert.Equal(0, ico[7]); // height
    }

    [Fact]
    public void Build_일반_치수는_그대로_표기된다()
    {
        var ico = IcoWriter.Build(new[] { new IconFrame(48, FakePng(5, 0x01)) });

        Assert.Equal(48, ico[6]);
        Assert.Equal(48, ico[7]);
    }

    [Fact]
    public void Build_엔트리의_오프셋과_길이가_PNG_데이터를_정확히_가리킨다()
    {
        var png1 = FakePng(10, 0xAA);
        var png2 = FakePng(20, 0xBB);
        var ico = IcoWriter.Build(new[] { new IconFrame(32, png1), new IconFrame(48, png2) });

        // 엔트리1: offset 6, 엔트리2: offset 22. 데이터 시작 = 6 + 16*2 = 38.
        var len1 = BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(6 + 8));
        var off1 = BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(6 + 12));
        var len2 = BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(22 + 8));
        var off2 = BinaryPrimitives.ReadUInt32LittleEndian(ico.AsSpan(22 + 12));

        Assert.Equal((uint)10, len1);
        Assert.Equal((uint)38, off1);
        Assert.Equal((uint)20, len2);
        Assert.Equal((uint)48, off2); // 38 + 10

        // 실제 데이터가 해당 위치에 들어있는지(첫 바이트로 확인)
        Assert.Equal(0xAA, ico[(int)off1]);
        Assert.Equal(0xBB, ico[(int)off2]);
    }

    [Fact]
    public async Task WriteAsync_파일을_생성한다()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wg_{Guid.NewGuid():N}.ico");
        try
        {
            await IcoWriter.WriteAsync(path, new[] { new IconFrame(32, FakePng(8, 0xCC)) });
            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 6);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
