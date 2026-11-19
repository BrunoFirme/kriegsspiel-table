using KTable.Client.Studio;
using KTable.Core.Model;

namespace KTable.Client.Tests;

public class FloodOutlineTests
{
    static byte[] Mask(int w, int h, Func<int, int, bool> on)
    {
        var m = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (on(x, y)) m[(y * w + x) * 4 + 3] = 255;
        return m;
    }

    [Fact]
    public void A_block_of_pixels_is_outlined_by_its_four_corners()
    {
        var edge = Assert.Single(FloodFill.Loops(Mask(10, 8, (x, y) => x is >= 2 and <= 6 && y is >= 1 and <= 4), 10, 8, 1));
        Assert.Equal(new[] { (2, 1), (7, 1), (7, 5), (2, 5) }, edge);
    }

    [Fact]
    public void A_ring_keeps_a_hole_as_large_as_asked_and_washes_a_smaller_one_over()
    {
        var ring = Mask(9, 9, (x, y) => x is >= 1 and <= 7 && y is >= 1 and <= 7 && !(x is >= 3 and <= 5 && y is >= 3 and <= 5));
        var kept = FloodFill.Loops(ring, 9, 9, 9);
        Assert.Equal(2, kept.Count);
        Assert.Equal(new[] { (1, 1), (8, 1), (8, 8), (1, 8) }, kept[0]);
        Assert.Equal(4, kept[1].Count);
        Assert.Equal((3, 3, 6, 6), (kept[1].Min(p => p.X), kept[1].Min(p => p.Y), kept[1].Max(p => p.X), kept[1].Max(p => p.Y)));
        Assert.Single(FloodFill.Loops(ring, 9, 9, 10));
    }

    [Fact]
    public void A_keyholed_ring_is_inside_round_its_holes_and_outside_in_them()
    {
        var outer = new List<Vec2> { new(0, 0), new(10, 0), new(10, 10), new(0, 10) };
        var hole = new List<Vec2> { new(3, 3), new(3, 6), new(6, 6), new(6, 3) };
        var low = new List<Vec2> { new(7, 1), new(7, 2), new(8, 2), new(8, 1) };
        var ring = Polyline.Keyholed(new() { outer, hole, low });
        Assert.Equal(4 + 5 + 2 + 5 + 2, ring.Count);
        var looked = new[] { (new Vec2(1, 1), true), (new Vec2(4.5, 4.5), false), (new Vec2(7.5, 1.5), false), (new Vec2(8, 8), true), (new Vec2(12, 5), false), (new Vec2(5, 8), true) };
        foreach (var (p, inside) in looked) Assert.Equal(inside, Polyline.Inside(ring, p));
    }

    static double Area(List<Vec2> tris)
    {
        double sum = 0;
        for (int i = 0; i + 2 < tris.Count; i += 3)
            sum += Math.Abs((tris[i + 1].X - tris[i].X) * (tris[i + 2].Z - tris[i].Z) - (tris[i + 2].X - tris[i].X) * (tris[i + 1].Z - tris[i].Z)) / 2;
        return sum;
    }

    [Fact]
    public void Ruled_lines_flooded_with_their_squares_left_as_holes_are_banded_into_exactly_their_area()
    {
        int side = 41;
        var lines = Mask(side, side, (x, y) => x % 8 == 0 || y % 8 == 0);
        var loops = FloodFill.Loops(lines, side, side, 1);
        Assert.Equal(1 + 25, loops.Count);
        var ring = Polyline.Keyholed(loops.Select(l => l.Select(p => new Vec2(p.X, p.Y)).ToList()).ToList());
        int solid = Enumerable.Range(0, side * side).Count(i => lines[i * 4 + 3] > 0);
        Assert.Equal(solid, Area(Polyline.Slabs(ring)), 6);
        Assert.Equal(90, Area(Polyline.Slabs(Polyline.Keyholed(new()
        {
            new() { new(0, 0), new(10, 0), new(10, 10), new(0, 10) },
            new() { new(3, 3), new(3, 6), new(6, 6), new(6, 3) },
            new() { new(7, 1), new(7, 2), new(8, 2), new(8, 1) },
        }))), 6);
    }

    [Fact]
    public void An_l_shape_keeps_its_six_corners_and_an_empty_mask_has_none()
    {
        var edge = Assert.Single(FloodFill.Loops(Mask(6, 6, (x, y) => x <= 1 && y <= 4 || y is >= 3 and <= 4 && x <= 4), 6, 6, 1));
        Assert.Equal(6, edge.Count);
        Assert.Contains((5, 3), edge);
        Assert.Empty(FloodFill.Loops(Mask(4, 4, (_, _) => false), 4, 4, 1));
    }

    [Fact]
    public void A_wall_stops_the_flood_where_the_colours_would_let_it_through()
    {
        int w = 12, h = 6;
        var plain = new byte[w * h * 4];
        var wall = new bool[w * h];
        for (int y = 0; y < h; y++) wall[y * w + 6] = true;
        var (mask, mw, _, x, _) = FloodFill.Region(plain, w, h, 2, 2, 0.1f, wall)!.Value;
        Assert.Equal(0, x);
        Assert.Equal(7, mw);
        Assert.Null(FloodFill.Region(plain, w, h, 6, 2, 0.1f, wall));
        Assert.Equal(12, FloodFill.Region(plain, w, h, 2, 2, 0.1f)!.Value.w);
    }

    [Fact]
    public void A_drawn_circle_holds_the_flood_inside_it_and_the_outside_runs_to_the_edge()
    {
        int w = 60, h = 60;
        var circle = new Stroke { Color = "#202020", Width = 1, Points = Enumerable.Range(0, 64).Select(i => new Vec2(30 + 20 * Math.Cos(i * Math.Tau / 64), 30 + 20 * Math.Sin(i * Math.Tau / 64))).ToList() };
        circle.Points.Add(circle.Points[0]);
        var ink = InkRaster.Paint(new[] { circle }, w, h, p => (p.X, p.Z), 1);
        var inside = FloodFill.Region(ink, w, h, 30, 30, 0.12f)!.Value;
        Assert.InRange(inside.w, 38, 43);
        Assert.InRange(inside.h, 38, 43);
        var outside = FloodFill.Region(ink, w, h, 2, 2, 0.12f)!.Value;
        Assert.Equal((60, 60), (outside.w, outside.h));
    }

    [Fact]
    public void A_filled_area_on_the_layer_is_ink_the_flood_can_take_or_stop_at()
    {
        int w = 40, h = 40;
        var area = new Stroke { Color = "#3060a0", Width = 0, Fill = true, Points = { new(10, 10), new(30, 10), new(30, 30), new(10, 30) } };
        var ink = InkRaster.Paint(new[] { area }, w, h, p => (p.X, p.Z), 1);
        var on = FloodFill.Region(ink, w, h, 20, 20, 0.12f)!.Value;
        Assert.InRange(on.w, 20, 23);
        var off = FloodFill.Region(ink, w, h, 2, 2, 0.12f)!.Value;
        Assert.Equal(40, off.w);
        var edge = FloodFill.Loops(on.mask, on.w, on.h, 1)[0];
        Assert.Equal((0, 0, on.w, on.h), (edge.Min(p => p.X), edge.Min(p => p.Y), edge.Max(p => p.X), edge.Max(p => p.Y)));
    }
}
