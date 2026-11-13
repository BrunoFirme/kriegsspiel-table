using KTable.Core.Model;

namespace KTable.Core.Tests;

public class BuiltSculptTests
{
    const double Side = 60, MetresToTheUnit = 50;
    static readonly Spot Middle = new(30, 30);

    static BuiltSite Site(params BuiltThing[] others) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = others };

    static List<Spot> Points(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    static Spot Centre(BuiltPart part) => new(Points(part).Average(p => p.X), Points(part).Average(p => p.Y));

    static List<BuiltPart> Of(BuiltThing t, string kind, string cls) => t.Parts.Where(p => p.Kind == kind && p.Class == cls).ToList();

    static BuiltThing Town(double houses = 0.8, double walls = 0.5) => Settlements.Lay(Site(), "s", Middle, 5, 4, houses, walls, 0);

    [Fact]
    public void A_place_kept_whole_is_left_alone_when_laid_again_and_a_kept_part_stays_where_it_is()
    {
        var village = Town(0.4, 0);
        var whole = BuiltThings.WithKept(village, -1, true);
        Assert.True(BuiltThings.Fixed(whole) && !BuiltThings.Fixed(village));
        Assert.Equal(Json.Serialize(whole), Json.Serialize(BuiltThings.Remade(Site(), whole, 99)));
        Assert.False(BuiltThings.Fixed(BuiltThings.WithKept(whole, -1, false)));
        int house = village.Parts.FindIndex(p => p.Class == "house");
        var one = BuiltThings.WithKept(village, house, true);
        var again = BuiltThings.Remade(Site(), one, 99);
        Assert.Contains(again.Parts, p => p.Kept && p.Class == "house" && p.At.SequenceEqual(village.Parts[house].At));
        Assert.DoesNotContain(BuiltThings.Remade(Site(), BuiltThings.WithKept(one, house, false), 99).Parts, p => p.Kept);
    }

    [Fact]
    public void Laid_again_no_house_stands_on_a_thing_kept_by_hand()
    {
        var village = Town(0.6, 0);
        var house = Of(village, PartKinds.Block, "house")[0];
        var barn = BuiltThings.Made(Site(), "barn", Centre(house))[0];
        var again = BuiltThings.Remade(Site(), BuiltThings.WithPart(village, barn), village.Seed);
        Assert.Contains(again.Parts, p => p.Kept && p.Class == "barn");
        Assert.DoesNotContain(again.Parts, p => !p.Kept && p.Kind == PartKinds.Block && BuiltThings.Reach(barn, Centre(p)) <= 0);
        Assert.Equal(Of(village, PartKinds.Block, "house").Count - 1, Of(again, PartKinds.Block, "house").Count, 3.0);
    }

    [Fact]
    public void Thinning_takes_houses_under_the_brush_leaves_gardens_and_they_stay_gardens_when_laid_again()
    {
        var town = Town(0.8, 0);
        const double Brush = 1.6;
        int Houses(BuiltThing t, bool under) => Of(t, PartKinds.Block, "house").Count(p => (Centre(p) - Middle).Length <= Brush == under);
        var thinned = BuiltThings.Remade(Site(), town, town.Seed) is var same && Brushed.Thinned(Site(), same, [Middle], Brush, 5) is { } done ? done : null;
        Assert.NotNull(thinned);
        int gone = Houses(town, true) - Houses(thinned!, true);
        Assert.True(gone >= 1 && Houses(thinned!, true) >= 1, $"{Houses(town, true)} under the brush, {gone} gone");
        Assert.Equal(Houses(town, false), Houses(thinned!, false));
        Assert.Equal(gone, thinned!.Parts.Count(p => p.Kept && p.Class == "garden"));
        Assert.Null(Brushed.Thinned(Site(), town, [new Spot(3, 3)], Brush, 5));
        var again = BuiltThings.Remade(Site(), thinned, town.Seed);
        Assert.Equal(Houses(thinned, true), Houses(again, true));
    }

    [Fact]
    public void Filling_fronts_the_streets_under_the_brush_with_more_houses_that_are_kept()
    {
        var village = Town(0.25, 0);
        var street = Points(Of(village, PartKinds.Line, "street")[0]);
        Spot[] stroke = [street[0] + (street[^1] - street[0]) * 0.3, street[0] + (street[^1] - street[0]) * 0.7];
        const double Brush = 1;
        var filled = Brushed.Filled(Site(), village, stroke, Brush, 3);
        Assert.NotNull(filled);
        int before = Of(village, PartKinds.Block, "house").Count, after = Of(filled!, PartKinds.Block, "house").Count;
        Assert.True(after >= before + 4, $"{before} houses, then {after}");
        var made = filled!.Parts.Where(p => p.Kept && p.Class == "house").ToList();
        Assert.Equal(after - before, made.Count);
        Assert.All(made, p => Assert.True(Centre(p).FromSegment(stroke[0], stroke[1]) < Brush + 0.6));
        Assert.Null(Brushed.Filled(Site(), village, [new Spot(3, 3)], Brush, 3));
        var again = BuiltThings.Remade(Site(), filled, village.Seed);
        Assert.True(Of(again, PartKinds.Block, "house").Count >= after - 5, $"{after} houses, laid again {Of(again, PartKinds.Block, "house").Count}");
    }

    [Fact]
    public void A_town_wall_has_a_gatehouse_at_each_gate_and_a_village_palisade_has_none()
    {
        var town = Town();
        var trace = Walls.Of(town);
        Assert.NotNull(trace);
        Assert.True(trace!.Masonry && trace.Corners.Count >= 4 && trace.Gates.Count >= 1);
        var gatehouses = Of(town, PartKinds.Block, "gatehouse");
        Assert.Equal(trace.Gates.Count, gatehouses.Count);
        Assert.All(trace.Gates, gate => Assert.Contains(gatehouses, g => (Centre(g) - gate).Length < 0.05));
        Assert.Null(Walls.ByHand(town));
        var village = Settlements.Lay(Site(), "s", Middle, 2.4, 4, 0.3, 0.5, 0);
        Assert.NotEmpty(Of(village, PartKinds.Line, "palisade"));
        Assert.Empty(Of(village, PartKinds.Block, "gatehouse"));
        Assert.False(Walls.Of(village)!.Masonry);
        var road = BuiltRoads.Lay(Site(town), "s", [Middle, trace.Gates[0] + (trace.Gates[0] - Middle).Unit * 3], "road", false);
        var cleared = BuiltRoads.MadeWay(town, road, 1 / MetresToTheUnit) ?? town;
        Assert.Equal(gatehouses.Count, Of(cleared, PartKinds.Block, "gatehouse").Count);
    }

    [Fact]
    public void A_wall_corner_moved_by_hand_moves_the_wall_and_the_town_is_laid_again_inside_it()
    {
        var town = Town();
        var trace = Walls.Of(town)!;
        var middle = Enclosures.Middle(trace.Corners);
        int corner = Enumerable.Range(0, trace.Corners.Count).MaxBy(i => trace.Gates.Min(g => (g - trace.Corners[i]).Length));
        var to = trace.Corners[corner] + (trace.Corners[corner] - middle).Unit * 1.2;
        var moved = Walls.CornerMoved(Site(), town, corner, to);
        Assert.NotNull(moved);
        var hand = Walls.ByHand(moved!);
        Assert.NotNull(hand);
        Assert.Contains(hand!.Corners, c => (c - to).Length < 0.01);
        Assert.Equal(Of(town, PartKinds.Block, "house").Count, Of(moved!, PartKinds.Block, "house").Count);
        Assert.Equal(trace.Gates.Count, hand.Gates.Count);
        Assert.Equal(hand.Gates.Count, Of(moved!, PartKinds.Block, "gatehouse").Count);

        var denser = moved!.Clone();
        denser.Settings[Settlements.Houses] = 0.9;
        foreach (var again in new[] { BuiltThings.Remade(Site(), denser, town.Seed), BuiltThings.Remade(Site(), moved, 77) })
        {
            var ring = Walls.ByHand(again);
            Assert.NotNull(ring);
            Assert.Contains(ring!.Corners, c => (c - to).Length < 0.01);
            Assert.All(Of(again, PartKinds.Block, "house"), house => Assert.True(Enclosures.Inside(ring.Corners, Centre(house))));
            Assert.Equal(ring.Gates.Count, Of(again, PartKinds.Block, "gatehouse").Count);
        }
        var open = moved.Clone();
        open.Settings[Settlements.Walls] = 0;
        Assert.Null(Walls.Of(BuiltThings.Remade(Site(), open, town.Seed)));
    }

    [Fact]
    public void A_click_on_the_wall_opens_a_gate_and_a_click_on_that_gate_closes_it()
    {
        var town = Town();
        var trace = Walls.Of(town)!;
        var spot = trace.Corners.MaxBy(c => trace.Gates.Min(g => (g - c).Length));
        Assert.True(trace.Gates.Min(g => (g - spot).Length) > 0.6, "a stretch of wall far from every gate");
        Assert.Null(Walls.Gated(Site(), town, Middle, 0.2));
        var gated = Walls.Gated(Site(), town, spot, 0.2);
        Assert.NotNull(gated);
        Assert.Equal(trace.Gates.Count + 1, Walls.Of(gated!)!.Gates.Count);
        Assert.Equal(trace.Gates.Count + 1, Of(gated!, PartKinds.Block, "gatehouse").Count);
        Assert.Contains(Of(gated!, PartKinds.Block, "gatehouse"), g => (Centre(g) - spot).Length < 0.05);
        var shut = Walls.Gated(Site(), gated!, spot, 0.2);
        Assert.Equal(trace.Gates.Count, Walls.Of(shut!)!.Gates.Count);
        Assert.Equal(Of(town, PartKinds.Sign, "tower").Count, Of(shut!, PartKinds.Sign, "tower").Count, 3.0);
    }

    [Fact]
    public void A_town_square_has_buildings_on_every_side()
    {
        var town = Settlements.Lay(Site(), "s", Middle, 6, 4, 0.8, 0, 0);
        var square = Points(Assert.Single(Of(town, PartKinds.Area, "square")));
        var blocks = town.Parts.Where(p => p.Kind == PartKinds.Block).Select(Centre).ToList();
        for (int i = 0; i < 4; i++)
        {
            Spot a = square[i], b = square[(i + 1) % 4], out_ = ((a + b) * 0.5 - Centre(Of(town, PartKinds.Area, "square")[0])).Unit;
            int fronting = blocks.Count(c => c.FromSegment(a, b) < 26 / MetresToTheUnit && (c - a).Dot(out_) > 0);
            Assert.True(fronting >= 1, $"side {i} has {fronting} buildings");
        }
    }

    [Fact]
    public void A_road_laid_through_a_town_wall_opens_a_gate_where_it_passes_and_the_wall_stays_the_generator_s()
    {
        var town = Town();
        var trace = Walls.Of(town)!;
        var middle = Enclosures.Middle(trace.Corners);
        var corner = trace.Corners.MaxBy(c => trace.Gates.Min(g => (g - c).Length));
        var road = BuiltRoads.Lay(Site(town), "s", [middle, middle + (corner - middle) * 1.6], "road", false);
        var opened = Walls.Opened(Site(), town, road);
        Assert.NotNull(opened);
        var after = Walls.Of(opened!)!;
        Assert.Equal(trace.Gates.Count + 1, after.Gates.Count);
        Assert.Contains(after.Gates, g => g.FromSegment(middle, middle + (corner - middle) * 1.6) < 0.25 && trace.Gates.All(old => (old - g).Length > 0.2));
        Assert.Null(Walls.ByHand(opened!));
        Assert.Null(Walls.Opened(Site(), town, BuiltRoads.Lay(Site(town), "s", [new Spot(2, 2), new Spot(8, 3)], "road", false)));
        var moved = Walls.CornerMoved(Site(), town, 0, trace.Corners[0] + (trace.Corners[0] - middle).Unit * 0.5)!;
        Assert.NotNull(Walls.ByHand(Walls.Opened(Site(), moved, road) ?? moved));
    }
}
