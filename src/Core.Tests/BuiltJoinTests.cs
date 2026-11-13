using KTable.Core.Model;

namespace KTable.Core.Tests;

public class BuiltJoinTests
{
    const double Side = 60, MetresToTheUnit = 50;
    static readonly Spot Middle = new(30, 30);

    static BuiltSite Site(ReliefGrid? grid = null, params BuiltThing[] others) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = grid ?? ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = others };

    static BuiltThing Line(string kind, string cls, params Spot[] through) => new() { Kind = kind, Parts = { BuiltSite.Line(cls, through) } };

    static List<Spot> Points(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    static Spot Centre(BuiltPart part) => new(Points(part).Average(p => p.X), Points(part).Average(p => p.Y));

    static List<BuiltPart> Of(BuiltThing t, string kind, string cls) => t.Parts.Where(p => p.Kind == kind && p.Class == cls).ToList();

    static ReliefGrid River()
    {
        var river = ReliefGrid.Flat(Side, Side);
        for (int y = (int)(river.Rows * 0.48); y < (int)(river.Rows * 0.52); y++)
            for (int x = 0; x < river.Cols; x++) river.Kind[y * river.Cols + x] = GroundKinds.Water;
        return river;
    }

    [Fact]
    public void A_road_ending_near_another_joins_it()
    {
        var highway = Line(BuiltKinds.Road, "chaussee", new Spot(5, 30), new Spot(55, 30));
        var lane = BuiltRoads.Lay(Site(null, highway), "s", [new Spot(20, 10), new Spot(20.2, 29.6)], "chaussee", false);
        Assert.Equal(30, Points(lane.Parts[0])[^1].Y, 3);
        var apart = BuiltRoads.Lay(Site(null, highway), "s", [new Spot(20, 10), new Spot(20, 28)], "chaussee", false);
        Assert.Equal(28, Points(apart.Parts[0])[^1].Y, 3);
    }

    [Fact]
    public void A_road_bridges_a_built_stream_a_track_fords_it_and_a_stream_laid_later_is_bridged_too()
    {
        var stream = Line(BuiltKinds.Water, "stream", new Spot(30, 5), new Spot(30, 55));
        var road = BuiltRoads.Lay(Site(null, stream), "s", [new Spot(10, 30), new Spot(50, 30)], "chaussee", false);
        var bridge = Assert.Single(Of(road, PartKinds.Sign, "bridge"));
        Assert.Equal(30, Centre(bridge).X, 2);
        var track = BuiltRoads.Lay(Site(null, stream), "s", [new Spot(10, 20), new Spot(50, 20)], "track", false);
        Assert.Single(Of(track, PartKinds.Sign, "ford"));
        var highway = Line(BuiltKinds.Road, "chaussee", new Spot(5, 30), new Spot(55, 30));
        var later = BuiltRoads.Lay(Site(null, highway), "s", [new Spot(30, 5), new Spot(30, 55)], "river", false);
        Assert.Single(Of(later, PartKinds.Sign, "bridge"));
    }

    [Fact]
    public void A_stream_laid_by_hand_wanders_and_a_chaussee_does_not()
    {
        var stream = BuiltRoads.Lay(Site(), "s", [new Spot(5, 30), new Spot(55, 30)], "stream", false);
        double swing = Points(stream.Parts[0]).Max(p => Math.Abs(p.Y - 30));
        Assert.True(swing > 0.02 && swing < 0.3, $"swings {swing:0.000} units");
        Assert.Equal(Json.Serialize(stream), Json.Serialize(BuiltRoads.Lay(Site(), "s", [new Spot(5, 30), new Spot(55, 30)], "stream", false)));
    }

    [Fact]
    public void A_road_laid_through_a_place_clears_its_way_and_leaves_the_rest()
    {
        var village = Settlements.Lay(Site(), "s", Middle, 5, 4, 0.7, 0, 0);
        var road = BuiltRoads.Lay(Site(null, village), "s", [new Spot(22, 24), new Spot(38, 37)], "chaussee", false);
        double perMetre = 1 / MetresToTheUnit, half = BuiltRoads.HalfWidthM("chaussee") * perMetre;
        var cleared = BuiltRoads.MadeWay(village, road, perMetre);
        Assert.NotNull(cleared);
        Assert.Equal(village.Id, cleared!.Id);
        var path = Points(road.Parts[0]);
        double Off(BuiltPart p) => Enumerable.Range(0, path.Count - 1).Min(i => Centre(p).FromSegment(path[i], path[i + 1]));
        Assert.All(cleared.Parts.Where(p => p.Kind == PartKinds.Block), p => Assert.True(Off(p) >= half));
        int before = village.Parts.Count(p => p.Kind == PartKinds.Block), after = cleared.Parts.Count(p => p.Kind == PartKinds.Block);
        Assert.True(after < before && after > before / 2, $"{before} buildings, {after} left");
        Assert.Null(BuiltRoads.MadeWay(village, BuiltRoads.Lay(Site(), "s", [new Spot(2, 2), new Spot(8, 2)], "road", false), perMetre));
        Assert.Null(BuiltRoads.MadeWay(Forts.Lay(Site(), "s", Middle, 4, 2, 5, 0.5, 0), road, perMetre));
    }

    [Fact]
    public void A_place_laid_beside_a_road_turns_its_street_to_it_and_joins_it()
    {
        var highway = Line(BuiltKinds.Road, "road", new Spot(5, 38), new Spot(55, 38));
        var village = Settlements.Lay(Site(null, highway), "s", Middle, 4, 6, 0.5, 0, 0);
        var link = Assert.Single(Of(village, PartKinds.Line, "track"));
        Assert.Equal(38, Points(link)[^1].Y, 2);
        var street = Points(Of(village, PartKinds.Line, "street")[0]);
        Assert.True(Math.Abs(street[^1].Y - street[0].Y) > Math.Abs(street[^1].X - street[0].X), "the street runs toward the road");
        Assert.Empty(Of(Settlements.Lay(Site(), "s", Middle, 4, 6, 0.5, 0, 0), PartKinds.Line, "track"));
    }

    [Fact]
    public void Hedges_stop_at_a_road()
    {
        var highway = Line(BuiltKinds.Road, "road", new Spot(0, 30), new Spot(60, 30));
        var country = BuiltDetails.Lay(Site(null, highway), "s", [new Spot(15, 30), new Spot(45, 30)], 14, 5, 0.9, 0);
        var hedges = country.Parts.Where(p => p.Kind == PartKinds.Line && p.Class is "hedge" or "fence" or "ditch").ToList();
        Assert.True(hedges.Count > 5);
        Assert.All(hedges, h => Assert.True(Points(h).All(p => p.Y > 30) || Points(h).All(p => p.Y < 30), "no hedge crosses the road"));
    }

    [Fact]
    public void A_fort_opens_toward_the_road_and_is_joined_to_it()
    {
        var highway = Line(BuiltKinds.Road, "road", new Spot(5, 40), new Spot(55, 40));
        var fort = Forts.Lay(Site(null, highway), "s", Middle, 4, 2, 5, 0.5, 0);
        var road = Assert.Single(Of(fort, PartKinds.Line, "road"));
        Assert.Equal(40, Points(road)[^1].Y, 2);
        var gate = Assert.Single(Of(fort, PartKinds.Sign, "bridge"));
        Assert.True(Centre(gate).Y > 31, "the gate faces the road");
        Assert.Equal(4, Of(fort, PartKinds.Block, "barracks").Count);
        Assert.Empty(Of(Forts.Lay(Site(), "s", Middle, 4, 2, 5, 0.5, 0), PartKinds.Line, "road"));
        Assert.Single(Of(Camps.Lay(Site(null, highway), "s", Middle, 4, 2, 0), PartKinds.Line, "road"));
    }

    [Fact]
    public void A_fort_or_camp_dragged_over_a_river_stands_beside_it_and_over_open_water_is_not_laid()
    {
        var site = Site(River());
        var fort = Forts.Lay(site, "s", Middle, 3, 2, 5, 0.5, 0);
        Assert.NotEmpty(fort.Parts);
        Assert.All(Of(fort, PartKinds.Line, "rampart").SelectMany(Points), p => Assert.True(Math.Abs(p.Y - 30) > 1.2, "the rampart keeps off the river"));
        var camp = Camps.Lay(site, "s", Middle, 3, 2, 0);
        Assert.All(Of(camp, PartKinds.Line, "wall").SelectMany(Points), p => Assert.True(Math.Abs(p.Y - 30) > 1.2, "the camp keeps off the river"));
        Assert.True(Camps.Is(BuiltThings.Remade(site, camp, 3)));
        var sea = ReliefGrid.Flat(Side, Side);
        Array.Fill(sea.Kind, GroundKinds.Water);
        Assert.Empty(Forts.Lay(Site(sea), "s", Middle, 3, 2, 5, 0.5, 0).Parts);
        Assert.Empty(Camps.Lay(Site(sea), "s", Middle, 3, 2, 0).Parts);
    }
}
