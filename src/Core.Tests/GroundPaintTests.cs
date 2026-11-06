using KTable.Core.Model;

namespace KTable.Core.Tests;

public class GroundPaintTests
{
    static readonly byte Grass = GroundKinds.Of("grass")!.Value;
    static readonly byte Field = GroundKinds.Of("field")!.Value;
    static readonly byte Wood = GroundKinds.Of("wood")!.Value;

    static byte[] Kinds(int cols, int rows, byte fill)
    {
        var k = new byte[cols * rows];
        Array.Fill(k, fill);
        return k;
    }

    static void Square(byte[] kinds, int cols, int x0, int y0, int x1, int y1, byte kind)
    {
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++) kinds[y * cols + x] = kind;
    }

    static ReliefGrid Grid(int side, byte fill = GroundKinds.None)
    {
        var g = new ReliefGrid(side, side);
        Array.Fill(g.Kind, fill);
        return g;
    }

    static double Uv(ReliefGrid g, int cell) => cell / (double)(g.Cols - 1);

    [Fact]
    public void The_distance_to_a_kind_boundary_is_half_a_cell_on_it_and_grows_a_cell_per_cell_inside()
    {
        const int side = 64;
        var kinds = Kinds(side, side, Field);
        Square(kinds, side, 10, 10, 20, 20, Grass);
        var g = new ReliefGrid(side, side);
        Array.Copy(kinds, g.Kind, kinds.Length);
        var d = GroundEdges.Distances(g);
        Assert.Equal(GroundEdges.StepsPerCell / 2, d[15 * side + 10]);
        Assert.Equal(GroundEdges.StepsPerCell / 2, d[15 * side + 9]);
        Assert.Equal((int)Math.Round(3.5 * GroundEdges.StepsPerCell), d[15 * side + 13]);
        Assert.Equal(GroundEdges.Far, d[side * side - 1]);
    }

    [Fact]
    public void Bare_ground_has_no_boundaries_anywhere()
    {
        var g = new ReliefGrid(16, 16);
        Assert.All(GroundEdges.Distances(g), v => Assert.Equal(GroundEdges.Far, v));
        Assert.All(GroundEdges.Roads(g), v => Assert.Equal(GroundEdges.Far, v));
    }

    [Fact]
    public void Off_a_road_each_cell_holds_its_distance_to_the_road_and_the_fields_say_where_town_and_water_are()
    {
        const int side = 40;
        var kinds = Kinds(side, side, Field);
        Square(kinds, side, 0, 20, side, 22, GroundKinds.Road);
        Square(kinds, side, 30, 0, 34, 4, GroundKinds.Town);
        var (roads, fields) = GroundEdges.Derive(side, side, kinds);
        Assert.Equal((int)Math.Round(3.5 * GroundEdges.RoadStepsPerCell), roads[17 * side + 5]);
        Assert.True(roads[21 * side + 5] > 0, "a road cell holds how wide the road is");
        int c = GroundEdges.Channels;
        Assert.Equal((int)Math.Round(2.5 * GroundEdges.FieldStepsPerCell), fields[(2 * side + 28) * c + 1]);
        Assert.Equal(GroundEdges.Far, fields[(30 * side + 5) * c + 2]);
        Assert.Equal((int)Math.Round(0.5 * GroundEdges.TownStepsPerCell), fields[(30 * side + 5) * c + 3]);
    }

    [Fact]
    public void Deriving_a_window_gives_what_the_whole_map_gives_inside_it()
    {
        const int side = 300;
        var rng = new Random(11);
        var kinds = Kinds(side, side, Grass);
        byte[] palette = [Field, Wood, GroundKinds.Road, GroundKinds.Town, GroundKinds.Water];
        for (int n = 0; n < 60; n++)
        {
            int x = rng.Next(side - 12), y = rng.Next(side - 12);
            Square(kinds, side, x, y, x + rng.Next(2, 12), y + rng.Next(2, 12), palette[rng.Next(palette.Length)]);
        }
        var (roads, fields) = GroundEdges.Derive(side, side, kinds);
        var box = new CellBox(140, 140, 160, 160);
        var partRoads = new byte[roads.Length];
        var partFields = new byte[fields.Length];
        GroundEdges.Derive(side, side, kinds, box, partRoads, partFields);
        var inner = box.Grow(GroundEdges.Reach).Clip(side, side);
        for (int y = inner.Y0; y <= inner.Y1; y++)
            for (int x = inner.X0; x <= inner.X1; x++)
            {
                int i = y * side + x;
                Assert.Equal(roads[i], partRoads[i]);
                for (int ch = 0; ch < GroundEdges.Channels; ch++) Assert.Equal(fields[i * GroundEdges.Channels + ch], partFields[i * GroundEdges.Channels + ch]);
            }
        Assert.Equal(0, partRoads[0]);
    }

    [Theory]
    [InlineData(100, 10)]
    [InlineData(37, 5)]
    [InlineData(26, 2)]
    [InlineData(1000, 100)]
    [InlineData(0, 0)]
    [InlineData(double.NaN, 0)]
    public void Contour_intervals_are_a_round_tenth_of_the_highest_ground(double highest, double interval) =>
        Assert.Equal(interval, Contours.Interval(highest), 9);

    [Fact]
    public void Contours_are_numbered_in_the_map_s_own_unit_once_it_has_a_scale()
    {
        Assert.Null(Contours.Plan(100, 0, new Calibration()));
        var plain = Contours.Plan(100, 1, new Calibration())!;
        Assert.Equal(10, plain.Step, 9);
        Assert.False(plain.Numbered);
        var metres = Contours.Plan(2, 1, new Calibration { UnitsPerMeter = 0.01 })!;
        Assert.True(metres.Numbered);
        Assert.Equal(20, metres.Every, 9);
        Assert.Equal(0.2, metres.Step, 9);
        Assert.Equal("m", metres.Unit);
    }

    [Theory]
    [InlineData(1994, "MCMXCIV")]
    [InlineData(4, "IV")]
    [InlineData(3999, "MMMCMXCIX")]
    [InlineData(0, null)]
    [InlineData(4000, null)]
    [InlineData(2.5, null)]
    public void Roman_numerals_cover_one_to_3999_and_whole_numbers_only(double value, string? letters) =>
        Assert.Equal(letters, Roman.Of(value));

    [Fact]
    public void A_cell_box_unites_grows_and_clips_to_the_map()
    {
        var a = new CellBox(2, 3, 4, 5);
        Assert.Equal(a, a.Union(CellBox.None));
        Assert.Equal(a, CellBox.None.Union(a));
        Assert.True(CellBox.None.Empty);
        Assert.Equal(new CellBox(1, 1, 9, 9), a.Union(new CellBox(1, 1, 9, 9)));
        var grown = a.Grow(5).Clip(8, 8);
        Assert.Equal(new CellBox(0, 0, 7, 7), grown);
        Assert.Equal(8, grown.Width);
        Assert.Equal(8, grown.Height);
    }

    [Fact]
    public void A_paint_dab_lays_its_kind_only_within_its_painted_core()
    {
        var g = Grid(41);
        var box = ReliefBrush.Dab(g, 0.5, 0.5, 5, new Brush(BrushMode.Paint, 0, 1, Grass, Salt: 3, Detail: 9));
        Assert.False(box.Empty);
        Assert.Equal(Grass, g.Kind[20 * 41 + 24]);
        Assert.Equal(3, g.Salt[20 * 41 + 24]);
        Assert.Equal(9, g.Detail[20 * 41 + 24]);
        Assert.Equal(GroundKinds.None, g.Kind[20 * 41 + 25]);
        Assert.All(g.H, h => Assert.Equal(0, h));
    }

    [Fact]
    public void A_road_painted_over_water_is_a_bridge_and_a_town_leaves_roads_alone()
    {
        var g = Grid(21, GroundKinds.Water);
        ReliefBrush.Dab(g, 0.5, 0.5, 3, new Brush(BrushMode.Paint, 0, 1, GroundKinds.Road));
        Assert.Equal(GroundKinds.Bridge, g.Kind[10 * 21 + 10]);
        var town = Grid(21, GroundKinds.Road);
        ReliefBrush.Dab(town, 0.5, 0.5, 3, new Brush(BrushMode.Paint, 0, 1, GroundKinds.Town));
        Assert.Equal(GroundKinds.Road, town.Kind[10 * 21 + 10]);
    }

    [Fact]
    public void Erasing_paint_clears_what_the_paint_carried()
    {
        var g = Grid(21);
        ReliefBrush.Dab(g, 0.5, 0.5, 4, new Brush(BrushMode.Paint, 0, 1, Wood, Salt: 7, Detail: 5));
        ReliefBrush.Dab(g, 0.5, 0.5, 4, new Brush(BrushMode.Paint, 0, 1, GroundKinds.None, Salt: 7, Detail: 5));
        int i = 10 * 21 + 10;
        Assert.Equal((GroundKinds.None, (byte)0, (byte)0), (g.Kind[i], g.Salt[i], g.Detail[i]));
    }

    [Fact]
    public void A_fill_takes_the_connected_ground_of_one_kind_and_stops_at_other_kinds()
    {
        var g = Grid(20, Field);
        for (int y = 0; y < 20; y++) g.Kind[y * 20 + 10] = Wood;
        var box = ReliefBrush.Fill(g, Uv(g, 2), Uv(g, 5), Grass, 1);
        Assert.Equal(new CellBox(0, 0, 9, 19), box);
        Assert.Equal(Grass, g.Kind[5 * 20 + 9]);
        Assert.Equal(Wood, g.Kind[5 * 20 + 10]);
        Assert.Equal(Field, g.Kind[5 * 20 + 11]);
        Assert.True(ReliefBrush.Fill(g, Uv(g, 2), Uv(g, 5), Grass, 1).Empty, "filling with the kind already there changes nothing");
        Assert.True(ReliefBrush.Fill(Grid(8, Field), 0.5, 0.5, Grass, 0).Empty, "at density zero nothing is laid");
    }

    [Fact]
    public void A_shape_paints_inside_its_outline_and_a_flat_or_short_outline_paints_nothing()
    {
        var g = Grid(21);
        double a = Uv(g, 5), b = Uv(g, 15);
        var box = ReliefBrush.Shape(g, [(a, a), (b, a), (b, b), (a, b)], Grass, 1);
        Assert.Equal(new CellBox(5, 5, 15, 15), box);
        Assert.Equal(Grass, g.Kind[10 * 21 + 10]);
        Assert.Equal(GroundKinds.None, g.Kind[10 * 21 + 3]);
        Assert.True(ReliefBrush.Shape(g, [(a, a), (b, b)], Grass, 1).Empty);
        Assert.True(ReliefBrush.Shape(g, [(a, a), (b, a), (0.9, a)], Grass, 1).Empty);
    }

    [Fact]
    public void Scatter_takes_everything_at_full_density_nothing_at_none_and_about_its_share_between()
    {
        Assert.True(GroundScatter.Takes(3, 4, Grass, 1));
        Assert.False(GroundScatter.Takes(3, 4, Grass, 0));
        const int side = 128;
        int Count(double density)
        {
            int n = 0;
            for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++) if (GroundScatter.Takes(x, y, Wood, density)) n++;
            return n;
        }
        foreach (var density in new[] { 0.3, 0.7 })
            Assert.InRange(Count(density) / (double)(side * side), density - 0.1, density + 0.1);
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
                if (GroundScatter.Takes(x, y, Wood, 0.3)) Assert.True(GroundScatter.Takes(x, y, Wood, 0.7), "a thinner scatter is part of a thicker one");
    }

    [Fact]
    public void Ground_is_easiest_on_roads_and_hardest_in_water()
    {
        string[] order = ["road", "town", "grass", "wood", "sand", "snow", "marsh", "rock", "water"];
        var costs = order.Select(n => GroundRoute.GroundFactor(GroundKinds.Of(n)!.Value)).ToList();
        Assert.Equal(costs.OrderBy(c => c), costs);
        Assert.Equal(GroundRoute.GroundFactor(GroundKinds.Road), GroundRoute.GroundFactor(GroundKinds.Bridge));
    }

    [Fact]
    public void A_road_over_open_level_ground_runs_straight_from_its_first_point_to_its_last()
    {
        var g = Grid(64, Grass);
        var path = GroundRoute.Road(g, g.H, 0.01, [(Uv(g, 4), Uv(g, 10)), (Uv(g, 60), Uv(g, 10))]);
        Assert.Equal([(Uv(g, 4), Uv(g, 10)), (Uv(g, 60), Uv(g, 10))], path);
        var bent = GroundRoute.Road(g, g.H, 0.01, [(Uv(g, 4), Uv(g, 10)), (Uv(g, 60), Uv(g, 50))]);
        Assert.Equal((Uv(g, 4), Uv(g, 10)), bent[0]);
        Assert.Equal((Uv(g, 60), Uv(g, 50)), bent[^1]);
        Assert.Empty(GroundRoute.Road(g, g.H, 0.01, []));
    }

    [Fact]
    public void A_road_goes_round_a_lake_rather_than_through_it()
    {
        const int side = 64;
        var g = Grid(side, Grass);
        for (int x = 30; x < side; x++)
            for (int y = 30; y < 34; y++) g.Kind[y * side + x] = GroundKinds.Water;
        var path = GroundRoute.Road(g, g.H, 0.01, [(Uv(g, 40), Uv(g, 10)), (Uv(g, 40), Uv(g, 54))]);
        Assert.All(path, p => Assert.NotEqual(GroundKinds.Water, g.Kind[(int)Math.Round(p.v * (side - 1)) * side + (int)Math.Round(p.u * (side - 1))]));
        Assert.True(path.Min(p => p.u) < Uv(g, 30), "the road goes round the lake's open end");
    }

    static ReliefGrid Slope(int side, double perCell)
    {
        var g = Grid(side, Grass);
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++) g.H[y * side + x] = (float)(y * perCell);
        return g;
    }

    [Fact]
    public void A_river_runs_downhill_from_its_spring_to_the_low_edge()
    {
        var g = Slope(40, 0.01);
        var path = GroundRoute.River(g, g.H, (0.5, 0.5), out var pond, 0.5);
        Assert.Empty(pond);
        Assert.Equal(0, path[^1].v, 9);
        for (int k = 1; k < path.Count; k++) Assert.True(path[k].v <= path[k - 1].v + 1e-9, "it never climbs");
    }

    [Fact]
    public void A_river_ends_where_it_meets_water()
    {
        const int side = 40;
        var g = Slope(side, 0.01);
        for (int x = 0; x < side; x++) g.Kind[10 * side + x] = GroundKinds.Water;
        var path = GroundRoute.River(g, g.H, (0.5, Uv(g, 30)), out _, 0.5);
        Assert.Equal(Uv(g, 10), path[^1].v, 9);
    }

    [Fact]
    public void A_river_s_width_grows_to_three_times_over_five_kilometres()
    {
        Assert.Equal(2, GroundRoute.RiverRadius(2, 0), 9);
        Assert.Equal(4, GroundRoute.RiverRadius(2, 2500), 9);
        Assert.Equal(6, GroundRoute.RiverRadius(2, 5000), 9);
        Assert.Equal(6, GroundRoute.RiverRadius(2, 50000), 9);
    }

    [Fact]
    public void Water_on_a_slope_flows_straight_down_it_and_gathers_into_streams()
    {
        const int cols = 40, rows = 20;
        var h = new float[cols * rows];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++) h[y * cols + x] = x * 0.01f;
        var flow = GroundStreams.Flow(cols, rows, h, null, 100, 100);
        const byte west = 4;
        Assert.Equal(west, flow.Direction[10 * cols + 20]);
        Assert.True(flow.Order[10 * cols + 5] > 0, "a long way down the slope the water is a stream");
        Assert.Equal(0, flow.Order[10 * cols + 37]);
        Assert.All(flow.Current, c => Assert.Equal(GroundStreams.Still, c));
    }

    [Fact]
    public void Painted_water_takes_what_flows_in_and_does_not_flow_itself()
    {
        const int side = 20;
        var h = new float[side * side];
        var kinds = new byte[side * side];
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++) h[y * side + x] = x * 0.01f;
        for (int y = 0; y < side; y++) kinds[y * side + 3] = GroundKinds.Water;
        var flow = GroundStreams.Flow(side, side, h, kinds, 100, 100);
        Assert.Equal(GroundStreams.NoFlow, flow.Direction[5 * side + 3]);
        Assert.Equal(0, flow.Order[5 * side + 3]);
        Assert.Equal(4, flow.Direction[5 * side + 4]);
    }

    [Fact]
    public void Filling_raises_a_pit_to_where_it_would_spill_and_leaves_the_rim_alone()
    {
        const int side = 5;
        var h = Enumerable.Repeat(1f, side * side).ToArray();
        h[2 * side + 2] = 0;
        var filled = GroundStreams.Filled(h, side, side, _ => false);
        Assert.True(filled[2 * side + 2] >= 1);
        Assert.Equal(1f, filled[0]);
        Assert.All(filled, v => Assert.True(v >= 1));
    }
}
