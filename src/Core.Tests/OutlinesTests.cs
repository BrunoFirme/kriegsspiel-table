using KTable.Core.Model;

namespace KTable.Core.Tests;

public class OutlinesTests
{
    const double Side = 60, MetresToTheUnit = 25, Brush = 3;
    static readonly List<Spot> Snake = Enumerable.Range(0, 31).Select(i => new Spot(12 + i * 1.2, 30 + Math.Sin(i / 30.0 * Math.PI * 2) * 6)).ToList();

    static BuiltSite Site(int manner = Manners.Gothic) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = 1, Manner = manner };

    static List<Spot> Homes(BuiltThing town) => town.Parts.Where(p => p.Kind == PartKinds.Block).Select(p => BuiltSite.Round(p).At).ToList();

    static BuiltThing Town(List<Spot> ring, double walls = 0, int seed = 3) => Settlements.Lay(Site(), "s", ring, seed, 0.8, walls, Plans.Value(Plans.Grown));

    [Fact]
    public void A_brush_stroke_gives_one_closed_outline_that_holds_the_stroke_and_little_else()
    {
        var ring = Outlines.Around(Snake, Brush)!;
        Assert.InRange(ring.Count, 8, 36);
        Assert.All(Snake, p => Assert.True(Outlines.Depth(ring, p) > Brush * 0.6));
        Assert.True(Outlines.Depth(ring, new Spot(30, 30 + Brush * 2.5)) < 0);
        Assert.False(Outlines.Inside(ring, new Spot(21, 24)));
        var frame = Outlines.Framed(ring);
        Assert.True(Outlines.Inside(ring, frame.Centre));
        Assert.InRange(frame.Radius, 15, 26);
        Assert.InRange(frame.Size, 5, 12);
    }

    [Fact]
    public void Growing_adds_the_ground_brushed_and_shrinking_gives_it_up()
    {
        var round = Outlines.Circle(new Spot(30, 30), 8);
        var arm = new List<Spot> { new(36, 30), new(48, 30) };
        var grown = Outlines.Grown(round, arm, Brush)!;
        Assert.True(Outlines.Inside(grown, new Spot(47, 30)) && Outlines.Inside(grown, new Spot(24, 30)));
        Assert.False(Outlines.Inside(grown, new Spot(47, 36)));
        var shrunk = Outlines.Shrunk(grown, arm, Brush * 1.2)!;
        Assert.False(Outlines.Inside(shrunk, new Spot(46, 30)));
        Assert.True(Outlines.Inside(shrunk, new Spot(26, 30)));
        Assert.Null(Outlines.Shrunk(round, [new(30, 30)], 20));
    }

    [Fact]
    public void A_town_laid_in_an_outline_fills_it_to_its_ends_and_stays_inside()
    {
        var ring = Outlines.Around(Snake, Brush)!;
        var town = Town(ring);
        var homes = Homes(town);
        Assert.True(homes.Count > 60, $"{homes.Count} buildings");
        Assert.All(homes, p => Assert.True(Outlines.Inside(ring, p)));
        Assert.Contains(homes, p => (p - Snake[0]).Length < Brush * 1.5);
        Assert.Contains(homes, p => (p - Snake[^1]).Length < Brush * 1.5);
        Assert.Equal(ring.Count, Outlines.Of(town)!.Count);
        Assert.Contains(town.Parts, p => p.Class == BuiltThings.Quarter && p.At.Count == ring.Count * 2);
        foreach (int seed in Enumerable.Range(4, 8))
            foreach (var way in Town(ring, 0, seed).Parts.Where(BuiltSite.IsRoad).Select(p => BuiltGeometry.Points(p.At).Select(Spot.Of).ToList()))
                Assert.All(way.Zip(way.Skip(1)), leg => Assert.True(Outlines.Depth(ring, (leg.First + leg.Second) * 0.5) > -1, $"seed {seed}: a street leaves the outline"));
    }

    [Fact]
    public void Its_wall_runs_on_the_outline_and_laying_it_again_keeps_the_outline()
    {
        var ring = Outlines.Around(Snake, Brush * 1.4)!;
        var town = Town(ring, (Settlements.WalledFrom + Settlements.BastionedFrom) / 2);
        var wall = Walls.Of(town);
        Assert.NotNull(wall);
        Assert.True(wall!.Corners.Count(c => Math.Abs(Outlines.Depth(ring, c)) < 0.3) >= wall.Corners.Count * 0.75, "the wall stands on the outline, but for a castle's own");
        Assert.True(Homes(town).Count(p => Outlines.Inside(ring, p)) > Homes(town).Count * 0.85, "but for a suburb before a gate, the town is within its wall");
        var again = BuiltThings.Remade(Site(), town, 9);
        Assert.Equal(town.Outline, again.Outline);
        Assert.True(Homes(again).Count(p => Outlines.Inside(ring, p)) > Homes(again).Count * 0.85);
        Assert.NotEqual(Homes(town).Count * 1000 + town.Parts.Count, Homes(again).Count * 1000 + again.Parts.Count);
    }

    [Fact]
    public void A_plan_that_needs_a_middle_and_a_ring_falls_back_inside_an_outline_and_a_round_town_has_no_outline()
    {
        var ring = Outlines.Around(Snake, Brush)!;
        var wards = Settlements.Lay(Site(Manners.Chinese), "s", ring, 3, 0.8, 0, Plans.Value(Plans.Wards));
        Assert.True(Homes(wards).Count > 30);
        var round = Settlements.Lay(Site(), "s", new Spot(30, 30), 8, 3, 0.8, 0, Plans.Value(Plans.Grown));
        Assert.Null(Outlines.Of(round));
        Assert.Equal(4, round.Outline.Count);
    }
}
