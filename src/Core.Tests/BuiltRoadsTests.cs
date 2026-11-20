using KTable.Core.Model;

namespace KTable.Core.Tests;

public class BuiltRoadsTests
{
    const double Side = 60, MetresToTheUnit = 50;

    static BuiltSite Site(ReliefGrid? grid = null, params BuiltThing[] others) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = grid, HeightUnit = Side, Others = others };

    static ReliefGrid River(double from, double to)
    {
        var grid = ReliefGrid.Flat(Side, Side);
        for (int y = 0; y < grid.Rows; y++)
            for (int x = (int)(grid.Cols * from); x < (int)(grid.Cols * to); x++) grid.Kind[y * grid.Cols + x] = GroundKinds.Water;
        return grid;
    }

    [Fact]
    public void A_road_that_finds_its_way_within_one_relief_cell_follows_its_waypoints()
    {
        var site = new BuiltSite { Width = Side, Height = Side, UnitsPerMetre = 1 / 24414.5, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = Side };
        var track = BuiltRoads.Lay(site, "s", [new Spot(30, 30), new Spot(30.05, 30.03), new Spot(30.1, 30)], "track", true);
        Assert.Contains(track.Parts, p => p.Class == "track" && p.At.Count >= 4);
    }

    [Fact]
    public void A_road_across_a_planet_sized_map_stays_a_few_thousand_points()
    {
        var site = new BuiltSite { Width = Side, Height = Side, UnitsPerMetre = 1 / 75000.0, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = Side };
        foreach (var cls in new[] { "track", "chaussee" })
        {
            var road = BuiltRoads.Lay(site, "s", [new Spot(5, 5), new Spot(30, 20), new Spot(55, 50)], cls, true);
            Assert.InRange(road.Parts.Sum(p => p.At.Count / 2), 2, 25000);
        }
    }

    [Fact]
    public void A_road_runs_through_its_waypoints_with_its_corners_rounded()
    {
        var road = BuiltRoads.Lay(Site(), "s", [new Spot(10, 10), new Spot(30, 10), new Spot(30, 40)], "road", false);
        Assert.Equal(BuiltKinds.Road, road.Kind);
        var line = Assert.Single(road.Parts);
        var points = BuiltGeometry.Points(line.At).Select(Spot.Of).ToList();
        Assert.Equal(new Spot(10, 10), points[0]);
        Assert.Equal(new Spot(30, 40), points[^1]);
        Assert.True(points.Count > 3, "the corner is cut into several points");
        Assert.DoesNotContain(new Spot(30, 10), points);
        Assert.Equal(6, road.Outline.Count);
    }

    [Fact]
    public void A_road_over_water_gets_a_bridge_spanning_the_water_and_a_track_over_a_narrow_one_a_ford()
    {
        var site = Site(River(0.5, 0.56));
        var road = BuiltRoads.Lay(site, "s", [new Spot(10, 30), new Spot(50, 30)], "road", false);
        var bridge = Assert.Single(road.Parts, p => p.Kind == PartKinds.Sign);
        Assert.Equal("bridge", bridge.Class);
        Assert.InRange(bridge.At[0] / (double)BuiltGeometry.PerUnit, 30, 34);
        var ends = BuiltGeometry.Points(bridge.At).Select(Spot.Of).ToList();
        Assert.Equal(3, ends.Count);
        Assert.InRange((ends[2] - ends[1]).Length, 3.4, 4.4);
        var narrow = Site(River(0.5, 0.505));
        var track = BuiltRoads.Lay(narrow, "s", [new Spot(10, 30), new Spot(50, 30)], "track", false);
        Assert.Equal("ford", Assert.Single(track.Parts, p => p.Kind == PartKinds.Sign).Class);
    }

    [Fact]
    public void A_road_that_finds_its_way_goes_round_the_hill()
    {
        var grid = ReliefGrid.Flat(Side, Side);
        for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Cols; x++)
            {
                double dx = (x / (double)(grid.Cols - 1) - 0.5) * Side, dy = (y / (double)(grid.Rows - 1) - 0.5) * Side;
                grid.H[y * grid.Cols + x] = (float)(0.1 * Math.Exp(-(dx * dx + dy * dy) / 3));
            }
        var straight = BuiltRoads.Lay(Site(grid), "s", [new Spot(8, 30), new Spot(52, 30)], "road", false);
        var found = BuiltRoads.Lay(Site(grid), "s", [new Spot(8, 30), new Spot(52, 30)], "road", true);
        double Farthest(BuiltThing t) => BuiltGeometry.Points(t.Parts[0].At).Max(p => Math.Abs(p.Y / (double)BuiltGeometry.PerUnit - 30));
        Assert.True(Farthest(straight) < 0.08, "a road laid straight only wanders a few metres");
        Assert.True(Farthest(BuiltRoads.Lay(Site(grid), "s", [new Spot(8, 30), new Spot(52, 30)], "chaussee", false)) < 0.01, "a chaussée runs dead straight");
        Assert.True(Farthest(found) > 1, $"the found road swings {Farthest(found):0.0} units aside");
    }

    [Theory]
    [InlineData("road", 46, 3)]
    [InlineData("track", 52, 1)]
    [InlineData("lane", 52, 1)]
    public void A_long_road_over_steep_hills_lays_its_hairpins_end_to_end(string cls, int waypoints, int seed)
    {
        const double Wide = 300, MetresToUnit = 100;
        var grid = ReliefGrid.Flat(Wide, Wide);
        for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Cols; x++)
            {
                double u = x / (double)(grid.Cols - 1) * Wide, v = y / (double)(grid.Rows - 1) * Wide;
                grid.Kind[y * grid.Cols + x] = (x + y) % 37 == 0 ? GroundKinds.Water : GroundKinds.Grass;
                grid.H[y * grid.Cols + x] = (float)((Math.Sin(u * 0.07) * Math.Cos(v * 0.05) * 400 + Math.Sin(u * 0.31 + v * 0.23) * 60) / MetresToUnit);
            }
        var dice = new Random(seed);
        var pts = new List<Spot>();
        double px = 20 + dice.NextDouble() * 50, pz = 20 + dice.NextDouble() * 50;
        for (int i = 0; i < waypoints; i++)
        {
            pts.Add(new Spot(Math.Clamp(px, 1, Wide - 1), Math.Clamp(pz, 1, Wide - 1)));
            px += dice.NextDouble() * 7 - 1.5;
            pz += dice.NextDouble() * 7 - 1.5;
        }
        var site = new BuiltSite { Width = Wide, Height = Wide, UnitsPerMetre = 1 / MetresToUnit, Grid = grid, HeightUnit = 1, Manner = Manners.Continental };
        var line = BuiltRoads.Lay(site, "s", pts, cls, true).Parts.First(p => p.Kind == PartKinds.Line && p.Class == cls);
        var laid = BuiltGeometry.Points(line.At).Select(Spot.Of).ToList();
        Assert.True((laid[0] - pts[0]).Length < 1 && (laid[^1] - pts[^1]).Length < 1, "the road runs from its first waypoint to its last");
        Assert.True(laid.Zip(laid.Skip(1)).All(leg => (leg.Second - leg.First).Length < 20), "the road runs on without jumping back over itself");
    }

    [Fact]
    public void Details_lay_boundaries_and_farms_only_where_the_stroke_passed_and_join_farms_to_the_road()
    {
        var road = new BuiltThing { Kind = BuiltKinds.Road, Parts = { BuiltSite.Line("road", [new Spot(5, 20), new Spot(55, 20)]) } };
        var site = Site(null, road);
        var stroke = new[] { new Spot(15, 30), new Spot(45, 30) };
        var laid = BuiltDetails.Lay(site, "s", stroke, 10, 3, 0.6, 0.6);
        Assert.Equal(Json.Serialize(laid), Json.Serialize(BuiltDetails.Lay(site, "s", stroke, 10, 3, 0.6, 0.6)));
        Assert.Contains(laid.Parts, p => p.Class == "farm");
        Assert.Contains(laid.Parts, p => p.Class is "hedge" or "fence");
        Assert.Contains(laid.Parts, p => p.Class == "track");
        Assert.All(laid.Parts.Where(p => p.Kind == PartKinds.Block), p => Assert.InRange(p.At[1] / (double)BuiltGeometry.PerUnit, 18, 42));
        var bare = BuiltDetails.Lay(site, "s", stroke, 10, 3, 0, 0);
        Assert.Empty(bare.Parts);
    }
}
