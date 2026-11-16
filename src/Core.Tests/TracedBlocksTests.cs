using KTable.Core.Model;

namespace KTable.Core.Tests;

public class TracedBlocksTests
{
    const double Side = 60, MetresToTheUnit = 25, Reach = 10, InnerRow = 1.2;
    static readonly Spot Middle = new(30, 30);
    static readonly string[] Dwellings = ["house", "townhouse", "cottage", "shop", "insula", "domus", "taberna", "villa"];

    static readonly List<Spot> NorthBlock = [new(22, 31), new(29, 31), new(29, 37), new(22, 37)];
    static readonly List<Spot> SouthBlock = [new(31, 23), new(38, 23), new(38, 29), new(31, 29)];
    static readonly List<Spot> AwayBlock = [new(33, 33), new(37, 33), new(37, 37), new(33, 37)];

    static BuiltSite Site(int manner = Manners.Roman) =>
        new()
        {
            Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = 1, Manner = manner,
            Others = [Road(new(18, 30), new(42, 30)), Road(new(30, 18), new(30, 42))],
        };

    static BuiltThing Road(Spot a, Spot b) => new() { Kind = BuiltKinds.Road, Parts = { BuiltSite.Line("street", [a, b]) } };

    static BuiltThing Town(BuiltSite site, int seed = 5) =>
        Settlements.Lay(site, "s", Middle, Reach, seed, 0.9, 0, Plans.Value(Plans.Grid), false, null, new Settlements.Shape());

    static List<Spot> Points(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    static List<Spot> Homes(BuiltThing town) =>
        town.Parts.Where(p => p.Kind == PartKinds.Block && !p.Kept && Dwellings.Contains(p.Class)).Select(p => BuiltSite.Round(p).At).ToList();

    static BuiltThing Blocked(BuiltSite site, params List<Spot>[] blocks) => blocks.Aggregate(Town(site), (town, block) => Traces.WithBlock(site, town, block));

    static List<Spot> StreetPoints(BuiltThing town) =>
        town.Parts.Where(p => p.Kind == PartKinds.Line && p.Class is "street" or "lane" or "road" or "track").Select(Points)
            .SelectMany(line => line.Zip(line.Skip(1)).SelectMany(leg => Enumerable.Range(1, 9).Select(i => leg.First + (leg.Second - leg.First) * (i / 10.0)))).ToList();

    [Fact]
    public void A_drawn_block_is_lined_with_houses_and_the_town_s_own_streets_and_houses_keep_out_of_it()
    {
        var site = Site();
        var blocks = new[] { NorthBlock, SouthBlock, AwayBlock };
        var town = Blocked(site, blocks);
        Assert.Equal(3, Traces.Blocks(town).Count);
        var homes = Homes(town);
        foreach (var block in blocks)
            Assert.True(homes.Count(h => Enclosures.Inside(block, h)) >= 3, $"the block at {block[0]} is filled: {homes.Count(h => Enclosures.Inside(block, h))} houses");
        Assert.True(homes.Count(h => !blocks.Any(b => Enclosures.Inside(b, h))) > 10, "the rest of the town is still generated");
        double Deep(List<Spot> block, Spot h) => Enumerable.Range(0, block.Count).Min(i => h.FromSegment(block[i], block[(i + 1) % block.Count]));
        Assert.True(homes.Any(h => Enclosures.Inside(NorthBlock, h) && Deep(NorthBlock, h) > InnerRow), "a block of a close-built town is filled row behind row, not only along its edges");
        Assert.DoesNotContain(StreetPoints(town), p => blocks.Any(b => Enclosures.Inside(b, p)));
    }

    [Fact]
    public void Traced_blocks_stay_with_their_town_when_it_is_laid_again_with_another_seed()
    {
        var site = Site();
        var again = BuiltThings.Remade(site, Blocked(site, NorthBlock), 17);
        Assert.Single(Traces.Blocks(again));
        Assert.True(Homes(again).Count(h => Enclosures.Inside(NorthBlock, h)) >= 3);
        Assert.DoesNotContain(StreetPoints(again), p => Enclosures.Inside(NorthBlock, p));
    }

    [Fact]
    public void A_drawn_building_joins_its_town_clears_what_stood_under_it_and_takes_its_true_length_and_width()
    {
        var site = Site();
        var town = Town(site);
        var temple = Traces.Thing(site, "church", new Spot(34, 26), new Spot(1, 0), 6);
        Assert.Equal(("church", 6.0), (temple[0].Class, Math.Round(BuiltThings.Sides(temple[0]).Long, 3)));
        var stood = Traces.Stood(town, temple);
        int part = stood.Parts.FindIndex(p => p.Class == "church" && p.Kept);
        var stretched = BuiltThings.WithPartStretched(stood, part, 6, 4);
        var corners = Points(stretched.Parts[part]);
        Assert.Equal((6.0, 4.0), (Math.Round(BuiltThings.Sides(stretched.Parts[part]).Long, 2), Math.Round(BuiltThings.Sides(stretched.Parts[part]).Deep, 2)));
        Assert.DoesNotContain(Homes(stood), h => Enclosures.Inside(Points(stood.Parts[part]), h));
        var again = BuiltThings.Remade(site, stretched, 23);
        Assert.Contains(again.Parts, p => p.Kept && p.Class == "church");
        Assert.DoesNotContain(Homes(again), h => Enclosures.Inside(corners, h));
    }

    [Fact]
    public void An_amphitheatre_is_an_oval_and_a_circus_a_track_with_one_rounded_end_both_the_length_of_the_drag()
    {
        var site = Site();
        var arena = Traces.Thing(site, "amphitheatre", new Spot(14, 12.5), new Spot(1, 0), 8).Single();
        Assert.Equal((PartKinds.Area, Traces.Arena), (arena.Kind, arena.Class));
        Assert.All(Points(arena), p => Assert.True(p.X >= 10 - 1e-3 && p.X <= 18 + 1e-3 && Math.Abs(p.Y - 12.5) <= 4));
        var circus = Traces.Thing(site, "circus", new Spot(14, 12.5), new Spot(1, 0), 8).Single();
        var ring = Points(circus);
        Assert.Equal(Traces.Circus, circus.Class);
        Assert.All(ring, p => Assert.True(p.X >= 10 - 1e-3 && p.X <= 18 + 1e-3 && Math.Abs(p.Y - 12.5) <= 4));
        Assert.Equal(18, ring.Max(p => p.X), 2);
        Assert.Single(ring, p => Math.Abs(p.X - 18) < 1e-3);
    }

    [Fact]
    public void A_colonnade_runs_the_length_of_the_drag_and_a_statue_stands_turned_along_it()
    {
        var site = Site();
        var colonnade = Traces.Thing(site, "colonnade", new Spot(10, 5), new Spot(1, 0), 10).Single();
        Assert.Equal((PartKinds.Line, 2, 10.0), (colonnade.Kind, Points(colonnade).Count, Math.Round((Points(colonnade)[1] - Points(colonnade)[0]).Length, 3)));
        var statue = Traces.Thing(site, "statue", new Spot(10, 5), new Spot(0, 1), 3).Single();
        Assert.Equal((PartKinds.Sign, "statue"), (statue.Kind, statue.Class));
        Assert.NotEqual(0, statue.Turn);
    }

    [Fact]
    public void The_ancient_monuments_are_offered_on_an_ancient_map_only_and_the_old_grounds_keep_their_corners()
    {
        string[] monuments = ["rotunda", "mausoleum", "arch", "amphitheatre", "circus", "colonnade"];
        var ancient = Catalogue.For(true).SelectMany(s => s.Entries).Select(e => e.Id).ToList();
        var later = Catalogue.For(false).SelectMany(s => s.Entries).Select(e => e.Id).ToList();
        Assert.All(monuments, m => Assert.Contains(m, ancient));
        Assert.All(monuments, m => Assert.DoesNotContain(m, later));
        var site = new BuiltSite { Width = 1, Height = 1, UnitsPerMetre = 1 / 72.0, Manner = Manners.Roman };
        var theatre = Points(BuiltThings.Made(site, "theatre", new Spot(0.5, 0.5)).Single());
        double l = 64 / 72.0 / 2, d = 32 / 72.0 / 2;
        Spot[] old = [new(0.5 - l, 0.5 - d), new(0.5 + l, 0.5 - d), new(0.5 + l, 0.5 + d), new(0.5 - l, 0.5 + d)];
        Assert.Equal(old.Select(p => p.Stored), theatre.Select(p => p.Stored));
    }
}
