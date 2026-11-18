using KTable.Core.Model;

namespace KTable.Core.Tests;

public class WorksTests
{
    const double Side = 60, MetresToTheUnit = 50;
    static readonly Spot Middle = new(30, 30);

    static BuiltSite Site(ReliefGrid? grid = null, int manner = 0, params BuiltThing[] others) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = grid ?? ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = others, Manner = manner };

    static double M(double metres) => metres / MetresToTheUnit;

    static List<Spot> Points(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    static List<BuiltPart> Of(BuiltThing t, string kind, string cls) => t.Parts.Where(p => p.Kind == kind && p.Class == cls).ToList();

    static List<Spot> Towers(BuiltThing t) => Of(t, PartKinds.Sign, "tower").Select(p => BuiltSite.Round(p).At).ToList();

    static bool Touches(BuiltPart block, List<Spot> line, double half)
    {
        var ring = Points(block);
        if (line.Zip(line.Skip(1)).Any(leg => BuiltSite.Round(block).At.FromSegment(leg.First, leg.Second) < half)) return true;
        return ring.Select((p, i) => (p, ring[(i + 1) % ring.Count])).Any(edge => line.Zip(line.Skip(1)).Any(leg => BuiltSite.Crossing(edge.p, edge.Item2, leg.First, leg.Second) != null));
    }

    static BuiltThing Road(params Spot[] through) => new() { Kind = BuiltKinds.Road, Parts = { BuiltSite.Line("road", through) } };

    static readonly List<Spot> Ell = [new(10, 10), new(20, 10), new(20, 20)];
    static readonly List<Spot> Square = [new(10, 10), new(20, 10), new(20, 20), new(10, 20), new(10, 10)];

    static ReliefGrid River()
    {
        var river = ReliefGrid.Flat(Side, Side);
        for (int y = 0; y < river.Rows; y++)
            for (int x = (int)(river.Cols * 0.49); x < (int)(river.Cols * 0.51); x++) river.Kind[y * river.Cols + x] = GroundKinds.Water;
        return river;
    }

    [Fact]
    public void A_shaky_stroke_is_straightened_into_its_legs()
    {
        var stroke = new List<Spot>();
        for (int i = 0; i <= 20; i++) stroke.Add(new Spot(10 + i * 0.5, 10 + (i % 2) * 0.05));
        for (int i = 1; i <= 20; i++) stroke.Add(new Spot(20 + (i % 2) * 0.05, 10 + i * 0.5));
        var legs = Works.Straightened(stroke, 0.2);
        Assert.Equal(3, legs.Count);
        Assert.Equal(20, legs[1].X, 1);
        Assert.Equal(10, legs[1].Y, 1);
    }

    [Fact]
    public void A_stone_wall_stands_towers_at_its_corners_and_ends_and_along_it_as_asked()
    {
        var none = Works.Lay(Site(), "s", Ell, Works.Wall, Works.NoTowers, false);
        var few = Works.Lay(Site(), "s", Ell, Works.Wall, Works.FewTowers, false);
        var many = Works.Lay(Site(), "s", Ell, Works.Wall, Works.ManyTowers, false);
        Assert.Equal(BuiltKinds.Works, many.Kind);
        Assert.Single(Of(many, PartKinds.Line, Works.Wall));
        Assert.Empty(Towers(none));
        Assert.Equal(3, Towers(few).Count);
        Assert.Contains(Towers(few), t => (t - Ell[1]).Length < 1e-3);
        Assert.True(Towers(many).Count > Towers(few).Count);
        var spaced = Towers(many).Where(t => Math.Abs(t.Y - 10) < 1e-6).Select(t => t.X).OrderBy(x => x).ToList();
        Assert.True(spaced.Zip(spaced.Skip(1)).All(pair => pair.Second - pair.First <= M(56)), "towers stand no more than 55 m apart");
        Assert.Empty(Towers(Works.Lay(Site(), "s", Ell, Works.Palisade, Works.ManyTowers, false)));
    }

    [Fact]
    public void A_road_through_a_wall_opens_a_gate_with_its_gatehouse_and_through_a_hedge_a_gap()
    {
        var road = Road(new Spot(15, 0), new Spot(15, 30));
        var wall = Works.Lay(Site(null, 0, road), "s", Ell, Works.Wall, Works.FewTowers, false);
        var gatehouse = Assert.Single(Of(wall, PartKinds.Block, "gatehouse"));
        Assert.Equal(15, BuiltSite.Round(gatehouse).At.X, 2);
        Assert.Equal(2, Of(wall, PartKinds.Line, Works.Wall).Count);
        Assert.True(Towers(wall).Count(t => Math.Abs(t.X - 15) < M(8) && Math.Abs(t.Y - 10) < 1e-3) == 2, "a tower stands on each side of the gate");
        var hedge = Works.Lay(Site(null, 0, road), "s", Ell, Works.Hedge, Works.ManyTowers, false);
        Assert.Equal(2, Of(hedge, PartKinds.Line, Works.Hedge).Count);
        Assert.Empty(Of(hedge, PartKinds.Block, "gatehouse"));
        Assert.Single(Of(Works.Lay(Site(null, 0, road), "s", Ell, Works.Trench, Works.NoTowers, false), PartKinds.Line, Works.Trench));
    }

    [Fact]
    public void A_wall_stops_at_water_and_goes_on_across_it()
    {
        var wall = Works.Lay(Site(River()), "s", [new Spot(20, 30), new Spot(40, 30)], Works.Wall, Works.NoTowers, false);
        var runs = Of(wall, PartKinds.Line, Works.Wall);
        Assert.Equal(2, runs.Count);
        Assert.True(Points(runs[0])[^1].X < 29.6 && Points(runs[1])[0].X > 30.4, "the gap covers the river");
    }

    [Fact]
    public void A_closed_wall_digs_its_ditch_outside()
    {
        var walled = Works.Lay(Site(), "s", Square, Works.Wall, Works.FewTowers, true);
        var ring = Square.Take(4).ToList();
        var ditches = Of(walled, PartKinds.Line, Works.Ditch);
        Assert.NotEmpty(ditches);
        Assert.All(ditches.SelectMany(Points), p => Assert.False(Enclosures.Inside(ring, p)));
        Assert.Empty(Of(Works.Lay(Site(), "s", Square, Works.Hedge, Works.FewTowers, true), PartKinds.Line, Works.Ditch));
    }

    [Fact]
    public void A_gate_opens_where_clicked_and_a_click_on_it_shuts_it()
    {
        var wall = Works.Lay(Site(), "s", Ell, Works.Wall, Works.FewTowers, false);
        var gated = Works.Gated(Site(), wall, new Spot(20, 15), M(20))!;
        Assert.Single(Of(gated, PartKinds.Block, "gatehouse"));
        Assert.Equal(2, Of(gated, PartKinds.Line, Works.Wall).Count);
        var shut = Works.Gated(Site(), gated, new Spot(20, 15.1), M(20))!;
        Assert.Empty(Of(shut, PartKinds.Block, "gatehouse"));
        Assert.Null(Works.Gated(Site(), wall, new Spot(40, 40), M(20)));
        var fence = Works.Gated(Site(), Works.Lay(Site(), "s", Ell, Works.Fence, Works.NoTowers, false), new Spot(15, 10), M(20))!;
        Assert.Single(Works.GatesOf(Site(), fence));
    }

    [Fact]
    public void Corners_are_moved_added_and_taken_out_and_gates_stay()
    {
        var road = Road(new Spot(15, 0), new Spot(15, 30));
        var wall = Works.Lay(Site(null, 0, road), "s", Ell, Works.Wall, Works.FewTowers, false);
        var added = Works.CornerAdded(Site(null, 0, road), wall, 1, new Spot(24, 15))!;
        Assert.Equal(4, Works.Corners(added).Count);
        Assert.Single(Of(added, PartKinds.Block, "gatehouse"));
        var moved = Works.CornerMoved(Site(null, 0, road), added, 3, new Spot(20, 25))!;
        Assert.Equal(new Spot(20, 25), Works.Corners(moved)[3]);
        var taken = Works.CornerRemoved(Site(null, 0, road), moved, 2)!;
        Assert.Equal(3, Works.Corners(taken).Count);
        Assert.Null(Works.CornerRemoved(Site(), Works.Lay(Site(), "s", [new Spot(1, 1), new Spot(5, 1)], Works.Wall, 0, false), 0));
        var ring = Works.Lay(Site(), "s", Square, Works.Wall, Works.FewTowers, false);
        Assert.Equal(4, Works.Corners(ring).Count);
        Assert.Equal(5, Works.Corners(Works.CornerAdded(Site(), ring, 3, new Spot(8, 15))!).Count);
    }

    [Fact]
    public void Laid_again_a_wall_keeps_its_gates_its_towers_by_hand_and_its_kind_changes()
    {
        var wall = Works.Lay(Site(), "s", Ell, Works.Wall, Works.NoTowers, false);
        var towered = Works.Towered(wall, new Spot(14, 10.02), M(20))!;
        Assert.Single(Towers(towered));
        var gated = Works.Gated(Site(), towered, new Spot(20, 15), M(20))!;
        var again = BuiltThings.Remade(Site(), gated, gated.Seed);
        Assert.Single(Of(again, PartKinds.Block, "gatehouse"));
        Assert.Contains(Towers(again), t => (t - new Spot(14, 10)).Length < 1e-3);
        Assert.Empty(Towers(Works.Towered(again, new Spot(14, 10), M(20))!));
        var palisade = Works.Reworked(Site(), again, Works.Palisade);
        Assert.Equal(Works.Palisade, Works.ClassOf(palisade));
        Assert.Equal(2, Of(palisade, PartKinds.Line, Works.Palisade).Count);
        Assert.Empty(Towers(palisade));
        var many = Works.Reworked(Site(), again, towers: Works.ManyTowers);
        Assert.True(Towers(many).Count > 2);
    }

    [Fact]
    public void Houses_make_way_for_a_wall_and_keep_out_of_it_when_the_town_is_laid_again()
    {
        var town = Settlements.Lay(Site(), "s", Middle, 5, 4, 0.8, 0, 0);
        var across = Works.Lay(Site(null, 0, town), "s", [new Spot(24, 30), new Spot(36, 30)], Works.Wall, Works.FewTowers, false);
        var line = new List<Spot> { new(24, 30), new(36, 30) };
        double half = M(3);
        bool Under(BuiltPart p) => p.Kind == PartKinds.Block && p.Class != "gatehouse" && Touches(p, line, half);
        Assert.Contains(town.Parts, Under);
        var cleared = Works.Cleared(town, across, Site().UnitsPerMetre)!;
        Assert.DoesNotContain(cleared.Parts, Under);
        var again = BuiltThings.Remade(Site(null, 0, across), town, town.Seed);
        Assert.DoesNotContain(again.Parts, Under);
    }

    [Fact]
    public void A_ring_drawn_round_a_town_becomes_its_wall_with_the_towers_and_ditch_asked()
    {
        var town = Settlements.Lay(Site(), "s", Middle, 4, 4, 0.8, 0, 0);
        Assert.Null(Walls.Of(town));
        var ring = new List<Spot> { new(24, 24), new(36, 24), new(36, 36), new(24, 36) };
        var walled = Walls.Ringed(Site(), town, ring, true, Works.FewTowers, false);
        var trace = Walls.Of(walled)!;
        Assert.True(trace.Masonry);
        Assert.Equal(Works.FewTowers, trace.Towers);
        Assert.False(trace.Ditch);
        Assert.True(walled.Setting(Settlements.Walls, 0) >= Settlements.WalledFrom);
        Assert.DoesNotContain(walled.Parts, p => p.Kind == PartKinds.Line && p.Class == "ditch" && Points(p).All(q => !Enclosures.Inside(ring, q)));
        Assert.NotNull(Walls.ByHand(walled));
        var again = BuiltThings.Remade(Site(), walled, walled.Seed);
        Assert.Equal(Works.FewTowers, Walls.Of(again)!.Towers);
        var palisade = Walls.Optioned(Site(), again, false, Works.NoTowers, false)!;
        Assert.False(Walls.Of(palisade)!.Masonry);
    }

    [Fact]
    public void A_wall_from_the_town_wall_and_back_takes_in_the_ground_between_and_leaves_the_old_stretch()
    {
        var town = Walls.Ringed(Site(), Settlements.Lay(Site(), "s", Middle, 4, 4, 0.8, 0, 0), [new(24, 24), new(36, 24), new(36, 36), new(24, 36)], true, Works.FewTowers, false);
        var loop = new List<Spot> { new(36, 27), new(42, 27), new(42, 33), new(36, 33) };
        var (grown, old) = Walls.Spliced(Site(), town, loop, M(30))!.Value;
        var corners = Walls.Of(grown)!.Corners;
        Assert.Contains(corners, c => (c - new Spot(42, 27)).Length < 0.5);
        Assert.True(Enclosures.Inside(corners, new Spot(39, 30)), "the new ground is inside the town wall");
        Assert.True(old.Count >= 2);
        Assert.All(old, p => Assert.Equal(36, p.X, 1));
        Assert.Null(Walls.Spliced(Site(), town, [new(36, 27), new(30, 30), new(36, 33)], M(30)));
    }

    [Fact]
    public void A_town_wall_takes_a_corner_added_and_gives_one_up_and_a_tower_is_set_and_taken_down()
    {
        var town = Walls.Ringed(Site(), Settlements.Lay(Site(), "s", Middle, 4, 4, 0.8, 0, 0), [new(24, 24), new(36, 24), new(36, 36), new(24, 36)], true, Works.NoTowers, false);
        var added = Walls.CornerAdded(Site(), town, 1, new Spot(38, 30))!;
        Assert.Equal(Walls.Of(town)!.Corners.Count + 1, Walls.Of(added)!.Corners.Count);
        var taken = Walls.CornerRemoved(Site(), added, 2)!;
        Assert.Equal(Walls.Of(town)!.Corners.Count, Walls.Of(taken)!.Corners.Count);
        var towered = Walls.Towered(taken, new Spot(30, 24.05), M(20))!;
        Assert.Single(Towers(towered), t => (t - new Spot(30, 24)).Length < M(5));
        Assert.DoesNotContain(Towers(Walls.Towered(towered, new Spot(30, 24), M(20))!), t => (t - new Spot(30, 24)).Length < M(5));
    }

    [Fact]
    public void A_castle_is_laid_in_the_plan_asked_and_keeps_it_when_laid_again()
    {
        int motte = Castles.Plans.ToList().IndexOf("motte");
        var site = Site(null, Manners.Gothic);
        Assert.Contains("motte", Castles.PlansOf(Manners.Gothic));
        Assert.Single(Castles.PlansOf(Manners.Rus));
        var castle = Defences.Lay(site, "s", Defences.Castle, Middle, M(60), 7, 0, 0, 0, plan: motte);
        Assert.Equal(motte, (int)castle.Setting("plan", -1));
        var again = BuiltThings.Remade(site, castle, 99);
        Assert.Equal(motte, (int)again.Setting("plan", -1));
    }
}
