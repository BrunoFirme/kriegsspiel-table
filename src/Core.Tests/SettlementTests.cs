using KTable.Core.Model;

namespace KTable.Core.Tests;

public class SettlementTests
{
    const double Side = 60, MetresToTheUnit = 50;

    static BuiltSite Site(ReliefGrid? grid = null, params BuiltThing[] others) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = grid, HeightUnit = Side, Others = others };

    static ReliefGrid WaterEastOf(double share)
    {
        var grid = ReliefGrid.Flat(Side, Side);
        for (int y = 0; y < grid.Rows; y++)
            for (int x = (int)(grid.Cols * share); x < grid.Cols; x++) grid.Kind[y * grid.Cols + x] = GroundKinds.Water;
        return grid;
    }

    static int Count(BuiltThing t, string kind, string cls) => t.Parts.Count(p => p.Kind == kind && p.Class == cls);

    static Spot Middle(BuiltPart part)
    {
        var points = BuiltGeometry.Points(part.At);
        return new Spot(points.Average(p => p.X), points.Average(p => p.Y)) * (1.0 / BuiltGeometry.PerUnit);
    }

    [Fact]
    public void The_same_seed_on_the_same_ground_lays_the_same_place_and_another_seed_another()
    {
        var a = Settlements.Lay(Site(), "s", new Spot(30, 30), 4, 7, 0.5, 0, 0);
        var b = Settlements.Lay(Site(), "s", new Spot(30, 30), 4, 7, 0.5, 0, 0);
        var c = Settlements.Lay(Site(), "s", new Spot(30, 30), 4, 8, 0.5, 0, 0);
        Assert.Equal(Json.Serialize(a), Json.Serialize(b));
        Assert.NotEqual(Json.Serialize(a.Parts), Json.Serialize(c.Parts));
        Assert.True(Count(a, PartKinds.Block, "house") > 20, "a village of 200 m has houses");
        Assert.Equal(1, Count(a, PartKinds.Block, "church"));
        Assert.Equal(1, Count(a, PartKinds.Area, "square"));
    }

    [Fact]
    public void Packed_houses_outnumber_scattered_ones_and_a_hamlet_has_no_church()
    {
        int scattered = Count(Settlements.Lay(Site(), "s", new Spot(30, 30), 4, 3, 0.1, 0, 0), PartKinds.Block, "house");
        int packed = Count(Settlements.Lay(Site(), "s", new Spot(30, 30), 4, 3, 0.95, 0, 0), PartKinds.Block, "house");
        Assert.True(packed > scattered * 2, $"{packed} packed against {scattered} scattered");
        var hamlet = Settlements.Lay(Site(), "s", new Spot(30, 30), 1.5, 3, 0.3, 0, 0);
        Assert.Equal(0, Count(hamlet, PartKinds.Block, "church"));
        Assert.True(Count(hamlet, PartKinds.Block, "house") > 2);
    }

    [Fact]
    public void Nothing_is_built_on_water()
    {
        var site = Site(WaterEastOf(0.5));
        var town = Settlements.Lay(site, "s", new Spot(30, 30), 6, 5, 0.8, 0, 0);
        Assert.True(Count(town, PartKinds.Block, "house") > 20);
        Assert.All(town.Parts.Where(p => p.Kind == PartKinds.Block), p => Assert.False(site.Wet(Middle(p)), $"a {p.Class} stands in the water"));
    }

    [Fact]
    public void A_road_through_the_place_is_its_main_street()
    {
        var road = new BuiltThing { Kind = BuiltKinds.Road, Parts = { BuiltSite.Line("road", [new Spot(10, 30), new Spot(50, 30)]) } };
        var village = Settlements.Lay(Site(null, road), "s", new Spot(30, 30), 3, 9, 0.6, 0, 0);
        Assert.Equal(0, Count(village, PartKinds.Line, "street"));
        var along = village.Parts.Where(p => p.Class == "house").Select(Middle).Count(m => Math.Abs(m.Y - 30) < 0.6);
        Assert.True(along >= 6, $"{along} houses front the road");
    }

    [Fact]
    public void Walls_come_as_a_towered_ring_and_then_as_a_bastioned_trace_with_its_moat()
    {
        var open = Settlements.Lay(Site(), "s", new Spot(30, 30), 6, 4, 0.7, 0, 0);
        var walled = Settlements.Lay(Site(), "s", new Spot(30, 30), 6, 4, 0.7, 0.5, 0);
        var bastioned = Settlements.Lay(Site(), "s", new Spot(30, 30), 6, 4, 0.7, 1, 1);
        Assert.Equal(0, Count(open, PartKinds.Line, "wall") + Count(open, PartKinds.Line, "rampart"));
        Assert.True(Count(walled, PartKinds.Line, "wall") >= 1 && Count(walled, PartKinds.Sign, "tower") >= 4);
        Assert.True(Count(bastioned, PartKinds.Line, "rampart") > 1, "a rampart and its ravelins");
        Assert.Equal(1, Count(bastioned, PartKinds.Line, "moat"));
        Assert.True(Count(bastioned, PartKinds.Line, "street") > 6, "a gridded plan lays streets both ways");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    public void A_fort_has_the_bastions_asked_for(int bastions)
    {
        var trace = Forts.Trace(new Spot(30, 30), 4, bastions, 0);
        Assert.Equal(bastions * 5, trace.Count);
        Assert.Equal(bastions, trace.Count(p => Math.Abs((p - new Spot(30, 30)).Length - 4) < 1e-9));
        var fort = Forts.Lay(Site(), "s", new Spot(30, 30), 4, 2, bastions, 1, 0);
        Assert.Equal(bastions + 1, Count(fort, PartKinds.Line, "rampart"));
        Assert.Equal(bastions, Count(fort, PartKinds.Block, "barracks"));
        Assert.Equal(1, Count(fort, PartKinds.Sign, "bridge"));
    }

    [Fact]
    public void A_redoubt_is_a_plain_square_work()
    {
        var redoubt = Forts.Lay(Site(), "s", new Spot(30, 30), 2, 2, 0, 0, 0);
        var rampart = Assert.Single(redoubt.Parts, p => p.Class == "rampart");
        Assert.Equal(5 * 2, rampart.At.Count);
        Assert.Equal(0, Count(redoubt, PartKinds.Line, "glacis"));
    }
}
