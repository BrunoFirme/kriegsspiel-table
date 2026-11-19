using KTable.Core.Model;

namespace KTable.Core.Tests;

public class OutlinesTests
{
    const double Side = 60, MetresToTheUnit = 25, Brush = 3;
    static readonly List<Spot> Snake = Enumerable.Range(0, 31).Select(i => new Spot(12 + i * 1.2, 30 + Math.Sin(i / 30.0 * Math.PI * 2) * 6)).ToList();

    const double BrookFrom = 29, BrookTo = 30.2, MarshBand = 1, HomeReach = 1.6, Sampled = 0.5, DeepIn = 1;
    static readonly int[] Seeds = [3, 4, 5, 6, 7, 8];

    static BuiltSite Site(int manner = Manners.Gothic, ReliefGrid? grid = null, List<BuiltThing>? others = null) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = grid ?? ReliefGrid.Flat(Side, Side), HeightUnit = 1, Manner = manner, Others = others ?? [] };

    static List<Spot> Homes(BuiltThing town) => town.Parts.Where(p => p.Kind == PartKinds.Block).Select(p => BuiltSite.Round(p).At).ToList();

    static BuiltThing Town(List<Spot> ring, double walls = 0, int seed = 3, BuiltSite? site = null) => Settlements.Lay(site ?? Site(), "s", ring, seed, 0.8, walls, Plans.Value(Plans.Grown));

    static List<Spot> Stroke(params Spot[] corners) =>
        [.. corners.Zip(corners.Skip(1)).SelectMany(leg => Enumerable.Range(0, 20).Select(k => leg.First + (leg.Second - leg.First) * (k / 20.0))), corners[^1]];

    static double Empty(BuiltSite site, List<Spot> ring, BuiltThing town)
    {
        var homes = Homes(town);
        int ground = 0, empty = 0;
        for (double x = 0; x < Side; x += Sampled)
            for (double y = 0; y < Side; y += Sampled)
            {
                var p = new Spot(x, y);
                if (Outlines.Depth(ring, p) < DeepIn || !site.Buildable(p)) continue;
                ground++;
                if (!homes.Any(h => (h - p).Length < HomeReach)) empty++;
            }
        return empty / (double)Math.Max(1, ground);
    }

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
    public void A_town_painted_in_a_bent_stroke_builds_on_all_its_ground()
    {
        var ring = Outlines.Around(Stroke(new(10, 8), new(10, 50), new(50, 50)), Brush)!;
        double empty = Seeds.Average(seed => Empty(Site(), ring, Town(ring, 0, seed)));
        Assert.True(empty < 0.06, $"{empty:P0} of the painted ground is empty");
    }

    [Fact]
    public void A_town_painted_across_a_brook_in_marsh_builds_on_both_banks()
    {
        var grid = ReliefGrid.Flat(Side, Side);
        int Row(double y) => (int)Math.Round(y / Side * (grid.Rows - 1));
        for (int y = Row(BrookFrom - MarshBand); y <= Row(BrookTo + MarshBand); y++)
            for (int x = 0; x < grid.Cols; x++)
                grid.Kind[y * grid.Cols + x] = y >= Row(BrookFrom) && y <= Row(BrookTo) ? GroundKinds.Water : GroundKinds.Marsh;
        var site = Site(grid: grid);
        var ring = Outlines.Around(Stroke(new(12, 20), new(48, 20), new(48, 26), new(12, 26), new(12, 33), new(48, 33), new(48, 39), new(12, 39)), Brush)!;
        foreach (int seed in Seeds)
        {
            var town = Town(ring, 0, seed, site);
            var homes = Homes(town);
            int below = homes.Count(h => h.Y < BrookFrom), above = homes.Count(h => h.Y > BrookTo);
            Assert.True(Math.Min(below, above) > homes.Count / 4, $"seed {seed}: {below} homes on one bank, {above} on the other");
            Assert.True(Empty(site, ring, town) < 0.12, $"seed {seed}: {Empty(site, ring, town):P0} of the painted ground is empty");
        }
    }

    [Fact]
    public void A_stroke_painted_in_the_bend_of_a_named_town_builds_on_its_own_ground()
    {
        var bend = Outlines.Around(Stroke(new(10, 15), new(30, 40), new(50, 15)), Brush)!;
        var inner = Outlines.Around(Stroke(new(22, 20), new(38, 20)), Brush)!;
        foreach (int seed in Seeds)
        {
            var named = Town(bend, 0, seed);
            named.Name = "Bend";
            var site = Site(others: [named]);
            var town = Town(inner, 0, seed + 1, site);
            Assert.True(Empty(site, inner, town) < 0.25, $"seed {seed}: {Empty(site, inner, town):P0} of the painted ground is empty");
            Assert.DoesNotContain(Homes(town), h => Outlines.Depth(bend, h) > DeepIn);
        }
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
