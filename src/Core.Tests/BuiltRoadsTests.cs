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
    public void A_road_over_water_gets_a_bridge_and_a_track_over_a_narrow_one_a_ford()
    {
        var site = Site(River(0.5, 0.56));
        var road = BuiltRoads.Lay(site, "s", [new Spot(10, 30), new Spot(50, 30)], "road", false);
        var bridge = Assert.Single(road.Parts, p => p.Kind == PartKinds.Sign);
        Assert.Equal("bridge", bridge.Class);
        Assert.InRange(bridge.At[0] / (double)BuiltGeometry.PerUnit, 30, 34);
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
        Assert.True(Farthest(straight) < 0.01);
        Assert.True(Farthest(found) > 1, $"the found road swings {Farthest(found):0.0} units aside");
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
