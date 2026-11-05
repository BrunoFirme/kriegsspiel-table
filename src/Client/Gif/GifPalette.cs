namespace KTable.Client.Gif;

public sealed class GifPalette
{
    public const int Size = 256;
    public const byte Clear = Size - 1;
    const int KeyBits = 5, KeyLevels = 1 << KeyBits, Keys = KeyLevels * KeyLevels * KeyLevels, DropBits = 8 - KeyBits;
    const int DitherSpread = 12, DitherCells = 16;
    const int RedWeight = 3, GreenWeight = 4, BlueWeight = 2;

    static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

    static readonly (byte r, byte g, byte b)[] Kept =
    {
        (255, 240, 200), (255, 214, 120), (255, 170, 60), (240, 120, 30), (200, 70, 20), (150, 40, 15),
        (232, 232, 228), (200, 200, 196), (168, 168, 166), (130, 130, 130), (92, 92, 94), (60, 60, 62),
        (0, 0, 0), (255, 255, 255),
    };

    static readonly int[] Dither = Bayer.Select(v => (int)Math.Round(((v + 0.5) / DitherCells - 0.5) * DitherSpread)).ToArray();

    readonly byte[] _rgb = new byte[Size * 3];
    readonly int _used;
    readonly byte[] _nearest = new byte[Keys];
    readonly bool[] _known = new bool[Keys];

    GifPalette(IReadOnlyList<(byte r, byte g, byte b)> colours)
    {
        _used = Math.Min(colours.Count, Clear);
        for (int i = 0; i < _used; i++)
        {
            _rgb[i * 3] = colours[i].r;
            _rgb[i * 3 + 1] = colours[i].g;
            _rgb[i * 3 + 2] = colours[i].b;
        }
    }

    public ReadOnlySpan<byte> Rgb => _rgb;

    public static GifPalette From(ReadOnlySpan<byte> rgba)
    {
        var count = new long[Keys];
        var sums = new long[Keys * 3];
        for (int i = 0; i + 3 < rgba.Length; i += 4)
        {
            int key = Key(rgba[i], rgba[i + 1], rgba[i + 2]);
            count[key]++;
            sums[key * 3] += rgba[i];
            sums[key * 3 + 1] += rgba[i + 1];
            sums[key * 3 + 2] += rgba[i + 2];
        }
        var colours = MedianCut(count, sums, Clear - Kept.Length);
        colours.AddRange(Kept);
        return new GifPalette(colours);
    }

    public void Index(ReadOnlySpan<byte> rgba, int width, int height, Span<byte> into)
    {
        for (int y = 0; y < height; y++)
        {
            int row = (y & 3) * 4;
            for (int x = 0; x < width; x++)
            {
                int p = (y * width + x) * 4;
                int shift = Dither[row + (x & 3)];
                into[y * width + x] = Nearest(Key(Shifted(rgba[p], shift), Shifted(rgba[p + 1], shift), Shifted(rgba[p + 2], shift)));
            }
        }
    }

    static int Shifted(byte v, int by) => Math.Clamp(v + by, 0, byte.MaxValue);

    static int Key(int r, int g, int b) => (r >> DropBits) << (KeyBits * 2) | (g >> DropBits) << KeyBits | b >> DropBits;

    static int Expand(int level) => level << DropBits | level >> (KeyBits - DropBits);

    byte Nearest(int key)
    {
        if (_known[key]) return _nearest[key];
        int r = Expand(key >> KeyBits * 2), g = Expand(key >> KeyBits & (KeyLevels - 1)), b = Expand(key & (KeyLevels - 1));
        int best = 0;
        long bestFar = long.MaxValue;
        for (int i = 0; i < _used; i++)
        {
            int dr = _rgb[i * 3] - r, dg = _rgb[i * 3 + 1] - g, db = _rgb[i * 3 + 2] - b;
            long far = (long)RedWeight * dr * dr + (long)GreenWeight * dg * dg + (long)BlueWeight * db * db;
            if (far >= bestFar) continue;
            bestFar = far;
            best = i;
        }
        _nearest[key] = (byte)best;
        _known[key] = true;
        return (byte)best;
    }

    static List<(byte r, byte g, byte b)> MedianCut(long[] count, long[] sums, int most)
    {
        var keys = Enumerable.Range(0, Keys).Where(k => count[k] > 0).ToArray();
        var boxes = new List<(int start, int end)>();
        if (keys.Length > 0) boxes.Add((0, keys.Length));
        while (boxes.Count < most)
        {
            int pick = -1, channel = 0;
            long bestScore = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                var (c, range) = Widest(keys, boxes[i]);
                long score = Population(keys, count, boxes[i]) * range;
                if (score <= bestScore) continue;
                bestScore = score;
                pick = i;
                channel = c;
            }
            if (pick < 0) break;
            var (start, end) = boxes[pick];
            Array.Sort(keys, start, end - start, Comparer<int>.Create((a, b) => Level(a, channel).CompareTo(Level(b, channel))));
            int cut = Median(keys, count, start, end);
            boxes[pick] = (start, cut);
            boxes.Add((cut, end));
        }
        return boxes.Select(box => Average(keys, count, sums, box)).ToList();
    }

    static int Level(int key, int channel) => key >> (KeyBits * (2 - channel)) & (KeyLevels - 1);

    static (int channel, int range) Widest(int[] keys, (int start, int end) box)
    {
        int bestChannel = 0, bestRange = 0;
        for (int c = 0; c < 3; c++)
        {
            int lo = KeyLevels, hi = -1;
            for (int i = box.start; i < box.end; i++)
            {
                int v = Level(keys[i], c);
                lo = Math.Min(lo, v);
                hi = Math.Max(hi, v);
            }
            if (hi - lo <= bestRange) continue;
            bestRange = hi - lo;
            bestChannel = c;
        }
        return (bestChannel, bestRange);
    }

    static long Population(int[] keys, long[] count, (int start, int end) box)
    {
        long n = 0;
        for (int i = box.start; i < box.end; i++) n += count[keys[i]];
        return n;
    }

    static int Median(int[] keys, long[] count, int start, int end)
    {
        long half = Population(keys, count, (start, end)) / 2, run = 0;
        for (int i = start; i < end - 1; i++)
        {
            run += count[keys[i]];
            if (run >= half) return i + 1;
        }
        return end - 1;
    }

    static (byte r, byte g, byte b) Average(int[] keys, long[] count, long[] sums, (int start, int end) box)
    {
        long n = 0, r = 0, g = 0, b = 0;
        for (int i = box.start; i < box.end; i++)
        {
            int k = keys[i];
            n += count[k];
            r += sums[k * 3];
            g += sums[k * 3 + 1];
            b += sums[k * 3 + 2];
        }
        return ((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }
}
