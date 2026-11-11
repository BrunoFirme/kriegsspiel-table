using System;
using System.Threading.Tasks;
using Godot;
using Rgb = System.Numerics.Vector3;

namespace KTable.App;

static class BlockCompress
{
    const int Side = 4, Pixels = Side * Side, Channels = 4, ColourBlockBytes = 8, AlphaBlockBytes = 8;
    const int AxisSteps = 4, RefineRounds = 2;
    const float Flat = 1e-3f, Most = 255;
    static readonly float[] NearWeight = { 1, 0, 2 / 3f, 1 / 3f };

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
            EncodeLevel(src, (int)from, lw, lh, dst, (int)at, alpha);
        }
        var packed = Image.CreateFromData(w, h, true, format, dst);
        return packed == null || packed.IsEmpty() ? null : packed;
    }

    static void EncodeLevel(byte[] src, int from, int w, int h, byte[] dst, int at, bool alpha)
    {
        int across = (w + Side - 1) / Side, down = (h + Side - 1) / Side;
        int blockBytes = alpha ? AlphaBlockBytes + ColourBlockBytes : ColourBlockBytes;
        Parallel.For(0, down, by =>
        {
            var px = src.AsSpan(from, w * h * Channels);
            Span<byte> block = stackalloc byte[Pixels * Channels];
            int o = at + by * across * blockBytes;
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
                if (alpha) { AlphaBlock(block, dst.AsSpan(o, AlphaBlockBytes)); o += AlphaBlockBytes; }
                ColourBlock(block, dst.AsSpan(o, ColourBlockBytes));
                o += ColourBlockBytes;
            }
        });
    }

    static void ColourBlock(ReadOnlySpan<byte> block, Span<byte> dst)
    {
        Span<Rgb> px = stackalloc Rgb[Pixels];
        for (int i = 0; i < Pixels; i++) px[i] = new Rgb(block[i * Channels], block[i * Channels + 1], block[i * Channels + 2]);
        Span<byte> picks = stackalloc byte[Pixels], trial = stackalloc byte[Pixels];
        var (a, b) = Ends(px);
        var (c0, c1, error) = Fit(px, a, b, picks);
        for (int round = 0; round < RefineRounds && error > 0; round++)
        {
            if (!Refit(px, picks, c0, c1, out a, out b)) break;
            var (n0, n1, nextError) = Fit(px, a, b, trial);
            if (nextError >= error) break;
            (c0, c1, error) = (n0, n1, nextError);
            trial.CopyTo(picks);
        }
        dst[0] = (byte)c0;
        dst[1] = (byte)(c0 >> 8);
        dst[2] = (byte)c1;
        dst[3] = (byte)(c1 >> 8);
        for (int y = 0; y < Side; y++)
        {
            int row = 0;
            for (int x = 0; x < Side; x++) row |= picks[y * Side + x] << (2 * x);
            dst[4 + y] = (byte)row;
        }
    }

    static (Rgb A, Rgb B) Ends(ReadOnlySpan<Rgb> px)
    {
        var mean = Rgb.Zero;
        var lo = new Rgb(Most);
        var hi = Rgb.Zero;
        foreach (var p in px)
        {
            mean += p;
            lo = Rgb.Min(lo, p);
            hi = Rgb.Max(hi, p);
        }
        mean /= Pixels;
        Rgb own = Rgb.Zero, shared = Rgb.Zero;
        foreach (var p in px)
        {
            var d = p - mean;
            own += d * d;
            shared += new Rgb(d.X * d.Y, d.X * d.Z, d.Y * d.Z);
        }
        var axis = hi - lo;
        for (int i = 0; i < AxisSteps; i++)
        {
            var next = new Rgb(
                own.X * axis.X + shared.X * axis.Y + shared.Y * axis.Z,
                shared.X * axis.X + own.Y * axis.Y + shared.Z * axis.Z,
                shared.Y * axis.X + shared.Z * axis.Y + own.Z * axis.Z);
            float size = Math.Max(Math.Abs(next.X), Math.Max(Math.Abs(next.Y), Math.Abs(next.Z)));
            if (size < Flat) break;
            axis = next / size;
        }
        if (axis.LengthSquared() < Flat) return (mean, mean);
        int far = 0, near = 0;
        float most = float.MinValue, least = float.MaxValue;
        for (int i = 0; i < Pixels; i++)
        {
            float t = Rgb.Dot(px[i] - mean, axis);
            if (t > most) { most = t; far = i; }
            if (t < least) { least = t; near = i; }
        }
        return (px[far], px[near]);
    }

    static (ushort C0, ushort C1, float Error) Fit(ReadOnlySpan<Rgb> px, Rgb a, Rgb b, Span<byte> picks)
    {
        ushort c0 = To565(a), c1 = To565(b);
        if (c0 < c1) (c0, c1) = (c1, c0);
        Span<Rgb> colours = stackalloc Rgb[4];
        colours[0] = From565(c0);
        colours[1] = From565(c1);
        colours[2] = (2 * colours[0] + colours[1]) / 3;
        colours[3] = (colours[0] + 2 * colours[1]) / 3;
        int choices = c0 == c1 ? 1 : 4;
        float error = 0;
        for (int i = 0; i < Pixels; i++)
        {
            int best = 0;
            float bestError = float.MaxValue;
            for (int k = 0; k < choices; k++)
            {
                float e = Rgb.DistanceSquared(px[i], colours[k]);
                if (e < bestError) { bestError = e; best = k; }
            }
            picks[i] = (byte)best;
            error += bestError;
        }
        return (c0, c1, error);
    }

    static bool Refit(ReadOnlySpan<Rgb> px, ReadOnlySpan<byte> picks, ushort c0, ushort c1, out Rgb a, out Rgb b)
    {
        a = b = Rgb.Zero;
        if (c0 == c1) return false;
        var weights = Rgb.Zero;
        Rgb ap = Rgb.Zero, bp = Rgb.Zero;
        for (int i = 0; i < Pixels; i++)
        {
            float w = NearWeight[picks[i]], v = 1 - w;
            weights += new Rgb(w * w, w * v, v * v);
            ap += w * px[i];
            bp += v * px[i];
        }
        float aa = weights.X, ab = weights.Y, bb = weights.Z;
        float det = aa * bb - ab * ab;
        if (Math.Abs(det) < Flat) return false;
        a = Rgb.Clamp((bb * ap - ab * bp) / det, Rgb.Zero, new Rgb(Most));
        b = Rgb.Clamp((aa * bp - ab * ap) / det, Rgb.Zero, new Rgb(Most));
        return true;
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

    static ushort To565(Rgb c) => (ushort)(Level(c.X, 31) << 11 | Level(c.Y, 63) << 5 | Level(c.Z, 31));

    static int Level(float v, int top) => Math.Clamp((int)MathF.Round(v * top / Most), 0, top);

    static Rgb From565(ushort c)
    {
        int r = c >> 11, g = (c >> 5) & 63, b = c & 31;
        return new Rgb((r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2));
    }
}
