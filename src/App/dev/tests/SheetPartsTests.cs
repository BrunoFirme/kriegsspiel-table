using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class SheetPartsTests
{
    const int TileSide = 256, Channels = 4;

    static byte Low(int v) => (byte)(v & 255);
    static byte High(int v) => (byte)(v >> 8);

    static bool Never() => false;

    static (TileLevel Level, string[] Paths, string Dir) Tiles(int w, int h)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ktable-sheet-parts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var lv = new TileLevel { W = w, H = h, Cols = (w + TileSide - 1) / TileSide, Rows = (h + TileSide - 1) / TileSide };
        var paths = new string[lv.Cols * lv.Rows];
        for (int row = 0; row < lv.Rows; row++)
            for (int c = 0; c < lv.Cols; c++)
            {
                int tw = Math.Min(TileSide, w - c * TileSide), th = Math.Min(TileSide, h - row * TileSide);
                var px = new byte[tw * th * Channels];
                for (int y = 0; y < th; y++)
                    for (int x = 0; x < tw; x++)
                    {
                        int i = (y * tw + x) * Channels, sx = c * TileSide + x;
                        (px[i], px[i + 1], px[i + 2], px[i + 3]) = (Low(sx), High(sx), Low(row * TileSide + y), 255);
                    }
                var path = paths[row * lv.Cols + c] = Path.Combine(dir, $"{c}_{row}.png");
                using var tile = Image.CreateFromData(tw, th, false, Image.Format.Rgba8, px);
                tile.SavePng(path);
            }
        return (lv, paths, dir);
    }

    [NodeTest]
    static Task A_picture_build_stops_at_the_next_tile_row_once_it_is_no_longer_wanted(TestRunner r)
    {
        var (lv, paths, dir) = Tiles(8300, 3 * TileSide);
        var parts = SheetPicture.PartsOf(lv);
        int asked = 0;
        var made = SheetPicture.Stitch(lv, TileSide, paths, () => asked++ >= 2);
        Check.That(made == null, "a build stopped part-way hands over nothing");
        Check.Equal(3, asked, "the build stops at the second tile row of the first part instead of running on");
        var whole = SheetPicture.Stitch(lv, TileSide, paths, Never);
        Check.That(whole != null && whole.Imgs.Length == parts.Count, "the same picture builds whole when nothing stops it");
        whole!.Dispose();
        Check.That(SheetPicture.Stitch(lv, TileSide, paths, () => true) == null, "a build no longer wanted at the start reads nothing");
        KTable.Core.BestEffort.Try(() => Directory.Delete(dir, true));
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task Each_part_holds_its_own_pixels_and_its_neighbours_edge(TestRunner r)
    {
        const int w = 8300, h = 40;
        var (lv, paths, dir) = Tiles(w, h);
        var parts = SheetPicture.PartsOf(lv);
        Check.Equal(2, parts.Count, "an 8300 px wide picture is two parts");
        Check.That(parts.Gutter > 0, "parts of a split picture overlap their neighbours");
        for (int p = 0; p < parts.Count; p++)
        {
            var made = SheetPicture.Part(lv, TileSide, paths, parts, p, false, true, Never);
            Check.That(made != null, $"part {p} is built");
            using var img = made!.Value.Img;
            Check.Equal(parts.TextureW, img.GetWidth(), $"part {p} width");
            Check.Equal(parts.TextureH, img.GetHeight(), $"part {p} height");
            var data = img.GetData();
            for (int ty = 0; ty < parts.TextureH; ty++)
                for (int tx = 0; tx < parts.TextureW; tx++)
                {
                    int sx = Math.Clamp(p * parts.W - parts.Gutter + tx, 0, w - 1), sy = Math.Clamp(ty - parts.Gutter, 0, h - 1);
                    int i = (ty * parts.TextureW + tx) * Channels;
                    if (data[i] != Low(sx) || data[i + 1] != High(sx) || data[i + 2] != sy)
                        throw new TestFailure($"part {p} texel ({tx}, {ty}) holds x {data[i] | data[i + 1] << 8}, y {data[i + 2]} instead of x {sx}, y {sy}");
                }
        }
        KTable.Core.BestEffort.Try(() => Directory.Delete(dir, true));
        return Task.CompletedTask;
    }

    static Image Decoded(Image rgba, bool alpha)
    {
        var packed = BlockCompress.Compress(rgba, alpha);
        Check.That(packed != null, "the picture packs");
        packed!.Decompress();
        packed.Convert(Image.Format.Rgba8);
        return packed;
    }

    static double Psnr(byte[] a, byte[] b, int pixels)
    {
        double sum = 0;
        for (int i = 0; i < pixels * Channels; i++)
            if (i % Channels != 3) sum += (a[i] - b[i]) * (double)(a[i] - b[i]);
        double mse = sum / (pixels * 3);
        return mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255 * 255 / mse);
    }

    static Image Picture(int side, Func<int, int, (byte R, byte G, byte B)> at)
    {
        var px = new byte[side * side * Channels];
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                int i = (y * side + x) * Channels;
                var (cr, cg, cb) = at(x, y);
                (px[i], px[i + 1], px[i + 2], px[i + 3]) = (cr, cg, cb, 255);
            }
        var img = Image.CreateFromData(side, side, false, Image.Format.Rgba8, px);
        img.GenerateMipmaps();
        return img;
    }

    [NodeTest]
    static Task Packed_map_pictures_keep_ink_exact_and_tints_close(TestRunner r)
    {
        const int side = 128;
        var rng = new Random(11);
        using var ink = Picture(side, (_, _) => rng.Next(3) == 0 ? ((byte)0, (byte)0, (byte)0) : ((byte)255, (byte)255, (byte)255));
        using var inkBack = Decoded(ink, false);
        Check.That(Psnr(ink.GetData(), inkBack.GetData(), side * side) == double.PositiveInfinity, "black ink on white paper comes back exact");
        using var wash = Picture(side, (x, y) => ((byte)(150 + x / 3 + rng.Next(6)), (byte)(170 + y / 4 + rng.Next(6)), (byte)(140 + (x + y) / 6 + rng.Next(6))));
        using var washBack = Decoded(wash, false);
        double washed = Psnr(wash.GetData(), washBack.GetData(), side * side);
        Check.That(washed > 38, $"a mottled colour wash comes back within 38 dB, not {washed:0.0}");
        using var stipple = Picture(side, (x, y) => (x + y) % 3 == 0 ? ((byte)120, (byte)170, (byte)130) : (x * 7 + y) % 5 == 0 ? ((byte)40, (byte)60, (byte)160) : ((byte)236, (byte)234, (byte)226));
        using var stippleBack = Decoded(stipple, false);
        double stippled = Psnr(stipple.GetData(), stippleBack.GetData(), side * side);
        Check.That(stippled > 22, $"green stipple beside blue lines on paper comes back within 22 dB, not {stippled:0.0}");
        return Task.CompletedTask;
    }
}
