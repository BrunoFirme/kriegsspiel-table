using KTable.Client.Gif;

namespace KTable.Client.Tests;

public class GifWriterTests
{
    const int Tolerance = 12, DitheredTolerance = 24;

    [Fact]
    public void The_lzw_stream_reads_back_to_the_same_indices_across_table_resets()
    {
        var rng = new Random(7);
        foreach (int length in new[] { 0, 1, 2, 257, 4096, 60_000, 250_000 })
        {
            foreach (int spread in new[] { 2, 16, 256 })
            {
                var indices = new byte[length];
                for (int i = 0; i < length; i++) indices[i] = (byte)rng.Next(spread);
                using var ms = new MemoryStream();
                GifLzw.Write(ms, indices);
                ms.Position = 0;
                Assert.Equal(GifLzw.MinCodeSize, ms.ReadByte());
                Assert.Equal(indices, Decode(Blocks(ms), GifLzw.MinCodeSize));
            }
        }
    }

    [Fact]
    public void A_still_frame_reads_back_close_to_the_picture()
    {
        const int W = 72, H = 48;
        var frame = Tiles(W, H, 0);
        var gif = Write(W, H, (frame, 0.0));
        var read = Read(gif);
        Assert.Equal((W, H), (read.Width, read.Height));
        Assert.Single(read.Frames);
        AssertClose(frame, read.Frames[0].Rgba);
    }

    [Fact]
    public void A_frame_that_changes_a_corner_stores_only_the_corner_and_shows_the_whole_picture()
    {
        const int W = 80, H = 60;
        var first = Tiles(W, H, 0);
        var second = (byte[])first.Clone();
        for (int y = 40; y < 52; y++)
            for (int x = 60; x < 75; x++) Paint(second, W, x, y, (255, 170, 60));
        var gif = Write(W, H, (first, 0.0), (second, 0.1));
        var read = Read(gif);
        Assert.Equal(2, read.Frames.Count);
        Assert.Equal((60, 40, 15, 12), read.Frames[1].Box);
        AssertClose(second, read.Frames[1].Rgba);
        Assert.Equal(10, read.Frames[0].Delay);
    }

    [Fact]
    public void An_unchanged_frame_adds_nothing_and_the_shown_frame_lasts_longer()
    {
        const int W = 32, H = 32;
        var still = Tiles(W, H, 0);
        var moved = Tiles(W, H, 1);
        var gif = Write(W, H, (still, 0.0), (still, 0.1), (still, 0.2), (moved, 0.3), (moved, 0.4));
        var read = Read(gif);
        Assert.Equal(2, read.Frames.Count);
        Assert.Equal(30, read.Frames[0].Delay);
        Assert.Equal(20, read.Frames[1].Delay);
    }

    [Fact]
    public void A_colour_that_appears_only_later_still_has_a_place_in_the_palette()
    {
        const int W = 40, H = 40;
        var green = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++) Paint(green, W, x, y, ((byte)(40 + x), (byte)(110 + y), 50));
        var fire = (byte[])green.Clone();
        for (int y = 10; y < 20; y++)
            for (int x = 10; x < 20; x++) Paint(fire, W, x, y, (255, 214, 120));
        var read = Read(Write(W, H, (green, 0.0), (fire, 0.1)));
        var shown = read.Frames[1].Rgba;
        int p = (15 * W + 15) * 4;
        Assert.InRange(shown[p], 255 - Tolerance, 255);
        Assert.InRange(shown[p + 1], 214 - Tolerance, 214 + Tolerance);
        Assert.InRange(shown[p + 2], 120 - Tolerance, 120 + Tolerance);
    }

    [Fact]
    public void A_frame_of_the_wrong_size_is_refused()
    {
        var gif = new GifWriter(new MemoryStream(), 10, 10);
        Assert.Throws<ArgumentException>(() => gif.Add(new byte[10 * 9 * 4], 0));
    }

    static byte[] Tiles(int w, int h, int offset)
    {
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int tile = (x / 8 + y / 8 * 9 + offset) % 216;
                Paint(rgba, w, x, y, ((byte)(tile % 6 * 51), (byte)(tile / 6 % 6 * 51), (byte)(tile / 36 * 51)));
            }
        return rgba;
    }

    static void Paint(byte[] rgba, int w, int x, int y, (byte r, byte g, byte b) c)
    {
        int p = (y * w + x) * 4;
        rgba[p] = c.r;
        rgba[p + 1] = c.g;
        rgba[p + 2] = c.b;
        rgba[p + 3] = byte.MaxValue;
    }

    static void AssertClose(byte[] want, byte[] got)
    {
        Assert.Equal(want.Length, got.Length);
        int worst = 0;
        for (int i = 0; i < want.Length; i++)
            if (i % 4 != 3) worst = Math.Max(worst, Math.Abs(want[i] - got[i]));
        Assert.InRange(worst, 0, DitheredTolerance);
    }

    static byte[] Write(int w, int h, params (byte[] rgba, double at)[] frames)
    {
        using var ms = new MemoryStream();
        var gif = new GifWriter(ms, w, h);
        foreach (var (rgba, at) in frames) gif.Add(rgba, at);
        gif.Finish(frames[^1].at + 0.1);
        return ms.ToArray();
    }

    sealed record Frame((int x, int y, int w, int h) Box, int Delay, byte[] Rgba);

    sealed record Picture(int Width, int Height, List<Frame> Frames);

    static Picture Read(byte[] gif)
    {
        var s = new MemoryStream(gif);
        var head = new byte[6];
        s.ReadExactly(head);
        Assert.Equal("GIF89a"u8.ToArray(), head);
        int w = Short(s), h = Short(s), packed = s.ReadByte();
        s.ReadByte();
        s.ReadByte();
        var palette = new byte[3 << ((packed & 7) + 1)];
        s.ReadExactly(palette);
        var canvas = new byte[w * h * 4];
        var frames = new List<Frame>();
        int delay = 0, clear = -1;
        while (true)
        {
            int b = s.ReadByte();
            if (b == 0x3B) break;
            if (b == 0x21)
            {
                int label = s.ReadByte();
                var body = Blocks(s);
                if (label != 0xF9) continue;
                delay = body[1] | body[2] << 8;
                clear = (body[0] & 1) != 0 ? body[3] : -1;
                continue;
            }
            Assert.Equal(0x2C, b);
            int x = Short(s), y = Short(s), fw = Short(s), fh = Short(s);
            s.ReadByte();
            int min = s.ReadByte();
            var indices = Decode(Blocks(s), min);
            Assert.Equal(fw * fh, indices.Length);
            for (int i = 0; i < indices.Length; i++)
            {
                if (indices[i] == clear) continue;
                int p = ((y + i / fw) * w + x + i % fw) * 4;
                canvas[p] = palette[indices[i] * 3];
                canvas[p + 1] = palette[indices[i] * 3 + 1];
                canvas[p + 2] = palette[indices[i] * 3 + 2];
                canvas[p + 3] = byte.MaxValue;
            }
            frames.Add(new Frame((x, y, fw, fh), delay, (byte[])canvas.Clone()));
        }
        return new Picture(w, h, frames);
    }

    static int Short(Stream s) => s.ReadByte() | s.ReadByte() << 8;

    static byte[] Blocks(Stream s)
    {
        var all = new List<byte>();
        for (int n = s.ReadByte(); n > 0; n = s.ReadByte())
        {
            var block = new byte[n];
            s.ReadExactly(block);
            all.AddRange(block);
        }
        return all.ToArray();
    }

    static byte[] Decode(byte[] data, int min)
    {
        int clearCode = 1 << min, end = clearCode + 1;
        var table = new List<byte[]>();
        void Reset()
        {
            table.Clear();
            for (int i = 0; i < clearCode; i++) table.Add(new[] { (byte)i });
            table.Add(Array.Empty<byte>());
            table.Add(Array.Empty<byte>());
        }
        Reset();
        var output = new List<byte>();
        int size = min + 1, bit = 0;
        byte[]? previous = null;
        while (true)
        {
            int code = 0;
            for (int i = 0; i < size; i++, bit++) code |= (data[bit >> 3] >> (bit & 7) & 1) << i;
            if (code == clearCode)
            {
                Reset();
                size = min + 1;
                previous = null;
                continue;
            }
            if (code == end) break;
            byte[] entry = code < table.Count ? table[code] : previous!.Append(previous![0]).ToArray();
            output.AddRange(entry);
            if (previous != null && table.Count < 4096) table.Add(previous.Append(entry[0]).ToArray());
            previous = entry;
            if (table.Count == 1 << size && size < 12) size++;
        }
        return output.ToArray();
    }
}
