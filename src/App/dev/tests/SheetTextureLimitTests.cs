using System.Threading.Tasks;
using KTable.Core.Model;

namespace KTable.App;

static class SheetTextureLimitTests
{
    static TileIndex Pyramid(int w, int h)
    {
        const int ts = 256;
        var index = new TileIndex { TileSize = ts, SourceW = w, SourceH = h };
        for (int level = 0; ; level++)
        {
            index.Levels.Add(new TileLevel { Level = level, W = w, H = h, Cols = (w + ts - 1) / ts, Rows = (h + ts - 1) / ts });
            if (w <= ts && h <= ts) return index;
            w = System.Math.Max(1, w / 2);
            h = System.Math.Max(1, h / 2);
        }
    }

    static void Covers(int w, int h, int across, int down, string what)
    {
        var parts = SheetPicture.PartsOf(new TileLevel { W = w, H = h });
        Check.Equal(across, parts.Across, $"{what}: parts across");
        Check.Equal(down, parts.Down, $"{what}: parts down");
        Check.That(parts.W <= SheetPicture.MostPartSide && parts.H <= SheetPicture.MostPartSide, $"{what}: a part of {parts.W} × {parts.H} fits one texture");
        Check.Equal(parts.Count > 1 ? SheetPicture.PartGutter : 0, parts.Gutter, $"{what}: overlap with neighbouring parts");
        Check.That(parts.W * parts.Across >= w && parts.H * parts.Down >= h, $"{what}: the parts cover the whole picture");
        Check.That(parts.W * (parts.Across - 1) < w && parts.H * (parts.Down - 1) < h, $"{what}: no part lies wholly outside the picture");
        Check.That(parts.Across == 1 ? parts.W == w : parts.W % SheetPicture.PartAlign == 0, $"{what}: part width {parts.W} keeps neighbouring parts' mipmaps in step");
        Check.That(parts.Down == 1 ? parts.H == h : parts.H % SheetPicture.PartAlign == 0, $"{what}: part height {parts.H} keeps neighbouring parts' mipmaps in step");
    }

    [NodeTest]
    static Task A_map_too_big_for_one_texture_is_drawn_whole_from_parts(TestRunner r)
    {
        Covers(24400, 17200, 3, 3, "a 24400 × 17200 scan");
        Covers(16385, 200, 3, 1, "a map one pixel wider than two parts");
        Covers(8192, 8192, 1, 1, "a map exactly one part");
        Covers(8193, 8192, 2, 1, "a map one pixel wider than a part");
        Covers(6144, 3398, 1, 1, "an ordinary map");
        Covers(65000, 1000, 8, 1, "a long strip");
        var big = Pyramid(24400, 17200);
        Check.That(SheetPicture.BytesAt(big, 0, false) > 3 * SheetPicture.BytesAt(big, 1, false), "memory planning counts the full-size level at its own size");
        return Task.CompletedTask;
    }
}
