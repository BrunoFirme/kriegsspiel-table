using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using KTable.Client.Studio;

namespace KTable.Client.Tests;

public class StudioTests
{
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string P(IEnumerable<Vector2> l) => string.Join(" ", l.Select(v => F(v.X) + "," + F(v.Y)));

    [Fact]
    public void Undo_and_redo_walk_the_recorded_states()
    {
        var h = new UndoHistory();
        h.Start("a");
        Assert.False(h.CanUndo);
        Assert.True(h.Record("b"));
        Assert.False(h.Record("b"));
        Assert.True(h.Record("c"));
        Assert.Equal("b", h.Undo());
        Assert.Equal("a", h.Undo());
        Assert.Null(h.Undo());
        Assert.True(h.CanRedo);
        Assert.Equal("b", h.Redo());
        Assert.Equal(new[] { "a", "b", "c" }, h.All);
        Assert.True(h.Record("d"));
        Assert.False(h.CanRedo);
        Assert.Null(h.Redo());
        Assert.Equal(new[] { "a", "b", "d" }, h.All);
    }

    [Fact]
    public void Undo_keeps_120_steps()
    {
        var h = new UndoHistory();
        h.Start("0");
        for (int i = 1; i <= 300; i++) h.Record(i.ToString(CultureInfo.InvariantCulture));
        var all = h.All.ToList();
        Assert.Equal(UndoHistory.Steps, all.Count);
        Assert.Equal("181", all[0]);
        Assert.Equal("300", all[^1]);
    }

    [Fact]
    public void Undo_keeps_no_more_than_24_million_characters_but_always_the_last_two()
    {
        var h = new UndoHistory();
        h.Start(new string('a', 10_000_000));
        h.Record(new string('b', 10_000_000));
        h.Record(new string('c', 10_000_000));
        Assert.Equal(new[] { 'b', 'c' }, h.All.Select(s => s[0]));
        h.Record(new string('d', 30_000_000));
        h.Record(new string('e', 30_000_000));
        Assert.Equal(new[] { 'd', 'e' }, h.All.Select(s => s[0]));
        Assert.Equal("d", h.Undo()![..1]);
        Assert.Null(h.Undo());
    }

    [Fact]
    public void Shapes_have_the_corners_they_always_had()
    {
        Assert.Equal("110,58 109.769424,60.341084 109.086555,62.5922 107.97764,64.66684 106.48528,66.48528 104.66684,67.97764 102.5922,69.086555 100.34109,69.769424 98,70 22,70 19.658916,69.769424 17.407799,69.086555 15.333158,67.97764 13.514719,66.48528 12.022364,64.66684 10.913446,62.5922 10.2305765,60.341084 10,58 10,31.999998 10.2305765,29.658916 10.913446,27.407799 12.022366,25.333157 13.514718,23.514719 15.333155,22.022366 17.407797,20.913446 19.658916,20.230577 22,20 98,20 100.34109,20.230577 102.5922,20.913446 104.66684,22.022364 106.48528,23.514717 107.97764,25.333157 109.086555,27.407799 109.769424,29.658916 110,32.000004",
            P(ArtShapes.RoundRect(new Vector2(10, 20), new Vector2(110, 70), 12)));
        Assert.Equal("50,10 60.580135,35.437695 88.04227,37.63932 67.11902,55.562305 73.51141,82.36068 50,68 26.488592,82.36068 32.88098,55.5623 11.957737,37.63932 39.41986,35.4377",
            P(ArtShapes.Star(new Vector2(50, 50), new Vector2(50, 10), 5, 0.45f)));
        Assert.Equal("-3.059797E-07,-7 6.0621777,-3.5 6.0621777,3.5 -3.059797E-07,7 -6.0621777,3.5000005 -6.0621767,-3.5000012",
            P(ArtShapes.Ngon(new Vector2(0, 0), new Vector2(0, -7), 6)));
        Assert.Equal("100,40 69.41598,43.76033 80.44628,16.18457", P(ArtShapes.ArrowHead(new Vector2(0, 0), new Vector2(100, 40), 6)));
        Assert.Equal("79.9449,31.97796", P(new[] { ArtShapes.ArrowShaftEnd(new Vector2(0, 0), new Vector2(100, 40), 6) }));
        var ellipse = ArtShapes.Ellipse(new Vector2(3, 4), new Vector2(250, 90));
        Assert.Equal(96, ellipse.Count);
        Assert.Equal("27fe96ea401c8a26", Convert.ToHexString(SHA256.HashData(ellipse.SelectMany(v => new[] { v.X, v.Y }).SelectMany(BitConverter.GetBytes).ToArray()))[..16].ToLowerInvariant());
        Assert.Equal(new[] { new Vector2(1, 2), new Vector2(3, 2), new Vector2(3, 4), new Vector2(1, 4) }, ArtShapes.Rect(new Vector2(1, 2), new Vector2(3, 4)));
        Assert.Empty(ArtShapes.ArrowHead(new Vector2(5, 5), new Vector2(5, 5), 3));
    }

    [Theory]
    [InlineData("off", 0, "300,100")]
    [InlineData("x", 0, "300,100 212,100")]
    [InlineData("y", 0, "300,100 300,156")]
    [InlineData("xy", 0, "300,100 212,100 300,156 212,156")]
    [InlineData("radial", 5, "300,100 296.22632,161.194 236.8612,176.51503 203.94528,124.78992 242.96718,77.50102")]
    public void Symmetry_copies_a_point_where_it_always_has(string mode, int radial, string expected)
    {
        var copies = ArtShapes.Symmetry(mode, radial, new Vector2(256, 128));
        Assert.Equal(Affine2.Identity, copies[0]);
        Assert.Equal(expected, P(copies.Select(t => t * new Vector2(300, 100))));
    }

    [Fact]
    public void A_custom_outline_cuts_a_soft_edge()
    {
        var cov = ArtShapes.PolyCoverage(8, 6, new List<Vector2> { new(1.5f, 0.5f), new(7, 2), new(3.25f, 5.5f) });
        Assert.Equal("0 0.19583336 0.21875 0 0 0 0 0 0 0.15 1 0.9479166 0.67708325 0.40625 0.13541675 0 0 0 0.79999995 1 1 1 0.4642856 0 0 0 0.45 1 0.9977679 0.3950894 0 0 0 0 0.107812524 0.9720982 0.34151793 0 0 0 0 0 0 0.17767859 0 0 0 0",
            string.Join(" ", cov.Select(F)));
        Assert.All(ArtShapes.PolyCoverage(4, 4, new List<Vector2> { new(0, 0), new(4, 4) }), c => Assert.Equal(0, c));
    }

    static byte[] Ring()
    {
        var d = new byte[9 * 7 * 4];
        for (int y = 0; y < 7; y++)
            for (int x = 0; x < 9; x++)
            {
                int o = (y * 9 + x) * 4;
                bool ring = x >= 2 && x <= 6 && y >= 1 && y <= 5 && (x == 2 || x == 6 || y == 1 || y == 5);
                if (ring) { d[o] = 20; d[o + 1] = 20; d[o + 2] = 20; d[o + 3] = 255; }
                else if (x == 4 && y == 3) { d[o] = 200; d[o + 1] = 10; d[o + 2] = 10; d[o + 3] = 255; }
                else if (x >= 3 && x <= 5 && y >= 2 && y <= 4) { d[o] = 250; d[o + 1] = 250; d[o + 2] = 240; d[o + 3] = 255; }
            }
        return d;
    }

    static string Mask((byte[] mask, int w, int h, int x, int y) m) =>
        $"{m.w}x{m.h} at {m.x},{m.y} " + string.Join("", Enumerable.Range(0, m.w * m.h).Select(i => m.mask[i * 4 + 3] > 0 ? '#' : '.'));

    [Fact]
    public void The_bucket_fills_the_region_of_like_colour_grown_by_a_pixel()
    {
        Assert.Equal("3x3 at 3,2 .#.###.#.", Mask(FloodFill.Region(Ring(), 9, 7, 4, 3, 0.12f)!.Value));
        Assert.Equal("9x7 at 0,0 #####################...######...######...#####################", Mask(FloodFill.Region(Ring(), 9, 7, 0, 0, 0.12f)!.Value));
        Assert.Null(FloodFill.Region(Ring(), 9, 7, 9, 0, 0.12f));
        Assert.Null(FloodFill.Region(Ring(), 9, 7, -1, 3, 0.12f));
    }

    [Fact]
    public void Tracing_follows_the_edge_of_what_is_drawn()
    {
        var (pts, cell) = ArtTrace.Outline(Ring(), 9, 7, 9);
        Assert.Equal(1f, cell);
        Assert.Equal("2.5,5.5 2.5,4.5 2.5,3.5 2.5,2.5 2.5,1.5 3.5,1.5 4.5,1.5 5.5,1.5 6.5,1.5 6.5,2.5 6.5,3.5 6.5,4.5 6.5,5.5 5.5,5.5 4.5,5.5 3.5,5.5", P(pts));
        Assert.Empty(ArtTrace.Outline(new byte[9 * 7 * 4], 9, 7, 9).pts);
    }
}
