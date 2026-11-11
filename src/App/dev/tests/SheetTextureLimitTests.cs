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

    [NodeTest]
    static Task A_map_too_big_for_one_texture_shows_its_finest_level_that_fits(TestRunner r)
    {
        Check.Equal(1, SheetPicture.FinestOf(Pyramid(24400, 17200)), "a 24400 × 17200 map");
        Check.Equal(1, SheetPicture.FinestOf(Pyramid(20000, 14000)), "a map under 16384 a side but over the pixel limit");
        Check.Equal(1, SheetPicture.FinestOf(Pyramid(16385, 200)), "a map one pixel wider than a texture");
        Check.Equal(0, SheetPicture.FinestOf(Pyramid(16384, 16384)), "a map exactly at the limits");
        Check.Equal(0, SheetPicture.FinestOf(Pyramid(6144, 3398)), "an ordinary map");
        var big = Pyramid(24400, 17200);
        Check.Equal(SheetPicture.BytesAt(big, 1, false), SheetPicture.BytesAt(big, 0, false), "memory planning counts the level that is shown");
        return Task.CompletedTask;
    }
}
