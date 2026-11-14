using KTable.Core.Model;

namespace KTable.Core.Tests;

public class BuiltFromPaintTests
{
    const double Side = 60, MetresToTheUnit = 50;
    const byte Grass = 1, Field = 2, Wood = 3;

    static BuiltSite Site(ReliefGrid grid) => new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = grid, HeightUnit = Side };

    static ReliefGrid Painted()
    {
        var g = ReliefGrid.Flat(Side, Side);
        Array.Fill(g.Kind, Grass);
        int Row(double share) => (int)(g.Rows * share);
        int Col(double share) => (int)(g.Cols * share);
        for (int y = 0; y < g.Rows; y++)
            for (int x = Col(0.6); x < Col(0.62); x++) g.Kind[y * g.Cols + x] = GroundKinds.Water;
        for (int y = Row(0.5) - 1; y <= Row(0.5) + 1; y++)
            for (int x = Col(0.1); x < Col(0.9); x++)
            {
                int i = y * g.Cols + x;
                g.Kind[i] = g.Kind[i] == GroundKinds.Water ? GroundKinds.Bridge : GroundKinds.Road;
                g.Detail[i] = GroundKinds.Levels(100, 0);
            }
        for (int y = 0; y < g.Rows; y++)
            for (int x = 0; x < g.Cols; x++)
            {
                double dx = x - Col(0.3), dy = y - Row(0.25);
                int i = y * g.Cols + x;
                if (dx * dx + dy * dy < 30 * 30)
                {
                    g.Kind[i] = GroundKinds.Town;
                    g.Detail[i] = GroundKinds.Levels(60, 0);
                    g.Salt[i] = 9;
                }
                if (y > Row(0.7) && x < Col(0.5))
                {
                    g.Kind[i] = Field;
                    g.Detail[i] = GroundKinds.Levels(60, 80);
                }
                if (y > Row(0.8) && x > Col(0.7)) g.Kind[i] = Wood;
            }
        return g;
    }

    [Fact]
    public void Ground_without_painted_works_needs_nothing()
    {
        var g = ReliefGrid.Flat(Side, Side);
        Array.Fill(g.Kind, Wood);
        g.Detail[5] = GroundKinds.Levels(80, 0);
        Assert.False(BuiltFromPaint.Needed(g));
        Assert.True(BuiltFromPaint.Needed(Painted()));
    }

    [Fact]
    public void Painted_roads_towns_and_farmland_become_built_things_and_leave_the_ground_clean()
    {
        var old = Painted();
        var (things, ground) = BuiltFromPaint.Convert(old, Site(old), "s");
        Assert.False(BuiltFromPaint.Needed(ground));
        Assert.DoesNotContain(ground.Kind, k => k is GroundKinds.Road or GroundKinds.Bridge or GroundKinds.Town);
        int bridged = (int)(old.Rows * 0.5) * old.Cols + (int)(old.Cols * 0.61);
        Assert.Equal(GroundKinds.Bridge, old.Kind[bridged]);
        Assert.Equal(GroundKinds.Water, ground.Kind[bridged]);
        Assert.Equal(Wood, ground.Kind[old.Kind.Length - 2]);

        var roads = things.Where(t => t.Kind == BuiltKinds.Road).ToList();
        Assert.NotEmpty(roads);
        Assert.All(roads, r => Assert.Contains(r.Parts, p => p.Class == "chaussee"));
        var along = roads.SelectMany(r => r.Parts).Where(p => p.Kind == PartKinds.Line).SelectMany(p => BuiltGeometry.Points(p.At)).ToList();
        Assert.All(along, p => Assert.InRange(p.Y / (double)BuiltGeometry.PerUnit, Side * 0.5 - 0.6, Side * 0.5 + 0.6));
        Assert.InRange(along.Min(p => p.X) / (double)BuiltGeometry.PerUnit, Side * 0.08, Side * 0.13);
        Assert.InRange(along.Max(p => p.X) / (double)BuiltGeometry.PerUnit, Side * 0.87, Side * 0.92);
        Assert.Contains(roads.SelectMany(r => r.Parts), p => p.Class == "bridge");

        var town = Assert.Single(things, t => t.Kind == BuiltKinds.Settlement);
        Assert.True(town.Parts.Count(p => p.Class == "house") > 10);
        var middle = Spot.Of(BuiltGeometry.Points(town.Outline)[0]);
        Assert.InRange(middle.X, Side * 0.28, Side * 0.32);
        Assert.InRange(middle.Y, Side * 0.23, Side * 0.27);
        Assert.Equal(0.6, town.Setting(Settlements.Houses, 0), 1);

        var country = Assert.Single(things, t => t.Kind == BuiltKinds.Details);
        Assert.Contains(country.Parts, p => p.Class == "farm");
        Assert.All(country.Parts.Where(p => p.Kind == PartKinds.Block), p => Assert.True(p.At[1] / (double)BuiltGeometry.PerUnit > Side * 0.68));
    }

    [Fact]
    public void The_same_painted_ground_converts_the_same_every_time()
    {
        var a = BuiltFromPaint.Convert(Painted(), Site(Painted()), "s");
        var b = BuiltFromPaint.Convert(Painted(), Site(Painted()), "s");
        Assert.Equal(Json.Serialize(a.Things), Json.Serialize(b.Things));
        Assert.Equal(a.Ground.Kind, b.Ground.Kind);
    }

    const double BrushCells = 1.5, WithinCells = 1.5, EndWithinCells = 3;

    static ReliefGrid Meadow()
    {
        var g = ReliefGrid.Flat(Side, Side);
        Array.Fill(g.Kind, Grass);
        return g;
    }

    static void Brushed(ReliefGrid g, params Spot[] cells) => Brushed(g, BrushCells, cells);

    static void Brushed(ReliefGrid g, double brushCells, params Spot[] cells) =>
        ReliefBrush.Stroke(g, cells.Select(c => (c.X / (g.Cols - 1), c.Y / (g.Rows - 1))).ToList(), brushCells, new Brush(BrushMode.Paint, 0, 1, GroundKinds.Road, Detail: GroundKinds.Levels(100, 0)));

    static List<List<Spot>> Traced(ReliefGrid g) =>
        BuiltFromPaint.Convert(g, Site(g), "s").Things.Where(t => t.Kind == BuiltKinds.Road).SelectMany(t => t.Parts).Where(p => p.Kind == PartKinds.Line)
            .Select(p => BuiltGeometry.Points(p.At).Select(q => Spot.Of(q) * ((g.Cols - 1) / Side)).ToList()).ToList();

    static double Off(Spot p, IReadOnlyList<Spot> line) => Enumerable.Range(0, line.Count - 1).Min(i => p.FromSegment(line[i], line[i + 1]));

    static IEnumerable<Spot> Walked(IReadOnlyList<Spot> line)
    {
        for (int i = 0; i + 1 < line.Count; i++)
            for (int step = 0, steps = (int)Math.Ceiling((line[i + 1] - line[i]).Length); step <= steps; step++)
                yield return line[i] + (line[i + 1] - line[i]) * (step / (double)Math.Max(1, steps));
    }

    static double Turn(IReadOnlyList<Spot> line, int i)
    {
        Spot into = (line[i] - line[i - 1]).Unit, onward = (line[i + 1] - line[i]).Unit;
        return Math.Abs(Math.Atan2(into.Left.Dot(onward), into.Dot(onward))) * 180 / Math.PI;
    }

    [Theory]
    [InlineData(0, BrushCells)]
    [InlineData(7, BrushCells)]
    [InlineData(18, BrushCells)]
    [InlineData(32, BrushCells)]
    [InlineData(45, BrushCells)]
    [InlineData(58, BrushCells)]
    [InlineData(73, BrushCells)]
    [InlineData(104, BrushCells)]
    [InlineData(151, BrushCells)]
    [InlineData(32, 0.75)]
    [InlineData(18, 3)]
    [InlineData(73, 5)]
    public void A_straight_painted_road_is_one_straight_line_at_any_angle_and_width(double degrees, double brushCells)
    {
        var g = Meadow();
        Spot middle = new(256, 256), reach = Spot.Toward(degrees * Math.PI / 180) * 150;
        Spot[] painted = [middle - reach, middle + reach];
        Brushed(g, brushCells, painted);
        var line = Assert.Single(Traced(g));
        Assert.Equal(2, line.Count);
        Assert.All(line, p => Assert.InRange(Off(p, painted), 0, 1));
        Assert.All(painted, end => Assert.InRange(line.Min(p => (p - end).Length), 0, EndWithinCells));
    }

    [Fact]
    public void A_painted_bend_keeps_its_corner_between_two_straight_legs()
    {
        var g = Meadow();
        Spot[] painted = [new(100, 150), new(300, 150), new(360, 380)];
        Brushed(g, painted);
        var line = Assert.Single(Traced(g));
        Assert.InRange(Off(painted[1], line), 0, WithinCells);
        Assert.All(Walked(line), p => Assert.InRange(Off(p, painted), 0, WithinCells));
        Assert.All(painted, end => Assert.InRange(line.Min(p => (p - end).Length), 0, EndWithinCells));
        Assert.InRange(line.Count, 3, 12);
        Assert.All(line.Where(p => (p - painted[1]).Length > 12), p => Assert.Contains(p, new[] { line[0], line[^1] }));
    }

    [Fact]
    public void A_painted_curve_stays_a_curve()
    {
        const double radius = 120, sharpestTurn = 25;
        var g = Meadow();
        Spot centre = new(256, 300);
        var painted = Enumerable.Range(0, 29).Select(k => centre + Spot.Toward((200 + k * 5) * Math.PI / 180) * radius).ToArray();
        Brushed(g, painted);
        var line = Assert.Single(Traced(g));
        Assert.All(Walked(line), p => Assert.InRange((p - centre).Length, radius - WithinCells, radius + WithinCells));
        Assert.All(new[] { painted[0], painted[^1] }, end => Assert.InRange(line.Min(p => (p - end).Length), 0, EndWithinCells));
        Assert.All(Enumerable.Range(1, line.Count - 2), i => Assert.InRange(Turn(line, i), 0, sharpestTurn));
    }

    [Fact]
    public void Painted_roads_that_meet_keep_their_junction()
    {
        var g = Meadow();
        Spot[] through = [new(80, 240), new(430, 290)], branch = [new(255, 265), new(300, 90)];
        Brushed(g, through);
        Brushed(g, branch);
        var lines = Traced(g);
        Assert.Equal(3, lines.Count);
        Assert.All(lines, line => Assert.Equal(2, line.Count));
        Assert.All(lines.SelectMany(Walked), p => Assert.InRange(Math.Min(Off(p, through), Off(p, branch)), 0, WithinCells));
        Assert.All(lines, line => Assert.InRange(Math.Min((line[0] - branch[0]).Length, (line[^1] - branch[0]).Length), 0, EndWithinCells));
        Assert.All(new[] { through[0], through[1], branch[1] }, end => Assert.InRange(lines.Min(line => line.Min(p => (p - end).Length)), 0, EndWithinCells));
    }

    [Fact]
    public void A_painted_road_slanting_over_a_stream_is_one_line_with_one_bridge()
    {
        var g = Meadow();
        for (int y = 0; y < g.Rows; y++)
            for (int x = 300; x < 304; x++) g.Kind[y * g.Cols + x] = GroundKinds.Water;
        Spot[] painted = [new(130, 180), new(380, 330)];
        Brushed(g, painted);
        Assert.Contains(g.Kind, k => k == GroundKinds.Bridge);
        var road = Assert.Single(BuiltFromPaint.Convert(g, Site(g), "s").Things, t => t.Kind == BuiltKinds.Road);
        Assert.Equal(2, BuiltGeometry.Points(Assert.Single(road.Parts, p => p.Kind == PartKinds.Line).At).Count);
        var bridge = Spot.Of(BuiltGeometry.Points(Assert.Single(road.Parts, p => p.Class == "bridge").At)[0]) * ((g.Cols - 1) / Side);
        Assert.InRange(bridge.X, 299, 304);
        Assert.InRange(Off(bridge, painted), 0, WithinCells);
    }
}
