using KTable.Core.Model;

namespace KTable.Core.Tests;

public class TravelTests
{
    const double Side = 60, MetresPerUnit = 50, Close = 1e-6;
    static readonly Traveller Foot = Travellers.Default(Manners.Later)[Travellers.Foot];
    static readonly Traveller Cart = Travellers.Default(Manners.Later)[Travellers.Cart];

    static GameState Table(params BuiltThing[] built)
    {
        var g = new GameState();
        g.Sheets.Add(new SheetDef { Id = "s", Pos = new Vec2(5, -3), RotDeg = 30, Size = new Size2(Side, Side) });
        g.Calibration.UnitsPerMeter = 1 / MetresPerUnit;
        foreach (var thing in built)
        {
            thing.Sheet = "s";
            g.Built.Add(thing);
        }
        return g;
    }

    static BuiltSite Site(ReliefGrid? grid) => new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresPerUnit, Grid = grid, HeightUnit = Side };

    static Vec2 At(GameState g, double x, double y) => Ground.FromUv(g.Sheets[0], x / Side, y / Side);

    static Spot Back(GameState g, Vec2 world)
    {
        var (u, v) = Ground.UvOf(g.Sheets[0], world);
        return new Spot(u * Side, v * Side);
    }

    static ReliefGrid Laid(Func<double, double, double>? metres = null, Func<double, double, byte>? kind = null)
    {
        var grid = ReliefGrid.Flat(Side, Side);
        for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Cols; x++)
            {
                double px = x / (double)(grid.Cols - 1) * Side, py = y / (double)(grid.Rows - 1) * Side;
                grid.H[y * grid.Cols + x] = (float)((metres?.Invoke(px, py) ?? 0) / MetresPerUnit / Side);
                grid.Kind[y * grid.Cols + x] = kind?.Invoke(px, py) ?? GroundKinds.Grass;
            }
        return grid;
    }

    static byte River(double x, double y) => x is >= 30 and <= 31.8 ? GroundKinds.Water : GroundKinds.Grass;

    static Route Go(GameState g, ReliefGrid? grid, Spot from, Spot to, Traveller who) =>
        TravelMap.For(g, "s", grid)!.Find(At(g, from.X, from.Y), At(g, to.X, to.Y), who) ?? throw new Xunit.Sdk.XunitException("no route");

    static double OpenSeconds(Spot from, Spot to, Traveller who) => (to - from).Length * MetresPerUnit / who.Open;

    static List<Spot> Path(GameState g, Route r) => r.Points.Select(p => Back(g, p)).ToList();

    [Fact]
    public void A_plain_picture_sheet_gives_no_answer()
    {
        var house = new BuiltThing { Kind = BuiltKinds.Building, Parts = { BuiltSite.Block("house", new Spot(30, 30), new Spot(1, 0), 0.3, 0.2) } };
        Assert.Null(TravelMap.For(Table(), "s", null));
        Assert.Null(TravelMap.For(Table(house), "s", null));
        var unscaled = Table();
        unscaled.Calibration.UnitsPerMeter = null;
        Assert.Null(TravelMap.For(unscaled, "s", Laid()));
        Assert.Null(TravelMap.For(Table(), "elsewhere", Laid()));
        Assert.NotNull(TravelMap.For(Table(), "s", Laid()));
    }

    [Fact]
    public void Across_laid_ground_with_no_road_the_route_runs_straight_at_the_cross_country_pace()
    {
        var g = Table();
        Spot from = new(10.3, 10.1), to = new(50.2, 40.4);
        var route = Go(g, Laid(), from, to, Foot);
        double straight = OpenSeconds(from, to, Foot);
        Assert.InRange(route.Total, straight, straight * 1.02);
        Assert.True(route.Points.Count <= 6, $"the route keeps to a straight line, {route.Points.Count} points");
    }

    [Fact]
    public void With_a_road_the_route_follows_it_and_beats_the_straight_line()
    {
        var road = BuiltRoads.Lay(Site(null), "s", [new Spot(8, 30), new Spot(52, 30)], "road", false);
        var g = Table(road);
        Spot from = new(10, 32), to = new(50, 32);
        var route = Go(g, Laid(), from, to, Foot);
        Assert.True(route.Total < OpenSeconds(from, to, Foot) * 0.8, $"the road takes {route.Total:0} s against {OpenSeconds(from, to, Foot):0} s across the fields");
        var middle = Path(g, route).Where(p => p.X is > 15 and < 45).ToList();
        Assert.NotEmpty(middle);
        Assert.All(middle, p => Assert.InRange(p.Y, 29.5, 30.5));
        var roadOnly = TravelMap.For(g, "s", null)!.Find(At(g, 9, 30.2), At(g, 51, 30.2), Foot);
        Assert.NotNull(roadOnly);
        Assert.Null(TravelMap.For(g, "s", null)!.Find(At(g, 9, 40), At(g, 51, 30.2), Foot));
    }

    [Fact]
    public void Uphill_is_slower_than_the_same_distance_on_the_flat_and_downhill_is_not()
    {
        Spot west = new(10, 30), east = new(50, 30);
        double flat = Go(Table(), Laid(), west, east, Foot).Total;
        var slope = Laid((x, _) => x * MetresPerUnit * 0.5);
        double up = Go(Table(), slope, west, east, Foot).Total, down = Go(Table(), slope, east, west, Foot).Total;
        Assert.InRange(up / flat, 1 / HillPace.Factor(0.5) * 0.98, 1 / HillPace.Factor(0.5) * 1.02);
        Assert.InRange(down / flat, 0.98, 1.02);
    }

    [Fact]
    public void A_hill_too_steep_to_climb_is_walked_round()
    {
        double Hill(double x, double y)
        {
            double dx = (x - 30) * MetresPerUnit, dy = (y - 30) * MetresPerUnit;
            return 400 * Math.Exp(-(dx * dx + dy * dy) / (2 * 150 * 150));
        }
        var g = Table();
        Spot west = new(10, 30), east = new(50, 30);
        var route = Go(g, Laid(Hill), west, east, Foot);
        double flat = Go(g, Laid(), west, east, Foot).Total;
        Assert.True(Path(g, route).Max(p => Math.Abs(p.Y - 30)) > 3, "the route swings round the hill");
        Assert.InRange(route.Total, flat, flat * 1.5);
    }

    [Fact]
    public void Woods_slow_the_route()
    {
        Spot west = new(10, 30), east = new(50, 30);
        double flat = Go(Table(), Laid(), west, east, Foot).Total;
        double wooded = Go(Table(), Laid(kind: (x, _) => x is > 20 and < 40 ? GroundKinds.Wood : GroundKinds.Grass), west, east, Foot).Total;
        double expected = (20 + 20 / Slowing(GroundKinds.Wood)) * MetresPerUnit / Foot.Open;
        Assert.InRange(wooded, expected * 0.98, expected * 1.03);
        Assert.True(wooded > flat * 1.4);
    }

    static double Slowing(byte kind) => 1 / GroundRoute.GroundFactor(kind);

    [Fact]
    public void Water_turns_the_route_to_the_bridge_and_with_no_crossing_there_is_no_route()
    {
        var grid = Laid(kind: River);
        var road = BuiltRoads.Lay(Site(grid), "s", [new Spot(25, 10), new Spot(37, 10)], "road", false);
        Assert.Contains(road.Parts, p => p.Class == "bridge");
        var g = Table(road);
        Spot west = new(20, 40), east = new(40, 40);
        var route = Go(g, grid, west, east, Foot);
        var path = Path(g, route);
        Assert.Contains(path, p => p.Y < 12);
        Assert.True(route.Total > OpenSeconds(west, east, Foot) * 2);
        Assert.Null(TravelMap.For(Table(), "s", grid)!.Find(At(g, west.X, west.Y), At(g, east.X, east.Y), Foot));
    }

    [Fact]
    public void A_bridge_sign_alone_carries_the_route_over_the_water()
    {
        var grid = Laid(kind: River);
        var bridge = new BuiltThing { Kind = BuiltKinds.Details, Parts = { BuiltSite.Spanning("bridge", new Spot(29.5, 20), new Spot(32.3, 20)) } };
        var g = Table(bridge);
        var route = Go(g, grid, new Spot(20, 20), new Spot(40, 20), Cart);
        Assert.InRange(route.Total, OpenSeconds(new(20, 20), new(40, 20), Cart) * 0.6, OpenSeconds(new(20, 20), new(40, 20), Cart) * 1.05);
    }

    [Fact]
    public void A_cart_keeps_to_the_ways_it_can_use()
    {
        var grid = Laid(kind: River);
        var road = BuiltRoads.Lay(Site(grid), "s", [new Spot(25, 10), new Spot(37, 10)], "road", false);
        var path = new BuiltThing { Kind = BuiltKinds.Road, Parts = { BuiltSite.Line(Footpaths.Path, [new Spot(27, 40), new Spot(35, 40)]) } };
        var g = Table(road, path);
        Spot west = new(20, 40), east = new(40, 40);
        Assert.All(Path(g, Go(g, grid, west, east, Foot)), p => Assert.InRange(p.Y, 38, 42));
        Assert.Contains(Path(g, Go(g, grid, west, east, Cart)), p => p.Y < 12);
        Assert.Equal(0, Cart.On(Footpaths.Path));
        Assert.Equal(0, Cart.On(Grades.Stairs));
    }

    [Fact]
    public void A_cart_does_not_take_the_stairs_of_a_lane()
    {
        var grid = Laid(kind: River);
        var lane = new BuiltThing
        {
            Kind = BuiltKinds.Road,
            Parts = { BuiltSite.Line("lane", [new Spot(27, 40), new Spot(35, 40)]), BuiltSite.Line(Grades.Stairs, [new Spot(29, 40), new Spot(33, 40)]) },
        };
        var g = Table(lane);
        Assert.NotNull(TravelMap.For(g, "s", grid)!.Find(At(g, 20, 40), At(g, 40, 40), Foot));
        Assert.Null(TravelMap.For(g, "s", grid)!.Find(At(g, 20, 40), At(g, 40, 40), Cart));
    }

    [Fact]
    public void A_packs_paces_replace_the_defaults()
    {
        var pack = new PackManifest
        {
            Id = "rules",
            Paces =
            [
                new() { Traveller = "Foot", Way = "open", MetresPerSecond = Foot.Open * 2 },
                new() { Traveller = "foot", Way = "open", MetresPerSecond = 9, Age = "ancient" },
                new() { Traveller = "foot", Way = "road", MetresPerSecond = 0 },
                new() { Traveller = "camel", Way = "open", MetresPerSecond = 1.2 },
                new() { Traveller = "foot", Way = "track", MetresPerSecond = -1 },
            ],
        };
        var paces = Travellers.Merged(Manners.Later, [pack]);
        Assert.Equal(Foot.Open * 2, paces[Travellers.Foot].Open);
        Assert.Equal(0, paces[Travellers.Foot].On("road"));
        Assert.Equal(Foot.On("track"), paces[Travellers.Foot].On("track"));
        Assert.Equal(1.2, paces["camel"].Open);
        Assert.Equal(9, Travellers.Merged(Manners.Ancient, [pack])[Travellers.Foot].Open);
        Spot from = new(10, 10), to = new(50, 40);
        double before = Go(Table(), Laid(), from, to, Foot).Total, after = Go(Table(), Laid(), from, to, paces[Travellers.Foot]).Total;
        Assert.InRange(after / before, 0.49, 0.51);
        var copy = Json.Deserialize<PackManifest>(Json.Serialize(pack));
        Assert.Equal(Json.Serialize(pack), Json.Serialize(copy));
        Assert.DoesNotContain("paces", Json.Serialize(new PackManifest { Id = "plain" }));
    }

    [Fact]
    public void Defaults_differ_by_age_where_history_does()
    {
        var ancient = Travellers.Default(Manners.Ancient);
        var later = Travellers.Default(Manners.Later);
        Assert.Equal(later[Travellers.Foot].On("road"), ancient[Travellers.Foot].On("road"));
        Assert.True(later[Travellers.Cart].On("road") > ancient[Travellers.Cart].On("road"));
        Assert.True(later[Travellers.Coach].On("road") > ancient[Travellers.Coach].On("road"));
        Assert.All(Travellers.Names, n => Assert.True(later[n].Open < later[n].On("road"), $"{n} goes slower off the road"));
    }

    [Fact]
    public void A_routes_legs_add_up_and_it_starts_and_ends_where_asked()
    {
        var road = BuiltRoads.Lay(Site(null), "s", [new Spot(8, 30), new Spot(52, 30)], "road", false);
        var g = Table(road);
        var from = At(g, 10, 33);
        var to = At(g, 50, 27);
        var route = TravelMap.For(g, "s", Laid())!.Find(from, to, Foot)!;
        Assert.Equal(route.Points.Count - 1, route.Seconds.Count);
        Assert.Equal(route.Seconds.Sum(), route.Total, 9);
        Assert.All(route.Seconds, s => Assert.True(s >= 0));
        Assert.InRange(route.Points[0].X - from.X, -Close, Close);
        Assert.InRange(route.Points[0].Z - from.Z, -Close, Close);
        Assert.InRange(route.Points[^1].X - to.X, -Close, Close);
        Assert.InRange(route.Points[^1].Z - to.Z, -Close, Close);
    }

    [Fact]
    public void A_start_in_water_or_in_a_house_starts_from_the_nearest_free_ground()
    {
        var grid = Laid(kind: River);
        var house = new BuiltThing { Kind = BuiltKinds.Building, Parts = { BuiltSite.Block("house", new Spot(15, 15), new Spot(1, 0), 1, 1) } };
        var g = Table(house);
        var map = TravelMap.For(g, "s", grid)!;
        var wet = map.Find(At(g, 30.9, 20), At(g, 20, 20), Foot)!;
        var first = Back(g, wet.Points[0]);
        Assert.True(first.X is < 30 or > 31.8, $"the route starts on the bank at {first}");
        Assert.InRange(first.X, 29, 33);
        var housed = map.Find(At(g, 15, 15), At(g, 20, 20), Foot)!;
        var door = Back(g, housed.Points[0]);
        Assert.True(Math.Max(Math.Abs(door.X - 15), Math.Abs(door.Y - 15)) > 0.5, $"the route starts outside the house at {door}");
        Assert.True((door - new Spot(15, 15)).Length < 1.5);
    }

    [Fact]
    public void A_route_across_the_sheet_searches_a_bounded_part_of_it()
    {
        var plain = Table();
        var open = TravelMap.For(plain, "s", Laid())!;
        Assert.Equal(TravelMap.MostCellsAcross * TravelMap.MostCellsAcross, open.Cells);
        Assert.NotNull(open.Find(At(plain, 5, 10), At(plain, 55, 50), Foot, out int across));
        Assert.True(across < open.Cells / 4, $"{across} of {open.Cells} cells");
        var road = BuiltRoads.Lay(Site(null), "s", [new Spot(5, 30), new Spot(55, 30)], "chaussee", false);
        var g = Table(road);
        var map = TravelMap.For(g, "s", Laid())!;
        Assert.NotNull(map.Find(At(g, 6, 31), At(g, 54, 31), Travellers.Default(Manners.Later)[Travellers.Coach], out int along));
        Assert.True(along < map.Cells / 4, $"{along} of {map.Cells} cells and {map.WayNodes} way points");
        Assert.NotNull(map.Find(At(g, 5, 10), At(g, 55, 50), Foot, out int both));
        Assert.True(both < map.Cells, $"{both} of {map.Cells} cells and {map.WayNodes} way points");
    }
}
