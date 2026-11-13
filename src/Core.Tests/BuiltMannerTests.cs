using KTable.Core.Model;

namespace KTable.Core.Tests;

public class BuiltMannerTests
{
    const double Side = 60, MetresToTheUnit = 50;
    static readonly Spot Middle = new(30, 30);

    static BuiltSite Site(int manner, params BuiltThing[] others) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = others, Manner = manner };

    static int Count(BuiltThing t, string kind, string cls) => t.Parts.Count(p => p.Kind == kind && p.Class == cls);

    static BuiltThing Town(int manner, int seed = 4, double houses = 0.7, double walls = 0, double radius = 6) => Settlements.Lay(Site(manner), "s", Middle, radius, seed, houses, walls, 0);

    [Fact]
    public void A_place_keeps_the_manner_it_was_laid_in_and_an_ancient_map_takes_only_ancient_manners()
    {
        foreach (int manner in Enumerable.Range(0, Manners.All.Count))
        {
            var town = Town(manner);
            Assert.Equal(manner, Manners.Of(town));
            Assert.Equal(manner, Manners.Of(BuiltThings.Remade(Site(Manners.Continental), town, 9)));
        }
        Assert.Equal(Manners.Roman, Manners.For(true, Manners.English));
        Assert.Equal(Manners.Continental, Manners.For(false, Manners.Egyptian));
        Assert.Equal(Manners.Tribal, Manners.For(true, Manners.Tribal));
        Assert.Equal([Manners.Roman, Manners.Egyptian, Manners.Tribal], Manners.Of(true));
        var old = new BuiltThing { Settings = { [Settlements.Old] = 1 } };
        Assert.Equal(Manners.Roman, Manners.Of(old));
    }

    [Fact]
    public void A_roman_town_has_atrium_houses_blocks_a_forum_a_temple_with_its_altar_and_no_steeple()
    {
        var town = Town(Manners.Roman);
        Assert.True(Count(town, PartKinds.Block, "domus") >= 3 && Count(town, PartKinds.Block, "insula") >= 2, $"{Count(town, PartKinds.Block, "domus")} domus, {Count(town, PartKinds.Block, "insula")} insulae");
        Assert.Equal(1, Count(town, PartKinds.Area, "forum"));
        Assert.Equal(0, Count(town, PartKinds.Area, "square"));
        Assert.Equal((1, 0, 1), (Count(town, PartKinds.Block, "church"), Count(town, PartKinds.Block, "steeple"), Count(town, PartKinds.Sign, "altar")));
        Assert.Equal(1, Count(town, PartKinds.Block, "basilica"));
        Assert.Equal(0, Count(town, PartKinds.Sign, "cross"));
        Assert.Contains(Enumerable.Range(1, 6), seed => Count(Town(Manners.Roman, seed), PartKinds.Block, "baths") == 1);
        Assert.Contains(Enumerable.Range(1, 6), seed => Count(Town(Manners.Roman, seed), PartKinds.Area, "arena") == 1);
    }

    [Fact]
    public void An_egyptian_place_has_obelisks_and_grain_bins_and_a_tribal_one_round_houses_on_a_green()
    {
        var egyptian = Town(Manners.Egyptian);
        Assert.Equal((2, 0), (Count(egyptian, PartKinds.Sign, "obelisk"), Count(egyptian, PartKinds.Block, "steeple")));
        Assert.True(Count(egyptian, PartKinds.Block, "silo") >= 4);
        Assert.Equal(0, Count(egyptian, PartKinds.Block, "pigsty"));
        var tribal = Town(Manners.Tribal);
        Assert.True(Count(tribal, PartKinds.Block, "roundhouse") > 20 && Count(tribal, PartKinds.Block, "house") == 0);
        Assert.Equal((1, 0), (Count(tribal, PartKinds.Area, "green"), Count(tribal, PartKinds.Area, "square")));
        Assert.Equal(0, Count(tribal, PartKinds.Block, "inn"));
    }

    [Fact]
    public void A_town_of_later_times_has_its_steeple_its_trades_and_shops_and_a_bastioned_one_its_garrison()
    {
        var english = Town(Manners.English);
        Assert.Equal((1, 1), (Count(english, PartKinds.Block, "steeple"), Count(english, PartKinds.Sign, "marketcross")));
        string[] trades = ["shop", "bakery", "tavern", "cooper", "wheelwright", "potter", "weaver", "butcher", "chandler"];
        int plying = english.Parts.Count(p => p.Kind == PartKinds.Block && trades.Contains(p.Class));
        int homes = Count(english, PartKinds.Block, "house");
        Assert.True(plying >= 3 && plying < homes, $"{plying} trades among {homes} houses");
        Assert.Contains(Enumerable.Range(1, 6), seed =>
        {
            var fortress = Town(Manners.Continental, seed, 0.7, 1);
            return Count(fortress, PartKinds.Block, "barracks") == 2 && Count(fortress, PartKinds.Area, "parade") == 1 && Count(fortress, PartKinds.Block, "magazine") == 1;
        });
        Assert.Equal(0, Count(Town(Manners.Continental), PartKinds.Block, "barracks"));
    }

    [Fact]
    public void Village_yards_and_country_farms_keep_their_hens_pigs_and_beasts()
    {
        var villages = Enumerable.Range(1, 6).Select(seed => Town(Manners.Continental, seed, 0.3, 0, 5)).ToList();
        Assert.True(villages.Sum(v => Count(v, PartKinds.Block, "coop")) >= 6, $"{villages.Sum(v => Count(v, PartKinds.Block, "coop"))} coops in six villages");
        Assert.All(villages, v => Assert.Equal(Count(v, PartKinds.Block, "coop"), Count(v, PartKinds.Sign, "hens")));
        Assert.Contains(villages, v => Count(v, PartKinds.Block, "pigsty") > 0 && Count(v, PartKinds.Area, "pen") > 0 && Count(v, PartKinds.Sign, "pigs") > 0);
        string[] sheds = ["barn", "stable", "byre", "granary", "cartshed"];
        var kept = villages.SelectMany(v => v.Parts).Where(p => p.Kind == PartKinds.Block && sheds.Contains(p.Class)).GroupBy(p => p.Class).ToDictionary(g => g.Key, g => g.Count());
        Assert.True(kept.Count >= 3, string.Join(", ", kept.Select(k => $"{k.Value} {k.Key}")));
        Assert.Contains(villages, v => v.Parts.Any(p => p.Kind == PartKinds.Sign && p.Class is "sheep" or "cattle" or "horses"));
        var country = BuiltDetails.Lay(Site(Manners.Continental), "s", [new Spot(8, 30), new Spot(52, 30)], 12, 4, 0.5, 0.9);
        Assert.True(Count(country, PartKinds.Block, "farm") >= 2);
        Assert.Contains(country.Parts, p => p.Class is "coop" or "pigsty" or "dungheap");
        Assert.Equal(Manners.Continental, Manners.Of(country));
        var tribal = BuiltDetails.Lay(Site(Manners.Tribal), "s", [new Spot(8, 30), new Spot(52, 30)], 12, 4, 0.5, 0.9);
        Assert.True(Count(tribal, PartKinds.Block, "roundhouse") >= 2 && Count(tribal, PartKinds.Block, "farm") == 0);
    }

    [Fact]
    public void The_catalogue_is_sectioned_by_use_kept_to_its_era_and_every_entry_can_be_stood()
    {
        Assert.Equal(["homes", "farm", "trade", "arms", "sacred", "water", "green"], Catalogue.Sections.Select(s => s.Id));
        Assert.All(Catalogue.Sections, s => Assert.InRange(s.Entries.Count, 5, 24));
        Assert.Equal(Catalogue.All.Count, Catalogue.All.Select(e => e.Id).Distinct().Count());
        var later = Catalogue.For(false).SelectMany(s => s.Entries).Select(e => e.Id).ToList();
        var old = Catalogue.For(true).SelectMany(s => s.Entries).Select(e => e.Id).ToList();
        Assert.True(later.Contains("dovecote") && !later.Contains("domus") && old.Contains("domus") && !old.Contains("dovecote") && old.Contains("house") && later.Contains("house"));
        Assert.All(Catalogue.For(false, true).SelectMany(s => s.Entries), e => Assert.True(e.Block));
        Assert.All(Catalogue.All, e =>
        {
            var made = BuiltThings.Made(Site(0), e.Id, Middle);
            Assert.Equal(e.Laid, made[0].Class);
            Assert.Equal(e.Block ? PartKinds.Block : PartKinds.Sign, made[0].Kind);
            Assert.Equal(e.Block && e.With != null ? 2 : 1, made.Count);
        });
        Assert.Contains("house", BuiltThings.Blocks);
        var mill = BuiltThings.Building(Site(0), "s", "watermill", Middle, new Spot(1, 0));
        Assert.Equal(["mill", "wheel"], mill.Parts.Select(p => p.Class));
    }
}
