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

    [Fact]
    public void A_road_laid_through_a_place_gains_houses_along_it()
    {
        var village = Settlements.Lay(Site(), "s", Middle, 5, 4, 0.7, 0, 0);
        var road = BuiltRoads.Lay(Site(null, village), "s", [new Spot(20, 21), new Spot(40, 39)], "chaussee", false);
        var cleared = BuiltRoads.MadeWay(village, road, 1 / MetresToTheUnit) ?? village;
        var fronted = Settlements.Fronted(Site(), cleared, road);
        Assert.NotNull(fronted);
        var path = Points(road.Parts[0]);
        var added = fronted!.Parts.Skip(cleared.Parts.Count).Where(p => p.Kind == PartKinds.Block && p.Class == "house").ToList();
        Assert.True(added.Count >= 4, $"{added.Count} houses front the new road");
        Assert.All(added, h => Assert.True(Centre(h).FromSegment(path[0], path[^1]) < 0.6 && (Centre(h) - Middle).Length <= 5));
        Assert.Null(Settlements.Fronted(Site(), village, BuiltRoads.Lay(Site(), "s", [new Spot(2, 2), new Spot(8, 2)], "road", false)));
        Assert.Null(Settlements.Fronted(Site(), Settlements.Lay(Site(), "s", Middle, 5, 4, 0.7, 0.5, 0), road));
    }

    [Fact]
    public void A_road_laid_across_a_hedge_cuts_a_gap_in_it()
    {
        var country = new BuiltThing { Kind = BuiltKinds.Details };
        country.Parts.Add(BuiltSite.Line("hedge", [new Spot(10, 20), new Spot(30, 20), new Spot(50, 20)]));
        country.Parts.Add(BuiltSite.Line("hedge", [new Spot(10, 50), new Spot(50, 50)]));
        var road = BuiltRoads.Lay(Site(), "s", [new Spot(25, 5), new Spot(25, 35)], "chaussee", false);
        var cleared = BuiltRoads.MadeWay(country, road, 1 / MetresToTheUnit);
        Assert.NotNull(cleared);
        var hedges = Of(cleared!, PartKinds.Line, "hedge");
        Assert.Equal(3, hedges.Count);
        double half = BuiltRoads.HalfWidthM("chaussee") / MetresToTheUnit;
        Assert.All(hedges.SelectMany(Points), p => Assert.True(Math.Abs(p.X - 25) >= half - 0.001 || p.Y > 40));
        Assert.Contains(hedges, h => Points(h).Any(p => Math.Abs(p.X - (25 - half)) < 0.01));
    }

    [Fact]
    public void A_camp_keeps_off_even_a_narrow_river()
    {
        var narrow = ReliefGrid.Flat(Side, Side);
        int row = narrow.Rows / 2;
        for (int x = 0; x < narrow.Cols; x++) narrow.Kind[row * narrow.Cols + x] = GroundKinds.Water;
        var camp = Camps.Lay(Site(narrow), "s", new Spot(30, 30.4), 3, 2, 0);
        Assert.NotEmpty(camp.Parts);
        var wall = Of(camp, PartKinds.Line, "wall").SelectMany(Points).ToList();
        Assert.True(wall.All(p => p.Y > 30.2) || wall.All(p => p.Y < 29.8), "the camp stands on one bank");
    }

    [Fact]
    public void A_place_laid_on_an_ancient_map_has_no_cross_or_shrine_and_keeps_none_when_laid_again()
    {
        var later = Settlements.Lay(Site(), "s", Middle, 5, 4, 0.5, 0, 0);
        Assert.NotEmpty(Of(later, PartKinds.Sign, "cross"));
        var old = Settlements.Lay(Site(), "s", Middle, 5, 4, 0.5, 0, 0, old: true);
        Assert.Empty(Of(old, PartKinds.Sign, "cross"));
        Assert.Empty(Of(old, PartKinds.Sign, "shrine"));
        Assert.Single(Of(old, PartKinds.Block, "church"));
        var again = BuiltThings.Remade(Site(), old, 9);
        Assert.Empty(Of(again, PartKinds.Sign, "cross"));
        Assert.Equal(1, again.Setting(Settlements.Old, 0));
    }

    [Fact]
    public void A_thing_remembers_the_scale_it_was_laid_at_and_laying_it_again_takes_the_scale_of_the_day()
    {
        var g = CoreSamples.State();
        g.Sheets.Add(new SheetDef { Id = "s-000040", Size = new Size2(40, 20) });
        g.NextId = 60;
        g.Calibration.UnitsPerMeter = 0.02;
        Assert.Null(KTable.Core.Protocol.StateMachine.Apply(g, new KTable.Core.Protocol.LayBuilt { Thing = CoreSamples.Built("", "s-000040") }, out _));
        Assert.Equal(0.02, g.Built[0].Setting(BuiltThings.Scale, 0));
        g.Calibration.UnitsPerMeter = 0.2;
        Assert.Equal(0.02, BuiltGeometry.Moved(g.Built[0], 100, 0).Setting(BuiltThings.Scale, 0));
        var close = new BuiltSite { Width = 40, Height = 20, UnitsPerMetre = 0.2, Grid = ReliefGrid.Flat(40, 20) };
        Assert.Equal(0.2, BuiltThings.Remade(close, g.Built[0], 5).Setting(BuiltThings.Scale, 0));
    }

    [Fact]
    public void A_part_is_turned_and_sized_about_its_own_middle_and_kept_from_then_on()
    {
        var thing = new BuiltThing { Outline = { 0, 0, 5000, 0 }, Parts = { BuiltSite.Block("house", new Spot(10, 10), new Spot(1, 0), 4, 2), BuiltSite.Sign("cart", new Spot(3, 3)) } };
        var turned = BuiltThings.WithPartTurned(thing, 0, Math.PI / 2);
        Assert.Equal(new Spot(10, 10), Centre(turned.Parts[0]));
        Assert.Equal(2, Points(turned.Parts[0]).Max(p => p.X) - Points(turned.Parts[0]).Min(p => p.X), 2);
        Assert.Equal(4, Points(turned.Parts[0]).Max(p => p.Y) - Points(turned.Parts[0]).Min(p => p.Y), 2);
        Assert.True(turned.Parts[0].Kept && !thing.Parts[0].Kept);
        var sized = BuiltThings.WithPartSized(thing, 0, 1.5);
        Assert.Equal(new Spot(10, 10), Centre(sized.Parts[0]));
        Assert.Equal(6, Points(sized.Parts[0]).Max(p => p.X) - Points(sized.Parts[0]).Min(p => p.X), 2);
        var cart = BuiltThings.WithPartTurned(thing, 1, Math.PI / 6);
        Assert.Equal(300, cart.Parts[1].Turn);
        Assert.Equal(new Spot(3, 3), Centre(cart.Parts[1]));
        Assert.Equal(3300, BuiltThings.WithPartTurned(thing, 1, -Math.PI / 6).Parts[1].Turn);
    }

    [Fact]
    public void A_whole_place_turns_about_its_middle()
    {
        var thing = new BuiltThing { Outline = { 10000, 10000, 15000, 10000 }, Parts = { BuiltSite.Block("house", new Spot(14, 10), new Spot(1, 0), 2, 1), BuiltSite.Sign("well", new Spot(10, 10)) } };
        var turned = BuiltThings.Turned(thing, Math.PI / 2);
        Assert.Equal(10, Centre(turned.Parts[0]).X, 2);
        Assert.Equal(14, Centre(turned.Parts[0]).Y, 2);
        Assert.Equal(new Spot(10, 10), Centre(turned.Parts[1]));
        Assert.Equal(new[] { 10000, 10000, 10000, 15000 }, turned.Outline);
    }

    [Fact]
    public void A_thing_added_by_hand_joins_the_place_and_is_kept_when_the_place_is_laid_again()
    {
        var site = Site();
        var village = Settlements.Lay(site, "s", Middle, 4, 4, 0.4, 0, 0);
        Assert.All(BuiltThings.Addable, id => Assert.Equal(Catalogue.Find(id)!.Laid, BuiltThings.Made(site, id, Middle)[0].Class));
        Assert.Equal(["mill", "mill"], BuiltThings.Made(site, "windmill", Middle).Select(p => p.Class));
        var barn = BuiltThings.Made(site, "barn", new Spot(31, 33))[0];
        Assert.Equal(PartKinds.Block, barn.Kind);
        Assert.Equal(18 / MetresToTheUnit, Points(barn).Max(p => p.X) - Points(barn).Min(p => p.X), 3);
        Assert.Equal(PartKinds.Sign, BuiltThings.Made(site, "stall", Middle)[0].Kind);
        var more = BuiltThings.WithPart(village, barn);
        Assert.Equal(village.Parts.Count + 1, more.Parts.Count);
        var again = BuiltThings.Remade(site, more, 9);
        Assert.Contains(again.Parts, p => p.Class == "barn" && p.Kept && Centre(p) == Centre(barn));
    }

    [Fact]
    public void Changing_a_laid_place_s_houses_keeps_its_seed_name_and_main_street()
    {
        var site = Site();
        var village = Settlements.Lay(site, "s", Middle, 5, 4, 0.3, 0, 0);
        village.Id = "w-1";
        village.Name = "Aubach";
        var denser = village.Clone();
        denser.Settings[Settlements.Houses] = 0.9;
        var town = BuiltThings.Remade(site, denser, village.Seed);
        Assert.Equal(("w-1", "Aubach", village.Seed), (town.Id, town.Name, town.Seed));
        Assert.True(Of(town, PartKinds.Block, "house").Count > 2 * Of(village, PartKinds.Block, "house").Count);
        Assert.Equal(Points(Of(village, PartKinds.Line, "street")[0]), Points(Of(town, PartKinds.Line, "street")[0]));
    }

    [Fact]
    public void A_lane_drawn_through_a_place_gains_houses_as_a_road_does()
    {
        Assert.Contains("lane", BuiltRoads.Roads);
        Assert.Contains("street", BuiltRoads.Roads);
        var village = Settlements.Lay(Site(), "s", Middle, 5, 4, 0.7, 0, 0);
        var lane = BuiltRoads.Lay(Site(null, village), "s", [new Spot(27, 26), new Spot(27, 34)], "lane", false);
        Assert.Equal("lane", lane.Parts[0].Class);
        var cleared = BuiltRoads.MadeWay(village, lane, 1 / MetresToTheUnit) ?? village;
        var fronted = Settlements.Fronted(Site(), cleared, lane);
        Assert.NotNull(fronted);
        Assert.True(fronted!.Parts.Count > cleared.Parts.Count);
    }
}
