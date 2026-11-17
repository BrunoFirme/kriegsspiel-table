using System.Buffers.Binary;

namespace KTable.Core;

public static class PictureHeader
{
    public const int HeadBytes = 256 * 1024;
    const byte JpegMarker = 0xFF, JpegStart = 0xD8, JpegEnd = 0xD9, JpegScan = 0xDA;
    const int JpegLengthBytes = 2, JpegFrameBytes = 7, JpegFrameHeight = 3, JpegFrameWidth = 5;
    public static readonly (int W, int H) NoFrame = (0, 0);

    public static (int W, int H)? Of(string path)
    {
        try
        {
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var head = new byte[(int)Math.Min(HeadBytes, f.Length)];
            f.ReadExactly(head);
            return IsJpeg(head) ? Jpeg(f) : Read(head, Path.GetExtension(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    public static (int W, int H)? Read(ReadOnlySpan<byte> b, string? extension = null)
    {
        if (b.Length >= 24 && b[..8].SequenceEqual(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }) && b[12..16].SequenceEqual("IHDR"u8))
            return Sides(BinaryPrimitives.ReadUInt32BigEndian(b[16..]), BinaryPrimitives.ReadUInt32BigEndian(b[20..]));
        if (b.Length >= 10 && b[..4].SequenceEqual("GIF8"u8))
            return Sides(BinaryPrimitives.ReadUInt16LittleEndian(b[6..]), BinaryPrimitives.ReadUInt16LittleEndian(b[8..]));
        if (b.Length >= 26 && b[0] == 'B' && b[1] == 'M') return Bmp(b);
        if (b.Length >= 30 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8)) return Webp(b);
        if (IsJpeg(b)) return Jpeg(new MemoryStream(b.ToArray(), writable: false));
        if (b.Length >= 18 && extension?.TrimStart('.').Equals("tga", StringComparison.OrdinalIgnoreCase) == true)
            return Sides(BinaryPrimitives.ReadUInt16LittleEndian(b[12..]), BinaryPrimitives.ReadUInt16LittleEndian(b[14..]));
        return null;
    }

    static (int, int) Sides(long w, long h) => ((int)Math.Min(w, int.MaxValue), (int)Math.Min(h, int.MaxValue));

    static (int, int) Bmp(ReadOnlySpan<byte> b)
    {
        const int CoreHeaderSize = 12;
        if (BinaryPrimitives.ReadUInt32LittleEndian(b[14..]) == CoreHeaderSize)
            return Sides(BinaryPrimitives.ReadUInt16LittleEndian(b[18..]), BinaryPrimitives.ReadUInt16LittleEndian(b[20..]));
        return Sides(Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(b[18..])), Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(b[22..])));
    }

    static (int, int)? Webp(ReadOnlySpan<byte> b)
    {
        const int Lossy14Bits = 0x3FFF;
        var chunk = b[12..16];
        if (chunk.SequenceEqual("VP8 "u8))
            return Sides(BinaryPrimitives.ReadUInt16LittleEndian(b[26..]) & Lossy14Bits, BinaryPrimitives.ReadUInt16LittleEndian(b[28..]) & Lossy14Bits);
        if (chunk.SequenceEqual("VP8L"u8))
        {
            uint bits = BinaryPrimitives.ReadUInt32LittleEndian(b[21..]);
            return Sides((bits & Lossy14Bits) + 1, ((bits >> 14) & Lossy14Bits) + 1);
        }
        if (chunk.SequenceEqual("VP8X"u8))
            return Sides((b[24] | b[25] << 8 | b[26] << 16) + 1L, (b[27] | b[28] << 8 | b[29] << 16) + 1L);
        return null;
    }

    static bool IsJpeg(ReadOnlySpan<byte> b) => b.Length >= 4 && b[0] == JpegMarker && b[1] == JpegStart;

    static (int, int) Jpeg(Stream f)
    {
        Span<byte> head = stackalloc byte[JpegFrameBytes];
        f.Position = JpegLengthBytes;
        while (true)
        {
            int m = NextMarker(f);
            if (m is < 0 or JpegScan or JpegEnd) return NoFrame;
            if (m is >= 0xD0 and <= 0xD7 or 0x01) continue;
            var want = head[..(IsFrameStart((byte)m) ? JpegFrameBytes : JpegLengthBytes)];
            if (f.ReadAtLeast(want, want.Length, throwOnEndOfStream: false) < want.Length) return NoFrame;
            if (want.Length == JpegFrameBytes) return Sides(BinaryPrimitives.ReadUInt16BigEndian(want[JpegFrameWidth..]), BinaryPrimitives.ReadUInt16BigEndian(want[JpegFrameHeight..]));
            int len = BinaryPrimitives.ReadUInt16BigEndian(want);
            if (len < JpegLengthBytes) return NoFrame;
            f.Seek(len - JpegLengthBytes, SeekOrigin.Current);
        }
    }

    static int NextMarker(Stream f)
    {
        for (int was = 0, b = f.ReadByte(), read = 0; b >= 0 && read < HeadBytes; was = b, b = f.ReadByte(), read++)
            if (was == JpegMarker && b is not (0 or JpegMarker)) return b;
        return -1;
    }

    static bool IsFrameStart(byte m) => m is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC);
}
