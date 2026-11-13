using KTable.Core.Model;

namespace KTable.Core.Tests;

public class BuiltLandTests
{
    const double Side = 60, MetresToTheUnit = 50;

    static ReliefGrid Painted(Func<double, double, byte> kind, Func<double, double, bool>? sea = null)
    {
        var grid = ReliefGrid.Flat(Side, Side);
        for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Cols; x++)
            {
                double u = x / (double)(grid.Cols - 1), v = y / (double)(grid.Rows - 1);
                grid.Kind[y * grid.Cols + x] = kind(u, v);
                if (sea?.Invoke(u, v) == true) grid.Detail[y * grid.Cols + x] = GroundKinds.Levels(100, 0);
            }
        return grid;
    }

    static BuiltSite Site(ReliefGrid grid, int manner = 0, params BuiltThing[] others) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = grid, HeightUnit = Side, Others = others, Manner = manner };

    static int Count(BuiltThing t, string kind, string cls) => t.Parts.Count(p => p.Kind == kind && p.Class == cls);

    static readonly ReliefGrid River = Painted((_, v) => v is > 0.5 and < 0.53 ? GroundKinds.Water : GroundKinds.Grass);
    static readonly ReliefGrid Lake = Painted((_, v) => v > 0.5 ? GroundKinds.Water : GroundKinds.Grass);
    static readonly ReliefGrid Inland = Painted((_, _) => GroundKinds.Grass);
    static readonly Spot Beside = new(30, 26);

    static BuiltThing Village(ReliefGrid grid, int seed = 4, int manner = 0, double houses = 0.5, params BuiltThing[] others) =>
        Settlements.Lay(Site(grid, manner, others), "s", Beside, 4, seed, houses, 0, 0);

    [Fact]
    public void The_land_round_a_place_is_read_for_its_water_and_its_woods()
    {
        var river = Surroundings.Of(Site(River), Beside, 4);
        Assert.True(river.River && !river.Lake && !river.Sea && river.Banks.Count >= 8);
        Assert.All(river.Banks, b => Assert.InRange(b.Land.Y, 29, 30.1));
        Assert.All(river.Banks, b => Assert.InRange(b.WideM, 60, 170));
        var lake = Surroundings.Of(Site(Lake), Beside, 4);
        Assert.True(lake.Lake && !lake.River);
        Assert.Empty(Surroundings.Of(Site(Inland), Beside, 4).Banks);
        var sea = Surroundings.Of(Site(Painted((_, v) => v > 0.5 ? GroundKinds.Water : GroundKinds.Grass, (_, v) => v > 0.5)), Beside, 4);
        Assert.True(sea.Sea && !sea.Lake && !sea.River);
        var wooded = Surroundings.Of(Site(Painted((u, _) => u > 0.56 ? GroundKinds.Wood : GroundKinds.Grass)), Beside, 4);
        Assert.True(wooded.Woods.Count >= 8 && wooded.Wooded is > 0.1 and < 0.6);
        Assert.All(wooded.Woods, w => Assert.True(w.At.X > 33.5));
    }

    [Fact]
    public void A_village_on_a_river_fishes_it_grinds_by_it_and_ferries_across_it()
    {
        var villages = Enumerable.Range(1, 5).Select(seed => Village(River, seed)).ToList();
        Assert.All(villages, v => Assert.True(Count(v, PartKinds.Block, "fisher") >= 2, $"{Count(v, PartKinds.Block, "fisher")} huts"));
        Assert.All(villages, v => Assert.True(Count(v, PartKinds.Sign, "boat") >= 2));
        Assert.Contains(villages, v => Count(v, PartKinds.Sign, "nets") >= 1);
        Assert.Contains(villages, v => Count(v, PartKinds.Block, "mill") == 1 && Count(v, PartKinds.Sign, "wheel") == 1);
        Assert.Contains(villages, v => Count(v, PartKinds.Line, "rope") == 1 && Count(v, PartKinds.Sign, "ferryboat") == 1);
        Assert.All(villages, v => Assert.All(v.Parts.Where(p => p.Kind == PartKinds.Block), p => Assert.True(BuiltGeometry.Points(p.At).All(q => Spot.Of(q).Y < 30.05), "no building stands in the river")));
        var bigger = Enumerable.Range(1, 5).Select(seed => Settlements.Lay(Site(River), "s", new Spot(30, 24), 5.5, seed, 0.6, 0, 0)).ToList();
        Assert.Contains(bigger, v => Count(v, PartKinds.Block, "tanner") == 1 && Count(v, PartKinds.Sign, "pits") == 1);
    }

    [Fact]
    public void A_village_on_a_lake_fishes_without_mill_or_ferry_and_one_inland_does_neither()
    {
        var lake = Enumerable.Range(1, 4).Select(seed => Village(Lake, seed)).ToList();
        Assert.All(lake, v => Assert.True(Count(v, PartKinds.Block, "fisher") >= 2));
        Assert.All(lake, v => Assert.Equal((0, 0), (Count(v, PartKinds.Sign, "wheel"), Count(v, PartKinds.Line, "rope"))));
        var inland = Village(Inland);
        Assert.Equal((0, 0, 0), (Count(inland, PartKinds.Block, "fisher"), Count(inland, PartKinds.Sign, "nets"), Count(inland, PartKinds.Sign, "ferryboat")));
    }

    [Fact]
    public void A_bridge_near_by_spares_the_ferry_and_egypt_has_no_watermill()
    {
        var road = BuiltRoads.Lay(Site(River), "s", [new Spot(31, 20), new Spot(31, 40)], "road", false);
        Assert.Contains(road.Parts, p => p.Class == "bridge");
        Assert.All(Enumerable.Range(1, 5), seed => Assert.Equal(0, Count(Village(River, seed, 0, 0.5, road), PartKinds.Line, "rope")));
        Assert.All(Enumerable.Range(1, 4), seed => Assert.Equal(0, Count(Village(River, seed, Manners.Egyptian), PartKinds.Block, "mill")));
        Assert.Contains(Enumerable.Range(1, 4), seed => Count(Village(River, seed, Manners.Egyptian), PartKinds.Sign, "sweep") >= 1);
    }
}
