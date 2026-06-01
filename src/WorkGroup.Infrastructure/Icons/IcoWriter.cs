using System.Buffers.Binary;

namespace WorkGroup.Infrastructure.Icons;

/// <summary>아이콘 한 프레임. PNG로 인코딩된 정사각 이미지.</summary>
public readonly record struct IconFrame(int Size, byte[] PngBytes);

/// <summary>
/// PNG 프레임들을 .ico 컨테이너로 묶는 순수 라이터(plan.md D16).
/// 각 프레임은 PNG 압축 프레임으로 저장한다(.ico 표준 — 256px 포함 가능).
/// </summary>
public static class IcoWriter
{
    private const int IconDirSize = 6;       // ICONDIR
    private const int IconDirEntrySize = 16;  // ICONDIRENTRY

    /// <summary>프레임들로 .ico 바이트 배열을 만든다.</summary>
    public static byte[] Build(IReadOnlyList<IconFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
            throw new ArgumentException("최소 한 개의 프레임이 필요합니다.", nameof(frames));

        var totalSize = IconDirSize + (IconDirEntrySize * frames.Count) + frames.Sum(f => f.PngBytes.Length);
        var buffer = new byte[totalSize];
        var span = buffer.AsSpan();

        // ICONDIR: reserved(2)=0, type(2)=1(아이콘), count(2)
        BinaryPrimitives.WriteUInt16LittleEndian(span[0..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], (ushort)frames.Count);

        // 각 프레임의 PNG 데이터는 디렉터리 엔트리 뒤에 순서대로 배치된다.
        var imageOffset = IconDirSize + (IconDirEntrySize * frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            var entryOffset = IconDirSize + (i * IconDirEntrySize);

            // width/height: 256은 0으로 표기한다(.ico 규약).
            span[entryOffset + 0] = (byte)(frame.Size >= 256 ? 0 : frame.Size); // width
            span[entryOffset + 1] = (byte)(frame.Size >= 256 ? 0 : frame.Size); // height
            span[entryOffset + 2] = 0; // colorCount(팔레트 미사용)
            span[entryOffset + 3] = 0; // reserved
            BinaryPrimitives.WriteUInt16LittleEndian(span[(entryOffset + 4)..], 1);  // planes
            BinaryPrimitives.WriteUInt16LittleEndian(span[(entryOffset + 6)..], 32); // bitCount
            BinaryPrimitives.WriteUInt32LittleEndian(span[(entryOffset + 8)..], (uint)frame.PngBytes.Length); // bytesInRes
            BinaryPrimitives.WriteUInt32LittleEndian(span[(entryOffset + 12)..], (uint)imageOffset);           // imageOffset

            frame.PngBytes.CopyTo(span[imageOffset..]);
            imageOffset += frame.PngBytes.Length;
        }

        return buffer;
    }

    /// <summary>프레임들로 .ico 파일을 기록한다.</summary>
    public static async Task WriteAsync(string path, IReadOnlyList<IconFrame> frames, CancellationToken cancellationToken = default)
    {
        var bytes = Build(frames);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
    }
}
