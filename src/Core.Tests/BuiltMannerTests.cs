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
        foreach (int manner in Manners.All.Select(m => m.Number))
        {
            var town = Town(manner);
            Assert.Equal(manner, Manners.Of(town));
            Assert.Equal(manner, Manners.Of(BuiltThings.Remade(Site(Manners.Continental), town, 9)));
        }
        Assert.Equal(Manners.Tribal, Manners.For(Manners.Ancient, Manners.Northern));
        Assert.Equal(Manners.American, Manners.For(Manners.Later, Manners.Egyptian));
        Assert.Equal(Manners.Tribal, Manners.For(Manners.Ancient, Manners.Tribal));
        Assert.Equal(Manners.Soviet, Manners.For(Manners.Modern, Manners.Tribal));
        foreach (int epoch in new[] { Manners.Ancient, Manners.Medieval, Manners.Later, Manners.Modern })
        {
            var five = Manners.In(epoch).ToList();
            Assert.Equal(5, five.Count);
            Assert.Equal(5, five.Select(m => Manners.Named(m).Seat).Distinct().Count());
            Assert.All(five, m => Assert.Equal(Manners.Old(m), Manners.Old(Manners.Kin(m))));
        }
        var old = new BuiltThing { Settings = { [Settlements.Old] = 1 } };
        Assert.Equal(Manners.Roman, Manners.Of(old));
    }

    [Fact]
    public void A_place_is_laid_out_built_and_adorned_by_cultures_of_one_age()
    {
        var pure = new BuiltThing { Settings = { [Manners.Setting] = Manners.Roman } };
        Assert.Equal(new Manners.Sides(Manners.Roman, Manners.Roman, Manners.Roman), Manners.SidesOf(pure));
        var mixed = new BuiltThing { Settings = { [Manners.Setting] = Manners.Roman, [Manners.Builders] = Manners.Egyptian, [Manners.Adorners] = Manners.Tribal } };
        Assert.Equal(new Manners.Sides(Manners.Roman, Manners.Egyptian, Manners.Tribal), Manners.SidesOf(mixed));
        var across = new BuiltThing { Settings = { [Manners.Setting] = Manners.Roman, [Manners.Builders] = Manners.Northern, [Manners.Adorners] = Manners.Soviet } };
        Assert.Equal(new Manners.Sides(Manners.Roman, Manners.Roman, Manners.Roman), Manners.SidesOf(across));
        var town = Town(Manners.Roman);
        town.Settings[Manners.Builders] = Manners.Egyptian;
        town.Settings[Manners.Adorners] = Manners.Egyptian;
        Assert.Equal(new Manners.Sides(Manners.Roman, Manners.Egyptian, Manners.Egyptian), Manners.SidesOf(BuiltThings.Remade(Site(Manners.Roman), town, 9)));
    }

    [Fact]
    public void Every_culture_lays_a_town_with_its_own_marks()
    {
        foreach (var culture in Manners.All)
        {
            var town = Town(culture.Number);
            Assert.True(town.Parts.Count(p => p.Kind == PartKinds.Block) > 20, culture.Id + " lays a town");
            Assert.Equal(culture.Number, Manners.Of(town));
        }
        var chinese = Town(Manners.Chinese);
        Assert.True(Count(chinese, PartKinds.Block, "villa") > 0, "a Chinese town has courtyard houses");
        Assert.Equal(0, Count(chinese, PartKinds.Area, "forum") + Count(chinese, PartKinds.Block, "basilica") + Count(chinese, PartKinds.Block, "domus"));
        foreach (int unsteepled in new[] { Manners.Ottoman, Manners.Japanese, Manners.NorthAfrican, Manners.Nippon, Manners.Byzantine, Manners.Saracen })
        {
            var town = Town(unsteepled);
            Assert.True(Count(town, PartKinds.Block, "church") >= 1);
            Assert.Equal((0, 0), (Count(town, PartKinds.Block, "steeple"), Count(town, PartKinds.Sign, "cross")));
        }
        Assert.Equal(1, Count(Town(Manners.American), PartKinds.Block, "steeple"));
        Assert.Equal(0, Count(Town(Manners.Ottoman), PartKinds.Sign, "marketcross"));
    }

    [Fact]
    public void A_mix_is_kept_by_its_code_named_by_its_cultures_and_laid_onto_a_place_of_its_age()
    {
        var mix = new Manners.Mix(Manners.Roman, Manners.Egyptian, Manners.Tribal, Manners.Persian, 0.3);
        Assert.Equal("roman+egyptian+tribal+persian+0.3", mix.Code);
        Assert.Equal(mix, Manners.Parse(mix.Code));
        Assert.Equal("Roman, Egyptian builders, Tribal adorners, Persian quarter", mix.Name);
        var pure = Manners.Parse("chinese");
        Assert.True(pure.Pure);
        Assert.Equal(("chinese", "Chinese"), (pure.Code, pure.Name));
        Assert.True(Manners.Parse("nonsense+x").Pure);
        var town = Town(Manners.Roman);
        mix.Onto(town);
        Assert.Equal(mix, Manners.MixOf(town));
        Assert.Equal(mix, Manners.MixOf(BuiltThings.Remade(Site(Manners.Roman), town, 9)));
        var later = Town(Manners.Continental);
        mix.Onto(later);
        Assert.True(Manners.MixOf(later).Pure);
        town.Settings[Manners.WardShare] = 0.9;
        Assert.Equal(Manners.MostWard, Manners.MixOf(town).Share);
        town.Settings[Manners.Ward] = Manners.Ottoman;
        Assert.Equal(-1, Manners.MixOf(town).Ward);
    }

    const int OnceEnglish = 1, OnceSouthern = 3;

    [Fact]
    public void A_place_saved_as_English_or_Southern_is_Continental()
    {
        foreach (int once in new[] { OnceEnglish, OnceSouthern })
        {
            var place = new BuiltThing { Settings = { [Manners.Setting] = once } };
            Assert.Equal(Manners.Continental, Manners.Of(place));
            Assert.Equal(Manners.Continental, Manners.For(Manners.Later, once));
            Assert.Equal("continental", Manners.Id(once));
        }
        Assert.Equal(Manners.Continental, Manners.Of("english"));
        Assert.Equal(Manners.Continental, Manners.Of("southern"));
        Assert.DoesNotContain(Manners.All, m => m.Id is "english" or "southern");
    }

    [Fact]
    public void A_roman_town_has_atrium_houses_blocks_a_forum_a_temple_with_its_altar_and_no_steeple()
    {
        var town = Town(Manners.Roman);
        Assert.True(Count(town, PartKinds.Block, "domus") >= 3 && Count(town, PartKinds.Block, "insula") >= 2, $"{Count(town, PartKinds.Block, "domus")} domus, {Count(town, PartKinds.Block, "insula")} insulae");
        Assert.Equal(1, Count(town, PartKinds.Area, "forum"));
        Assert.Equal(0, Count(town, PartKinds.Area, "square"));
        Assert.Equal((1, 0), (Count(town, PartKinds.Block, "church"), Count(town, PartKinds.Block, "steeple")));
        Assert.True(Count(town, PartKinds.Sign, "altar") >= 1, "an altar before the temple, and tombs along the road out");
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
        var english = Town(Manners.Continental);
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
        Assert.Equal(["homes", "farm", "trade", "works", "arms", "sacred", "water", "green"], Catalogue.Sections.Select(s => s.Id));
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
            Assert.Equal(e.Shape == Catalogue.Run ? PartKinds.Line : e.Shape == Catalogue.Ground ? PartKinds.Area : e.Block ? PartKinds.Block : PartKinds.Sign, made[0].Kind);
            Assert.Equal(e.Block && e.With != null ? 2 : 1, made.Count);
        });
        Assert.Contains("house", BuiltThings.Blocks);
        var mill = BuiltThings.Building(Site(0), "s", "watermill", Middle, new Spot(1, 0));
        Assert.Equal(["mill", "wheel"], mill.Parts.Select(p => p.Class));
    }
}
