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
}
