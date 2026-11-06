using System.Buffers.Binary;

namespace KTable.Core;

public static class PictureHeader
{
    public const int HeadBytes = 256 * 1024;
    const byte JpegMarker = 0xFF, JpegStart = 0xD8, JpegScan = 0xDA;

    public static (int W, int H)? Of(string path)
    {
        try
        {
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var head = new byte[(int)Math.Min(HeadBytes, f.Length)];
            f.ReadExactly(head);
            return Read(head, Path.GetExtension(path));
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
        if (b.Length >= 4 && b[0] == JpegMarker && b[1] == JpegStart) return Jpeg(b);
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

    static (int, int)? Jpeg(ReadOnlySpan<byte> b)
    {
        int i = 2;
        while (i + 9 <= b.Length)
        {
            if (b[i] != JpegMarker) return null;
            byte m = b[i + 1];
            if (m == JpegMarker) { i++; continue; }
            if (m is >= 0xD0 and <= 0xD7 or 0x01) { i += 2; continue; }
            if (m == JpegScan) return null;
            int len = BinaryPrimitives.ReadUInt16BigEndian(b[(i + 2)..]);
            if (IsFrameStart(m)) return Sides(BinaryPrimitives.ReadUInt16BigEndian(b[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(b[(i + 5)..]));
            if (len < 2) return null;
            i += 2 + len;
        }
        return null;
    }

    static bool IsFrameStart(byte m) => m is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC);
}
