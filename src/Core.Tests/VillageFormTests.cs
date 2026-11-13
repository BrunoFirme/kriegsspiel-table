using KTable.Core.Model;

namespace KTable.Core.Tests;

public class VillageFormTests
{
    const double Side = 60, MetresToTheUnit = 50, Village = 5;
    static readonly Spot Middle = new(30, 30);

    static BuiltSite Site(ReliefGrid? grid = null, params BuiltThing[] others) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = grid ?? ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = others };

    static BuiltThing Laid(string plan, double houses = 0.5, double walls = 0, int seed = 4, double radius = Village, BuiltSite? site = null) =>
        Settlements.Lay(site ?? Site(), "s", Middle, radius, seed, houses, walls, Plans.Value(plan));

    static List<BuiltPart> Of(BuiltThing t, string kind, string cls) => t.Parts.Where(p => p.Kind == kind && p.Class == cls).ToList();

    static List<Spot> Points(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    static Spot Centre(BuiltPart part) => new(Points(part).Average(p => p.X), Points(part).Average(p => p.Y));

    static double Long(BuiltPart part) => Points(part).Max(p => (p - Points(part)[0]).Length);

    [Fact]
    public void A_plan_is_stored_by_its_number_and_an_old_place_keeps_its_plan()
    {
        Assert.Equal(Plans.Grown, Plans.Of(0));
        Assert.Equal(Plans.Grid, Plans.Of(1));
        Assert.All(Plans.All, plan => Assert.Equal(plan, Plans.Of(Plans.Value(plan))));
        Assert.Equal(Plans.Grown, Plans.Of(Plans.Value("crescent")));
        Assert.All(Plans.All, plan => Assert.Equal(Json.Serialize(Laid(plan)), Json.Serialize(Laid(plan))));
    }

    [Fact]
    public void A_street_village_is_one_street_with_long_strips_behind_its_houses()
    {
        var village = Laid(Plans.Street, 0.6);
        Assert.Empty(Of(village, PartKinds.Line, "lane"));
        Assert.Single(Of(village, PartKinds.Line, "street"));
        Assert.True(Of(village, PartKinds.Block, "house").Count > 12);
        var strips = Of(village, PartKinds.Area, "garden").Concat(Of(village, PartKinds.Area, "orchard")).ToList();
        Assert.True(strips.Count > 10, $"{strips.Count} strips");
        Assert.True(strips.Average(Long) > Site().M(80), "the plots run far back from the street");
        Assert.True(strips.Average(Long) > 2 * Of(Laid(Plans.Grown, 0.6), PartKinds.Area, "garden").DefaultIfEmpty(strips[0]).Average(Long) || Of(Laid(Plans.Grown, 0.6), PartKinds.Area, "garden").Count == 0);
    }

    [Fact]
    public void A_green_village_parts_its_street_round_a_green_that_holds_pond_and_church()
    {
        var village = Laid(Plans.Green);
        var green = Assert.Single(Of(village, PartKinds.Area, "green"));
        Assert.Equal(3, Of(village, PartKinds.Line, "street").Count);
        Assert.True(Enclosures.Inside(Points(green), Centre(Assert.Single(Of(village, PartKinds.Block, "church")))));
        Assert.True(Enclosures.Inside(Points(green), Centre(Assert.Single(Of(village, PartKinds.Area, "pond")))));
        var houses = Of(village, PartKinds.Block, "house");
        Assert.True(houses.Count > 8);
        Assert.All(houses, h => Assert.False(Enclosures.Inside(Points(green), Centre(h)), "no house stands on the green"));
    }

    [Fact]
    public void A_round_village_rings_a_small_green_with_one_way_in()
    {
        var village = Laid(Plans.Round);
        var green = Assert.Single(Of(village, PartKinds.Area, "green"));
        var lane = Assert.Single(Of(village, PartKinds.Line, "lane"));
        var houses = Of(village, PartKinds.Block, "house");
        Assert.True(houses.Count >= 6, $"{houses.Count} farmsteads");
        var reaches = houses.Select(h => (Centre(h) - Centre(green)).Length).ToList();
        Assert.True(reaches.Max() - reaches.Min() < Site().M(8), "all stand at one reach from the green's middle");
        var way = (Points(lane)[0] - Centre(green)).Unit;
        Assert.All(houses, h => Assert.True((Centre(h) - Centre(green)).Unit.Dot(way) < 0.97, "the way in is left open"));
        Assert.True(Of(village, PartKinds.Area, "garden").Count + Of(village, PartKinds.Area, "orchard").Count >= houses.Count / 2);
    }

    [Fact]
    public void A_green_finds_dry_ground_or_nothing_is_laid()
    {
        var river = ReliefGrid.Flat(Side, Side);
        for (int y = (int)(river.Rows * 0.48); y < (int)(river.Rows * 0.52); y++)
            for (int x = 0; x < river.Cols; x++) river.Kind[y * river.Cols + x] = GroundKinds.Water;
        var village = Laid(Plans.Round, site: Site(river));
        var green = Assert.Single(Of(village, PartKinds.Area, "green"));
        Assert.All(Points(green), p => Assert.True(Math.Abs(p.Y - 30) > 1.2, "the green keeps clear of the river"));
        var sea = ReliefGrid.Flat(Side, Side);
        Array.Fill(sea.Kind, GroundKinds.Water);
        Assert.All(Plans.All, plan => Assert.Empty(Laid(plan, site: Site(sea)).Parts));
    }

    [Fact]
    public void A_wall_hugs_what_is_built_and_opens_where_the_road_passes()
    {
        var road = new BuiltThing { Kind = BuiltKinds.Road, Parts = { BuiltSite.Line("road", [new Spot(5, 30), new Spot(55, 30)]) } };
        var site = Site(null, road);
        var town = Laid(Plans.Street, 0.8, 0.5, radius: 7, site: site);
        var wall = Of(town, PartKinds.Line, "wall").SelectMany(Points).ToList();
        Assert.True(wall.Count > 20);
        var reaches = wall.Select(p => (p - Middle).Length).ToList();
        Assert.True(reaches.Max() > reaches.Min() * 1.5, $"a street town's wall is long, not round: {reaches.Min():0.0} to {reaches.Max():0.0}");
        Assert.All(wall, p => Assert.True(Math.Abs(p.Y - 30) > site.M(4), "no wall across the road"));
        Assert.True(Of(town, PartKinds.Sign, "tower").Count >= 6);
        var ditch = Assert.Single(Of(town, PartKinds.Line, "ditch"));
        Assert.All(Of(town, PartKinds.Block, "house"), h => Assert.True(Enclosures.Inside(Points(ditch), Centre(h))));
        Assert.All(Of(town, PartKinds.Area, "garden"), g => Assert.All(Points(g), p => Assert.True(Enclosures.Inside(Points(ditch), p), "no plot is cut by the wall")));
    }

    [Fact]
    public void A_village_is_fenced_not_walled_and_keeps_its_crofts_inside()
    {
        var village = Laid(Plans.Grown, 0.35, 0.5, radius: 4);
        Assert.Empty(Of(village, PartKinds.Line, "wall"));
        Assert.Empty(Of(village, PartKinds.Sign, "tower"));
        var fence = Of(village, PartKinds.Line, "palisade").SelectMany(Points).ToList();
        Assert.True(fence.Count > 10);
        var crofts = Of(village, PartKinds.Area, "garden").Concat(Of(village, PartKinds.Area, "orchard")).ToList();
        Assert.NotEmpty(crofts);
        var hull = Enclosures.Hull(fence);
        Assert.All(crofts, c => Assert.True(Enclosures.Inside(hull, Centre(c))));
    }

    [Fact]
    public void A_bastioned_trace_is_fitted_round_the_town()
    {
        var town = Laid(Plans.Street, 0.8, 1, radius: 9);
        var rampart = Of(town, PartKinds.Line, "rampart").MaxBy(p => p.At.Count)!;
        Assert.All(Of(town, PartKinds.Block, "house"), h => Assert.True(Enclosures.Inside(Points(rampart), Centre(h)), "every house is inside the rampart"));
        var middle = Enclosures.Middle(Points(rampart));
        var reaches = Points(rampart).Select(p => (p - middle).Length).ToList();
        Assert.True(reaches.Max() > reaches.Min() * 1.8, $"the trace is drawn out along a street town: {reaches.Min():0.0} to {reaches.Max():0.0}");
        Assert.Single(Of(town, PartKinds.Line, "moat"));
    }

    [Fact]
    public void A_hull_wraps_its_points_and_tells_inside_from_outside()
    {
        var hull = Enclosures.Hull([new Spot(0, 0), new Spot(4, 0), new Spot(4, 4), new Spot(0, 4), new Spot(2, 2), new Spot(1, 3)]);
        Assert.Equal(4, hull.Count);
        Assert.True(Enclosures.Inside(hull, new Spot(2, 2)));
        Assert.False(Enclosures.Inside(hull, new Spot(5, 2)));
    }

    [Fact]
    public void An_ancient_camp_is_a_rounded_card_with_four_gates_and_rows_of_barracks()
    {
        var camp = Camps.Lay(Site(), "s", Middle, 3, 2, 0);
        Assert.True(Camps.Is(camp));
        Assert.False(Forts.Plain(camp));
        Assert.Equal(4, Of(camp, PartKinds.Line, "wall").Count);
        Assert.Empty(Of(camp, PartKinds.Line, "rampart"));
        Assert.Single(Of(camp, PartKinds.Line, "ditch"));
        Assert.Single(Of(camp, PartKinds.Block, "public"));
        Assert.Equal(2, Of(camp, PartKinds.Block, "magazine").Count);
        Assert.True(Of(camp, PartKinds.Block, "barracks").Count >= 8);
        var wall = Of(camp, PartKinds.Line, "wall").SelectMany(Points).ToList();
        Assert.Equal(3, wall.Max(p => Math.Abs(p.X - 30)), 1);
        Assert.True(wall.Max(p => Math.Abs(p.Y - 30)) < 2.2, "it is longer than it is wide");
        Assert.All(Of(camp, PartKinds.Block, "barracks"), b => Assert.True(Math.Abs(Centre(b).X - 30) < 3 && Math.Abs(Centre(b).Y - 30) < 2.1));
        var again = BuiltThings.Remade(Site(), camp, 9);
        Assert.True(Camps.Is(again) && Of(again, PartKinds.Line, "wall").Count == 4, "Again lays a camp, not a star");
    }
}
