using System;
using Godot;

namespace KTable.App;

static class BlockCompress
{
    const int Side = 4, Pixels = Side * Side, Channels = 4, ColourBlockBytes = 8, AlphaBlockBytes = 8;

    public static Image? Compress(Image rgba, bool alpha)
    {
        int w = rgba.GetWidth(), h = rgba.GetHeight();
        var format = alpha ? Image.Format.Dxt5 : Image.Format.Dxt1;
        int blockBytes = alpha ? AlphaBlockBytes + ColourBlockBytes : ColourBlockBytes;
        using var shaped = Image.CreateEmpty(w, h, true, format);
        if (shaped == null || shaped.IsEmpty()) return null;
        int levels = shaped.GetMipmapCount() + 1;
        if (rgba.GetMipmapCount() + 1 < levels) return null;
        var src = rgba.GetData();
        var dst = new byte[shaped.GetData().Length];
        for (int level = 0; level < levels; level++)
        {
            int lw = Math.Max(1, w >> level), lh = Math.Max(1, h >> level);
            long at = shaped.GetMipmapOffset(level), from = rgba.GetMipmapOffset(level);
            long room = (level + 1 < levels ? shaped.GetMipmapOffset(level + 1) : dst.Length) - at;
            long need = (long)((lw + Side - 1) / Side) * ((lh + Side - 1) / Side) * blockBytes;
            if (need > room || from + (long)lw * lh * Channels > src.Length) return null;
            EncodeLevel(src.AsSpan((int)from, lw * lh * Channels), lw, lh, dst.AsSpan((int)at, (int)need), alpha);
        }
        var packed = Image.CreateFromData(w, h, true, format, dst);
        return packed == null || packed.IsEmpty() ? null : packed;
    }

    static void EncodeLevel(ReadOnlySpan<byte> px, int w, int h, Span<byte> dst, bool alpha)
    {
        Span<byte> block = stackalloc byte[Pixels * Channels];
        int across = (w + Side - 1) / Side, down = (h + Side - 1) / Side, o = 0;
        for (int by = 0; by < down; by++)
            for (int bx = 0; bx < across; bx++)
            {
                bool whole = bx * Side + Side <= w && by * Side + Side <= h;
                for (int y = 0; y < Side; y++)
                {
                    int sy = Math.Min(h - 1, by * Side + y);
                    if (whole) { px.Slice((sy * w + bx * Side) * Channels, Side * Channels).CopyTo(block.Slice(y * Side * Channels, Side * Channels)); continue; }
                    for (int x = 0; x < Side; x++)
                    {
                        int sx = Math.Min(w - 1, bx * Side + x);
                        px.Slice((sy * w + sx) * Channels, Channels).CopyTo(block.Slice((y * Side + x) * Channels, Channels));
                    }
                }
                if (alpha) { AlphaBlock(block, dst.Slice(o, AlphaBlockBytes)); o += AlphaBlockBytes; }
                ColourBlock(block, dst.Slice(o, ColourBlockBytes));
                o += ColourBlockBytes;
            }
    }

    static void ColourBlock(ReadOnlySpan<byte> block, Span<byte> dst)
    {
        int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
        for (int i = 0; i < Pixels; i++)
        {
            int r = block[i * Channels], g = block[i * Channels + 1], b = block[i * Channels + 2];
            minR = Math.Min(minR, r);
            maxR = Math.Max(maxR, r);
            minG = Math.Min(minG, g);
            maxG = Math.Max(maxG, g);
            minB = Math.Min(minB, b);
            maxB = Math.Max(maxB, b);
        }
        int dr = maxR - minR, dg = maxG - minG, db = maxB - minB;
        int lo = int.MaxValue, hi = int.MinValue, loAt = 0, hiAt = 0;
        for (int i = 0; i < Pixels; i++)
        {
            int d = block[i * Channels] * dr + block[i * Channels + 1] * dg + block[i * Channels + 2] * db;
            if (d < lo) { lo = d; loAt = i; }
            if (d > hi) { hi = d; hiAt = i; }
        }
        ushort c0 = To565(block.Slice(hiAt * Channels)), c1 = To565(block.Slice(loAt * Channels));
        if (c0 < c1) (c0, c1) = (c1, c0);
        dst[0] = (byte)c0;
        dst[1] = (byte)(c0 >> 8);
        dst[2] = (byte)c1;
        dst[3] = (byte)(c1 >> 8);
        if (c0 == c1) { dst[4] = dst[5] = dst[6] = dst[7] = 0; return; }
        Span<int> far = stackalloc int[3], near = stackalloc int[3];
        From565(c0, far);
        From565(c1, near);
        int ar = far[0] - near[0], ag = far[1] - near[1], ab = far[2] - near[2];
        int span = ar * ar + ag * ag + ab * ab;
        for (int y = 0; y < Side; y++)
        {
            int row = 0;
            for (int x = 0; x < Side; x++)
            {
                int i = (y * Side + x) * Channels;
                int along = (block[i] - near[0]) * ar + (block[i + 1] - near[1]) * ag + (block[i + 2] - near[2]) * ab;
                int index = along * 6 < span ? 1 : along * 2 < span ? 3 : along * 6 < 5 * span ? 2 : 0;
                row |= index << (2 * x);
            }
            dst[4 + y] = (byte)row;
        }
    }

    static void AlphaBlock(ReadOnlySpan<byte> block, Span<byte> dst)
    {
        int a0 = 0, a1 = 255;
        for (int i = 0; i < Pixels; i++)
        {
            int a = block[i * Channels + 3];
            a0 = Math.Max(a0, a);
            a1 = Math.Min(a1, a);
        }
        dst[0] = (byte)a0;
        dst[1] = (byte)a1;
        if (a0 == a1) { dst.Slice(2, 6).Clear(); return; }
        int range = a0 - a1;
        ulong bits = 0;
        for (int i = 0; i < Pixels; i++)
        {
            int step = ((a0 - block[i * Channels + 3]) * 7 * 2 + range) / (2 * range);
            int index = step == 0 ? 0 : step == 7 ? 1 : step + 1;
            bits |= (ulong)index << (3 * i);
        }
        for (int j = 0; j < 6; j++) dst[2 + j] = (byte)(bits >> (8 * j));
    }

    static ushort To565(ReadOnlySpan<byte> rgb) => (ushort)((rgb[0] >> 3) << 11 | (rgb[1] >> 2) << 5 | (rgb[2] >> 3));

    static void From565(ushort c, Span<int> rgb)
    {
        int r = c >> 11, g = (c >> 5) & 63, b = c & 31;
        rgb[0] = (r << 3) | (r >> 2);
        rgb[1] = (g << 2) | (g >> 4);
        rgb[2] = (b << 3) | (b >> 2);
    }
}
