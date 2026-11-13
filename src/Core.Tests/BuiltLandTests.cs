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

    [Fact]
    public void A_place_laid_over_a_road_that_is_there_keeps_its_buildings_off_it()
    {
        var road = BuiltRoads.Lay(Site(Inland), "r", [new Spot(10, 27), new Spot(50, 27)], "road", false);
        var village = Village(Inland, houses: 0.9, others: road);
        var blocks = village.Parts.Where(p => p.Kind == PartKinds.Block).Select(BuiltSite.Round).ToList();
        Assert.True(blocks.Count >= 10);
        Assert.All(blocks, b => Assert.True(Math.Abs(b.At.Y - 27) > 5.25 / MetresToTheUnit, $"a building stands on the road at {b.At.X:0.0}, {b.At.Y:0.0}"));
    }

    [Fact]
    public void A_place_by_a_wood_works_it_and_one_in_open_country_does_not()
    {
        var edge = Village(Painted((u, _) => u > 0.56 ? GroundKinds.Wood : GroundKinds.Grass), houses: 0.6);
        Assert.True(Count(edge, PartKinds.Block, "hut") >= 1 && Count(edge, PartKinds.Sign, "logs") >= 1);
        Assert.Equal(1, Count(edge, PartKinds.Sign, "sawpit"));
        Assert.All(edge.Parts.Where(p => p.Kind == PartKinds.Block && p.Class == "hut"), hut => Assert.True(BuiltSite.Round(hut).At.X < 33.6));
        Assert.True(Count(edge, PartKinds.Block, "cabin") >= 1 && Count(Village(Inland, houses: 0.6), PartKinds.Block, "cabin") == 0);
        Assert.True(Count(Village(Painted((u, _) => u > 0.56 ? GroundKinds.Rock : GroundKinds.Grass), houses: 0.6), PartKinds.Block, "stonehouse") >= 1);
        var within = Village(Painted((_, _) => GroundKinds.Wood), houses: 0.6);
        Assert.True(Count(within, PartKinds.Block, "hut") >= 1);
        Assert.Equal((0, 0), (Count(Village(Inland, houses: 0.6), PartKinds.Block, "hut"), Count(Village(Inland, houses: 0.6), PartKinds.Sign, "sawpit")));
    }

    [Fact]
    public void Marsh_rock_and_a_sandy_shore_each_give_a_place_its_work()
    {
        var marsh = Village(Painted((u, _) => u > 0.56 ? GroundKinds.Marsh : GroundKinds.Grass));
        Assert.Equal((1, 1, 1), (Count(marsh, PartKinds.Sign, "peat"), Count(marsh, PartKinds.Sign, "reeds"), Count(marsh, PartKinds.Line, "ditch")));
        Assert.Equal((1, 1, 1), (Count(marsh, PartKinds.Line, "causeway"), Count(marsh, PartKinds.Sign, "windpump"), Count(marsh, PartKinds.Area, "pond") >= 1 ? 1 : 0));
        Assert.Equal((0, 0), (Count(Village(Painted((u, _) => u > 0.56 ? GroundKinds.Marsh : GroundKinds.Grass), manner: Manners.Roman), PartKinds.Sign, "windpump"), Count(Village(Inland), PartKinds.Line, "causeway")));
        var rock = Village(Painted((u, _) => u > 0.56 ? GroundKinds.Rock : GroundKinds.Grass));
        Assert.Equal((1, 1), (Count(rock, PartKinds.Area, "quarry"), Count(rock, PartKinds.Sign, "blocks")));
        Assert.Equal(0, Count(Village(Painted((u, _) => u > 0.56 ? GroundKinds.Rock : GroundKinds.Grass), manner: Manners.Tribal), PartKinds.Area, "quarry"));
        var shore = Painted((_, v) => v > 0.535 ? GroundKinds.Water : v > 0.5 ? GroundKinds.Sand : GroundKinds.Grass, (_, v) => v > 0.535);
        Assert.Equal(1, Count(Village(shore), PartKinds.Area, "saltpans"));
        var open = Village(Inland);
        Assert.Equal((0, 0, 0), (Count(open, PartKinds.Sign, "peat"), Count(open, PartKinds.Area, "quarry"), Count(open, PartKinds.Area, "saltpans")));
    }

    static ReliefGrid Sloping(Func<double, double, double> height)
    {
        var grid = Painted((_, _) => GroundKinds.Grass);
        for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Cols; x++) grid.H[y * grid.Cols + x] = (float)height(x / (double)(grid.Cols - 1), y / (double)(grid.Rows - 1));
        return grid;
    }

    [Fact]
    public void The_church_takes_the_higher_side_and_the_vineyard_the_slope_that_faces_the_sun()
    {
        foreach (double fall in new[] { 0.06, -0.06 })
        {
            var town = Settlements.Lay(Site(Sloping((u, _) => u * fall)), "s", Beside, 5, 4, 0.5, 0, 0);
            var church = BuiltSite.Round(town.Parts.First(p => p.Kind == PartKinds.Block && p.Class == "church")).At;
            var square = BuiltSite.Round(town.Parts.First(p => p.Kind == PartKinds.Area && p.Class == "square")).At;
            Assert.True((church.X - square.X) * fall > 0, $"church at {church.X:0.0} beside a square at {square.X:0.0} on ground rising {fall}");
        }
        int south = 0, north = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            var hill = Sloping((u, v) => 0.05 - 0.4 * ((u - 0.5) * (u - 0.5) + (v - 0.44) * (v - 0.44)));
            var place = Settlements.Lay(Site(hill, Manners.Southern), "s", Beside, 4, seed, 0.5, 0, 0);
            foreach (var vines in place.Parts.Where(p => p.Kind == PartKinds.Area && p.Class == "vineyard"))
            {
                if (BuiltSite.Round(vines).At.Y > Beside.Y) south++;
                else north++;
            }
        }
        Assert.True(south >= 3 && north == 0, $"{south} vineyards on the sunny side, {north} on the other");
    }

    [Fact]
    public void Earthworks_shape_the_ground_that_is_read_and_leave_the_stored_relief_alone()
    {
        const double Level = 20;
        var grid = Sloping((_, _) => Level / (Side * MetresToTheUnit));
        var works = new BuiltThing { Id = "f", Sheet = "s", Kind = "fort" };
        works.Parts.Add(BuiltSite.Line("rampart", [new Spot(20, 30), new Spot(40, 30)]));
        works.Parts.Add(BuiltSite.Line("moat", [new Spot(20, 31), new Spot(40, 31)]));
        works.Parts.Add(BuiltSite.Area("quarry", [new Spot(10, 10), new Spot(14, 10), new Spot(14, 14), new Spot(10, 14)]));
        works.Parts.Add(BuiltSite.Block("house", new Spot(50, 50), new Spot(1, 0), 0.3, 0.2));
        var shaped = Earthworks.Shaped(grid, [works], Side, Side, 1 / MetresToTheUnit, Side);
        double At(ReliefGrid g, double x, double y) => Math.Round(g.HeightAt(x / Side, y / Side) * Side * MetresToTheUnit - Level, 3);
        Assert.NotSame(grid, shaped);
        Assert.All(grid.H, h => Assert.Equal(grid.H[0], h));
        var onFlat = Earthworks.Shaped(Painted((_, _) => GroundKinds.Grass), [works], Side, Side, 1 / MetresToTheUnit, Side);
        Assert.All(onFlat.H, h => Assert.True(h >= 0));
        Assert.True(onFlat.Worked);
        Assert.Equal(121, Earthworks.Shaped(new ReliefGrid(31, 31), [works], Side, Side, 1 / MetresToTheUnit, Side).Cols);
        Assert.True(onFlat.HeightAt(12 / Side, 12 / Side) < onFlat.HeightAt(50 / Side, 50 / Side) && onFlat.HeightAt(30 / Side, 30 / Side) > onFlat.HeightAt(50 / Side, 50 / Side));
        Assert.InRange(At(shaped, 30, 30), 2, 3.7);
        Assert.InRange(At(shaped, 30, 31), -3.1, -1.5);
        Assert.InRange(At(shaped, 12, 12), -5.1, -3);
        Assert.Equal((0, 0), (At(shaped, 50, 50), At(shaped, 30, 20)));
        Assert.Same(grid, Earthworks.Shaped(grid, [Village(Inland)], Side, Side, 1 / MetresToTheUnit, Side));
        Assert.NotEqual(0, Earthworks.Key([works]));
        var moved = new BuiltThing { Id = "f", Sheet = "s", Kind = "fort" };
        moved.Parts.Add(BuiltSite.Line("rampart", [new Spot(20, 32), new Spot(40, 32)]));
        Assert.NotEqual(Earthworks.Key([works]), Earthworks.Key([moved]));
    }

    [Fact]
    public void A_wall_corner_moves_out_to_the_brow_of_a_slope_and_stays_put_on_level_ground()
    {
        var hill = Site(Sloping((u, _) => 0.003 * Math.Clamp((0.55 - u) / 0.01, 0, 1)));
        var corner = Enclosures.Brow(hill, new Spot(20, 30), new Spot(31.9, 30));
        Assert.InRange(corner.X, 32.2, 32.7);
        Assert.Equal(30, corner.Y, 6);
        Assert.Equal(new Spot(31.9, 30), Enclosures.Brow(Site(Inland), new Spot(20, 30), new Spot(31.9, 30)));
        Assert.Equal(new Spot(25, 30), Enclosures.Brow(hill, new Spot(20, 30), new Spot(25, 30)));
    }

    [Fact]
    public void Painted_water_is_dug_into_the_ground_that_is_read()
    {
        var pond = Sloping((_, _) => 20 / (Side * MetresToTheUnit));
        for (int y = 0; y < pond.Rows; y++)
            for (int x = 0; x < pond.Cols; x++)
                if (Math.Abs(x - pond.Cols / 2) < 30 && Math.Abs(y - pond.Rows / 2) < 30) pond.Kind[y * pond.Cols + x] = GroundKinds.Water;
        var dug = Earthworks.Shaped(pond, [], Side, Side, 1 / MetresToTheUnit, Side);
        double Metres(double x, double y) => dug.HeightAt(x / Side, y / Side) * Side * MetresToTheUnit;
        Assert.InRange(Metres(30, 30), 18.4, 18.8);
        Assert.Equal(20, Metres(10, 10), 3);
        Assert.All(pond.H, h => Assert.Equal(pond.H[0], h));
        var level = Painted((u, v) => Math.Abs(u - 0.5) < 0.05 && Math.Abs(v - 0.5) < 0.05 ? GroundKinds.Water : GroundKinds.Grass);
        var lifted = Earthworks.Shaped(level, [], Side, Side, 1 / MetresToTheUnit, Side);
        Assert.All(lifted.H, h => Assert.True(h >= 0));
        Assert.True(lifted.HeightAt(0.5, 0.5) < lifted.HeightAt(0.1, 0.1));
    }

    [Fact]
    public void A_fort_is_not_laid_over_the_buildings_of_a_place_that_is_there()
    {
        var village = Village(Inland, houses: 0.9);
        var homes = village.Parts.Where(p => p.Kind == PartKinds.Block).Select(BuiltSite.Round).ToList();
        var fort = Forts.Lay(Site(Inland, 0, village), "s", Beside, 2.5, 1, 5, 0, 0);
        var middle = Spot.Of(BuiltGeometry.Points(fort.Outline)[0]);
        Assert.True(fort.Parts.Count == 0 || homes.All(h => (h.At - middle).Length > 2.5), "the fort keeps clear of the houses or is not laid");
        Assert.True(Forts.Lay(Site(Inland), "s", Beside, 2.5, 1, 5, 0, 0).Parts.Count > 0);
    }

    [Fact]
    public void A_lane_leads_out_to_the_fields_a_meadow_lies_by_the_river_and_the_harbour_takes_the_bay()
    {
        var tilled = Village(Painted((u, _) => u > 0.58 ? GroundKinds.Field : GroundKinds.Grass));
        Assert.Contains(tilled.Parts, p => p.Kind == PartKinds.Line && p.Class == "track" && Spot.Of(BuiltGeometry.Points(p.At)[^1]).X > 35);
        Assert.Contains(Enumerable.Range(1, 8), seed => Count(Village(River, seed), PartKinds.Area, "meadow") == 1);
        Assert.Equal(1, Count(Village(River, manner: Manners.Egyptian), PartKinds.Sign, "steps"));
        Assert.Equal(0, Count(Village(Inland), PartKinds.Area, "meadow"));
        bool Bay(double u, double v) => v > 0.53 || Math.Abs(u - 0.42) < 0.035 && v > 0.485;
        var cove = Painted((u, v) => Bay(u, v) ? GroundKinds.Water : GroundKinds.Grass, Bay);
        var port = Settlements.Lay(Site(cove), "s", Beside, 5, 4, 0.9, 0, 0);
        var quay = port.Parts.Single(p => p.Kind == PartKinds.Line && p.Class == "quay");
        Assert.InRange(BuiltGeometry.Points(quay.At).Average(p => Spot.Of(p).X), 22.5, 28);
    }

    [Fact]
    public void A_road_that_finds_its_way_bridges_a_river_where_it_is_narrow()
    {
        bool Wet(double u, double v) => Math.Abs(v - 0.5) < (Math.Abs(u - 0.47) < 0.03 ? 0.006 : 0.035);
        var road = BuiltRoads.Lay(Site(Painted((u, v) => Wet(u, v) ? GroundKinds.Water : GroundKinds.Grass)), "s", [new Spot(31, 20), new Spot(31, 40)], "road", true);
        var bridge = road.Parts.Single(p => p.Kind == PartKinds.Sign && p.Class == "bridge");
        Assert.InRange(Spot.Of(BuiltGeometry.Points(bridge.At)[0]).X, 26, 30.2);
    }

    [Fact]
    public void Streets_stop_at_the_bank_a_river_opens_at_the_sea_and_a_road_is_raised_through_marsh()
    {
        foreach (int seed in new[] { 1, 4, 7 })
            Assert.All(Village(River, seed, houses: 0.8).Parts.Where(p => p.Kind == PartKinds.Line && p.Class is "street" or "lane" or "road"),
                street => Assert.All(BuiltGeometry.Points(street.At), p => Assert.NotEqual(GroundKinds.Water, Site(River).KindAt(Spot.Of(p)))));
        var shore = Painted((_, v) => v > 0.5 ? GroundKinds.Water : GroundKinds.Grass, (_, v) => v > 0.5);
        var river = BuiltRoads.Lay(Site(shore), "s", [new Spot(30, 10), new Spot(30, 30.4)], "river", false);
        Assert.Equal(1, Count(river, PartKinds.Area, "pond"));
        Assert.Equal(0, Count(BuiltRoads.Lay(Site(Inland), "s", [new Spot(30, 10), new Spot(30, 30.4)], "river", false), PartKinds.Area, "pond"));
        var fen = Sloping((_, _) => 20 / (Side * MetresToTheUnit));
        for (int i = 0; i < fen.Kind.Length; i++) fen.Kind[i] = i % fen.Cols > fen.Cols / 2 ? GroundKinds.Marsh : GroundKinds.Grass;
        var road = new BuiltThing { Id = "r", Sheet = "s", Kind = "road" };
        road.Parts.Add(BuiltSite.Line("road", [new Spot(20, 30), new Spot(40, 30)]));
        var raised = Earthworks.Shaped(fen, [road], Side, Side, 1 / MetresToTheUnit, Side);
        double Metres(double x, double y) => raised.HeightAt(x / Side, y / Side) * Side * MetresToTheUnit;
        Assert.Equal(20, Metres(24, 30), 2);
        Assert.InRange(Metres(36, 30), 20.4, 21.1);
        var dry = new BuiltThing { Id = "d", Sheet = "s", Kind = "road" };
        dry.Parts.Add(BuiltSite.Line("road", [new Spot(10, 30), new Spot(25, 30)]));
        Assert.Same(fen, Earthworks.Shaped(fen, [dry], Side, Side, 1 / MetresToTheUnit, Side));
    }

    static double Metres(ReliefGrid g, double x, double y) => g.HeightAt(x / Side, y / Side) * Side * MetresToTheUnit;

    static ReliefGrid Rising(double fallM) => Sloping((u, _) => (20 + fallM * (u - 0.4) / 0.2) / (Side * MetresToTheUnit));

    static ReliefGrid Ridged() => Sloping((u, _) => (40 - 30 * u + 9 * Math.Exp(-Math.Pow((u - 0.5) / 0.03, 2))) / (Side * MetresToTheUnit));

    [Fact]
    public void A_moat_on_a_slope_holds_its_water_level_or_is_left_dry()
    {
        var fort = new BuiltThing { Id = "f", Sheet = "s", Kind = "fort" };
        fort.Parts.Add(BuiltSite.Line("moat", [new Spot(26, 26), new Spot(34, 26), new Spot(34, 34), new Spot(26, 34), new Spot(26, 26)]));
        var gentle = Rising(4);
        var wet = Earthworks.Shaped(gentle, [fort], Side, Side, 1 / MetresToTheUnit, Side);
        var bed = new[] { Metres(wet, 26, 30), Metres(wet, 34, 30), Metres(wet, 30, 26), Metres(wet, 30, 34) };
        Assert.True(bed.Max() - bed.Min() < 0.6, $"the moat's bed lies level: {string.Join(", ", bed.Select(m => m.ToString("0.0")))} m");
        Assert.True(bed.Max() < Metres(gentle, 26, 30) - 1.5, $"under the lowest ground of its ring: {bed.Max():0.0} m against {Metres(gentle, 26, 30):0.0}");
        Assert.False(Earthworks.Dry(gentle, fort.Parts[0], Side, Side, 1 / MetresToTheUnit, Side));
        Assert.Same(fort, Earthworks.Seen(gentle, [fort], Side, Side, 1 / MetresToTheUnit, Side)[0]);
        var steep = Rising(40);
        Assert.True(Earthworks.Dry(steep, fort.Parts[0], Side, Side, 1 / MetresToTheUnit, Side));
        Assert.Equal(("drymoat", "moat"), (Earthworks.Seen(steep, [fort], Side, Side, 1 / MetresToTheUnit, Side)[0].Parts[0].Class, fort.Parts[0].Class));
        var pond = new BuiltThing { Id = "p", Sheet = "s", Kind = "settlement" };
        pond.Parts.Add(BuiltSite.Area("pond", [new Spot(28, 28), new Spot(32, 28), new Spot(32, 32), new Spot(28, 32)]));
        var pooled = Earthworks.Shaped(steep, [pond], Side, Side, 1 / MetresToTheUnit, Side);
        Assert.True(Math.Abs(Metres(pooled, 29.2, 30) - Metres(pooled, 30.8, 30)) < 0.8, $"a pond lies level on any slope: {Metres(pooled, 29.2, 30):0.0} and {Metres(pooled, 30.8, 30):0.0} m");
    }

    [Fact]
    public void Running_water_never_rises_along_its_course()
    {
        var ridged = Ridged();
        var river = new BuiltThing { Id = "w", Sheet = "s", Kind = "water" };
        river.Parts.Add(BuiltSite.Line("river", [new Spot(10, 30), new Spot(50, 30)]));
        var cut = Earthworks.Shaped(ridged, [river], Side, Side, 1 / MetresToTheUnit, Side);
        var built = Enumerable.Range(0, 19).Select(i => Metres(cut, 12 + i * 2, 30)).ToList();
        Assert.All(Enumerable.Range(1, built.Count - 1), i => Assert.True(built[i] <= built[i - 1] + 0.05, $"the built river's bed rises at {12 + i * 2}: {built[i - 1]:0.00} to {built[i]:0.00} m"));
        Assert.True(Metres(ridged, 30, 30) - Metres(cut, 30, 30) > 6, "it is cut through the ridge");
        var laid = BuiltRoads.Lay(Site(ridged), "s", [new Spot(50, 30), new Spot(10, 30)], "stream", false).Parts.First(p => p.Class == "stream");
        var course = BuiltGeometry.Points(laid.At).Select(Spot.Of).ToList();
        Assert.True(Metres(ridged, course[0].X, course[0].Y) > Metres(ridged, course[^1].X, course[^1].Y), "a stream drawn from its low end is laid from its high end");
        var painted = Ridged();
        for (int y = 254; y <= 258; y++)
            for (int x = 40; x < painted.Cols - 40; x++) painted.Kind[y * painted.Cols + x] = GroundKinds.Water;
        var dug = Earthworks.Shaped(painted, [], Side, Side, 1 / MetresToTheUnit, Side);
        var bed = Enumerable.Range(0, 19).Select(i => Metres(dug, 12 + i * 2, 30)).ToList();
        Assert.All(Enumerable.Range(1, bed.Count - 1), i => Assert.True(bed[i] <= bed[i - 1] + 0.05, $"the painted river's bed rises at {12 + i * 2}: {bed[i - 1]:0.00} to {bed[i]:0.00} m"));
        var tarn = Rising(30);
        for (int y = 240; y <= 272; y++)
            for (int x = 240; x <= 272; x++) tarn.Kind[y * tarn.Cols + x] = GroundKinds.Water;
        var still = Earthworks.Shaped(tarn, [], Side, Side, 1 / MetresToTheUnit, Side);
        Assert.True(Math.Abs(Metres(still, 29, 30) - Metres(still, 31, 30)) < 0.3, $"painted water with no way out lies level: {Metres(still, 29, 30):0.0} and {Metres(still, 31, 30):0.0} m");
    }

    [Fact]
    public void A_built_river_is_cut_into_the_ground_and_does_not_make_the_ground_finer()
    {
        var plain = Sloping((_, _) => 20 / (Side * MetresToTheUnit));
        var river = new BuiltThing { Id = "w", Sheet = "s", Kind = "water" };
        river.Parts.Add(BuiltSite.Line("river", [new Spot(30, 10), new Spot(30, 50)]));
        var cut = Earthworks.Shaped(plain, [river], Side, Side, 1 / MetresToTheUnit, Side);
        double Metres(ReliefGrid g, double x, double y) => g.HeightAt(x / Side, y / Side) * Side * MetresToTheUnit;
        Assert.Equal(plain.Cols, cut.Cols);
        Assert.True(Metres(cut, 30, 30) < Metres(cut, 45, 30) - 0.6, $"the bed lies under the bank: {Metres(cut, 30, 30):0.0} m against {Metres(cut, 45, 30):0.0}");
    }

    [Fact]
    public void A_road_is_cut_through_a_rise_and_banked_over_a_dip_and_a_mill_on_a_stream_has_its_dam()
    {
        var ridge = Sloping((u, _) => (20 + 6 * Math.Exp(-Math.Pow((u - 0.5) / 0.01, 2)) - 5 * Math.Exp(-Math.Pow((u - 0.6) / 0.01, 2))) / (Side * MetresToTheUnit));
        var road = new BuiltThing { Id = "r", Sheet = "s", Kind = "road" };
        road.Parts.Add(BuiltSite.Line("road", [new Spot(20, 30), new Spot(40, 30)]));
        var graded = Earthworks.Shaped(ridge, [road], Side, Side, 1 / MetresToTheUnit, Side);
        double Metres(ReliefGrid g, double x, double y) => g.HeightAt(x / Side, y / Side) * Side * MetresToTheUnit;
        Assert.True(Metres(graded, 30, 30) < Metres(ridge, 30, 30) - 1.2, $"the crest under the road is cut: {Metres(graded, 30, 30):0.0} m from {Metres(ridge, 30, 30):0.0}");
        Assert.True(Metres(graded, 36, 30) > Metres(ridge, 36, 30) + 0.8, $"the dip under the road is banked: {Metres(graded, 36, 30):0.0} m from {Metres(ridge, 36, 30):0.0}");
        Assert.Equal(Metres(ridge, 30, 20), Metres(graded, 30, 20), 2);
        var level = new BuiltThing { Id = "l", Sheet = "s", Kind = "road" };
        level.Parts.Add(BuiltSite.Line("road", [new Spot(20, 10), new Spot(40, 10)]));
        var flat = Sloping((_, _) => 20 / (Side * MetresToTheUnit));
        Assert.Same(flat, Earthworks.Shaped(flat, [level], Side, Side, 1 / MetresToTheUnit, Side));
        var brook = Painted((_, v) => v is > 0.5 and < 0.506 ? GroundKinds.Water : GroundKinds.Grass);
        Assert.Contains(Enumerable.Range(1, 6), seed => Count(Village(brook, seed), PartKinds.Sign, "wheel") == 1 && Count(Village(brook, seed), PartKinds.Line, "dam") == 1);
        Assert.Equal(0, Count(Village(River), PartKinds.Line, "dam"));
    }

    [Fact]
    public void Each_gate_of_a_walled_town_has_a_bridge_over_the_ditch()
    {
        var town = Settlements.Lay(Site(Inland), "s", Beside, 5, 4, 0.9, 0.6, 0);
        var gates = town.Parts.Where(p => p.Kind == PartKinds.Block && p.Class == "gatehouse").Select(p => BuiltSite.Round(p).At).ToList();
        var bridges = town.Parts.Where(p => p.Kind == PartKinds.Sign && p.Class == "bridge").Select(p => Spot.Of(BuiltGeometry.Points(p.At)[0])).ToList();
        Assert.True(gates.Count >= 1 && Count(town, PartKinds.Line, "ditch") == 1);
        Assert.All(gates, gate => Assert.Contains(bridges, bridge => (bridge - gate).Length < 20 / MetresToTheUnit));
        Assert.Equal(0, Count(Village(Inland), PartKinds.Sign, "bridge"));
    }

    [Fact]
    public void A_bastion_of_a_town_is_pulled_out_by_its_tip_and_the_moat_follows()
    {
        var town = Settlements.Lay(Site(Inland), "s", Beside, 5, 4, 0.9, 0.9, 0);
        var star = Forts.Of(town)!;
        var handles = Walls.Handles(town)!;
        Assert.True(star.Tips.Count >= 5 && handles.Corners.Count == star.Tips.Count && Walls.Of(town) == null);
        var to = star.Centre + Spot.Toward(star.Tips[1].Bearing) * (star.Tips[1].Radius + 2);
        var pulled = Walls.CornerMoved(Site(Inland), town, 1, to)!;
        var after = Forts.Of(pulled)!;
        Assert.Equal(star.Tips[1].Radius + 2, after.Tips[1].Radius, 2);
        Assert.Equal(star.Tips[0].Radius, after.Tips[0].Radius, 2);
        Assert.Equal((star.Tips.Count, Count(town, PartKinds.Line, "moat"), Count(town, PartKinds.Block, "house")), (after.Tips.Count, Count(pulled, PartKinds.Line, "moat"), Count(pulled, PartKinds.Block, "house")));
        Assert.Null(Forts.Of(Village(Inland)));
    }

    [Fact]
    public void A_town_on_the_sea_has_a_harbour_and_a_tribal_place_only_fishes()
    {
        var sea = Painted((_, v) => v > 0.5 ? GroundKinds.Water : GroundKinds.Grass, (_, v) => v > 0.5);
        var town = Village(sea, houses: 0.9);
        Assert.Equal((1, 1, 1), (Count(town, PartKinds.Line, "quay"), Count(town, PartKinds.Sign, "ship"), Count(town, PartKinds.Sign, "crane")));
        Assert.True(Count(town, PartKinds.Line, "jetty") >= 1 && Count(town, PartKinds.Block, "warehouse") >= 1);
        Assert.True(Count(town, PartKinds.Sign, "slip") == 1 && Count(town, PartKinds.Block, "customs") + Count(town, PartKinds.Block, "ropewalk") >= 1,
            $"slip {Count(town, PartKinds.Sign, "slip")}, customs {Count(town, PartKinds.Block, "customs")}, ropewalk {Count(town, PartKinds.Block, "ropewalk")}");
        Assert.Equal((0, 0), (Count(town, PartKinds.Sign, "wheel"), Count(town, PartKinds.Line, "rope")));
        var quay = town.Parts.Single(p => p.Kind == PartKinds.Line && p.Class == "quay");
        Assert.All(BuiltGeometry.Points(quay.At), p => Assert.InRange(Spot.Of(p).Y, 29.5, 30.6));
        var old = Village(sea, manner: Manners.Roman, houses: 0.9);
        Assert.Equal((1, 0), (Count(old, PartKinds.Sign, "ship"), Count(old, PartKinds.Sign, "crane")));
        var tribal = Village(sea, manner: Manners.Tribal, houses: 0.9);
        Assert.Equal(0, Count(tribal, PartKinds.Line, "quay"));
        Assert.True(Count(tribal, PartKinds.Block, "fisher") >= 1);
        Assert.Equal(0, Count(Village(Lake, houses: 0.9), PartKinds.Line, "quay"));
    }

    [Fact]
    public void Sea_is_water_at_a_level_painted_and_read_by_its_own_name_and_no_road_bridges_it()
    {
        Assert.Equal(GroundKinds.Water, GroundKinds.Of("sea"));
        Assert.Equal(GroundKinds.Water, GroundKinds.Of(" Sea "));
        Assert.True(GroundKinds.IsSea(GroundKinds.SeaLevels) && !GroundKinds.IsSea(0));
        Assert.Equal(GroundKinds.SeaLevels, GroundKinds.DetailFor("sea", 0));
        Assert.Equal(0, GroundKinds.DetailFor("water", 9));
        Assert.Equal(9, GroundKinds.DetailFor("wood", 9));
        Assert.Equal(("sea", "water", "grass"), (GroundKinds.Painted(GroundKinds.Water, GroundKinds.SeaLevels), GroundKinds.Painted(GroundKinds.Water, 0), GroundKinds.Painted(GroundKinds.Grass, 0)));
        var paints = GroundKinds.Paints.ToList();
        Assert.Equal(paints.IndexOf("water") + 1, paints.IndexOf("sea"));
        Assert.NotEqual(GroundKinds.PaintColor("water", null), GroundKinds.PaintColor("sea", null));
        Assert.Equal(GroundKinds.Color(GroundKinds.Water, null), GroundKinds.PaintColor("water", null));
        var grid = ReliefGrid.Flat(Side, Side);
        ReliefBrush.Dab(grid, 0.5, 0.5, 30, new Brush(BrushMode.Paint, 0, 1, GroundKinds.Water, Detail: GroundKinds.SeaLevels));
        Assert.True(Site(grid).Sea(new Spot(30, 30)) && !Site(grid).Sea(new Spot(5, 5)));
        var strait = Painted((_, v) => v is > 0.5 and < 0.53 ? GroundKinds.Water : GroundKinds.Grass, (_, v) => v is > 0.5 and < 0.53);
        Assert.DoesNotContain(BuiltRoads.Lay(Site(strait), "s", [new Spot(31, 20), new Spot(31, 40)], "road", false).Parts, p => p.Class == "bridge");
        Assert.Equal((0, 0), (Count(Village(strait), PartKinds.Sign, "wheel"), Count(Village(strait), PartKinds.Line, "rope")));
    }
}
