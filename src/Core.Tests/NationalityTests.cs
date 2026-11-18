using KTable.Core.Model;
using E = KTable.Core.Model.CrowdEntry;

namespace KTable.Core.Tests;

public class NationalityTests
{
    const long Now = 1_000_000_000;
    const int Town = 41;

    static List<E> Crowd(Nationality n, int count = 300, IEnumerable<Nationality>? own = null)
    {
        var flat = CrowdDress.Flat(n, own);
        string coat = CrowdDress.ArmyCoat(flat, Town);
        var people = Enumerable.Range(0, count).Select(i => CrowdDress.Person(flat, i, 1000 + i * 7919, Now)).ToList();
        foreach (var p in people) CrowdDress.Look(p, flat, coat);
        return people;
    }

    [Fact]
    public void The_six_shipped_nationalities_are_whole_and_borrow_a_town_style()
    {
        Assert.Equal(["gothic", "continental", "ottoman", "japanese", "roman", "modern"], Nationalities.All.Select(n => n.Id));
        foreach (var n in Nationalities.All)
        {
            Assert.Empty(CrowdDress.Problems(n));
            Assert.Contains(Manners.All, m => m.Id == n.Culture);
        }
        Assert.Empty(CrowdDress.Problems(CrowdBaseline.Person));
    }

    [Fact]
    public void A_nationality_that_says_nothing_still_gives_dressed_working_people()
    {
        var people = Crowd(new Nationality { Id = "blank" });
        foreach (var p in people)
        {
            Assert.NotEmpty(p.Name);
            Assert.False(string.IsNullOrEmpty(p.Calling));
            Assert.All(CrowdSlots.All, slot => Assert.StartsWith("#", p.Look!.Colours[slot]));
            Assert.NotNull(p.Look!.Part(Wardrobe.Robe));
            Assert.NotNull(p.Look.Part(Wardrobe.Feet));
            Assert.InRange(p.Look.HeightM, 0.8, 2.1);
        }
        Assert.Contains(people, p => p.Calling == CrowdBaseline.Smith && p.Look!.Item == "hammer");
    }

    [Fact]
    public void A_nationality_falls_back_field_by_field()
    {
        var reds = new Nationality { Id = "reds", Dyes = new() { [E.Poor] = ["#aa0000"] } };
        var flat = CrowdDress.Flat(reds);
        Assert.Equal(["#aa0000"], flat.Dyes![E.Poor]);
        Assert.Equal(CrowdBaseline.Person.Dyes![E.Middle], flat.Dyes[E.Middle]);
        Assert.Equal(CrowdBaseline.Person.Hairs, flat.Hairs);
        var poorLabourers = Crowd(reds).Where(p => p.Wealth == E.Poor && p.Calling == CrowdBaseline.Labourer).ToList();
        Assert.NotEmpty(poorLabourers);
        Assert.All(poorLabourers, p => Assert.Equal("#aa0000", p.Look!.Colours[CrowdSlots.Outer]));
    }

    [Fact]
    public void A_copy_of_a_shipped_nationality_changes_only_what_it_says()
    {
        var copy = new Nationality { Id = "my-prussians", Base = "continental", ArmyCoats = ["#123456"] };
        var flat = CrowdDress.Flat(copy);
        Assert.Equal("continental", flat.Culture);
        Assert.Equal(Nationalities.Named("continental")!.Heights, flat.Heights);
        Assert.Equal("#123456", CrowdDress.ArmyCoat(flat, Town));
        var soldiers = Crowd(copy, 600).Where(p => p.Calling == CrowdBaseline.Soldier).ToList();
        Assert.NotEmpty(soldiers);
        Assert.All(soldiers, s => Assert.Equal("tricorne", s.Look!.Part(Wardrobe.Hat)!.Kind));
    }

    [Fact]
    public void A_stored_look_is_never_dressed_again()
    {
        var mine = new Nationality { Id = "mine", Base = "gothic" };
        var person = Crowd(mine, 1)[0];
        var stored = person.Look!;
        string before = KTable.Core.Json.Serialize(stored);
        mine.Dyes = new() { [E.Poor] = ["#00ff00"], [E.Middle] = ["#00ff00"], [E.Rich] = ["#00ff00"] };
        mine.Heights = new() { [E.Man] = 2.5, [E.Woman] = 2.5 };
        var flat = CrowdDress.Flat(mine);
        Assert.Same(stored, CrowdDress.Look(person, flat, CrowdDress.ArmyCoat(flat, Town)));
        Assert.Equal(before, KTable.Core.Json.Serialize(person.Look!));
        var fresh = CrowdDress.Person(flat, person.Id, person.Seed, Now);
        Assert.NotEqual(before, KTable.Core.Json.Serialize(CrowdDress.Look(fresh, flat, CrowdDress.ArmyCoat(flat, Town))));
    }

    [Fact]
    public void Every_soldier_of_a_town_wears_its_one_army_coat()
    {
        foreach (var n in Nationalities.All)
        {
            var flat = CrowdDress.Flat(n);
            var coats = Enumerable.Range(0, 40).Select(town => CrowdDress.ArmyCoat(flat, town)).Distinct().ToList();
            Assert.Equal(n.ArmyCoats!.Count > 1, coats.Count > 1);
            var soldiers = Crowd(n, 600).Where(p => p.Calling == CrowdBaseline.Soldier).ToList();
            Assert.NotEmpty(soldiers);
            Assert.Single(soldiers.Select(s => s.Look!.Colours[CrowdSlots.Outer]).Distinct());
            Assert.Equal(CrowdDress.ArmyCoat(flat, Town), soldiers[0].Look!.Colours[CrowdSlots.Outer]);
        }
    }

    [Fact]
    public void Names_follow_the_nationality_pattern()
    {
        var ottomans = Crowd(Nationalities.Named("ottoman")!, 50);
        Assert.All(ottomans, p => Assert.Contains(p.Sex == E.Man ? ", son of " : ", daughter of ", p.Name));
        var samurai = CrowdDress.Person(CrowdDress.Flat(Nationalities.Named("japanese")!), 1, 5, Now, E.Man, E.Adult, "samurai");
        Assert.Equal(2, samurai.Name.Split(' ').Length);
        Assert.Equal("Samurai", CrowdDress.CallingName(CrowdDress.Flat(Nationalities.Named("japanese")!), "samurai"));
    }

    [Fact]
    public void The_same_seed_gives_the_same_person_and_look()
    {
        var a = Crowd(Nationalities.Named("roman")!, 20);
        var b = Crowd(Nationalities.Named("roman")!, 20);
        Assert.Equal(a.Select(p => KTable.Core.Json.Serialize(p.Look!) + p.Name), b.Select(p => KTable.Core.Json.Serialize(p.Look!) + p.Name));
    }

    [Fact]
    public void A_nationality_and_a_roster_survive_a_json_round_trip()
    {
        foreach (var n in Nationalities.All)
        {
            var back = KTable.Core.Json.Deserialize<Nationality>(KTable.Core.Json.Serialize(n));
            Assert.Equal(KTable.Core.Json.Serialize(n), KTable.Core.Json.Serialize(back));
        }
        var roster = Crowd(Nationalities.Named("modern")!, 10);
        var again = KTable.Core.Json.Deserialize<List<E>>(KTable.Core.Json.Serialize(roster));
        Assert.Equal(KTable.Core.Json.Serialize(roster), KTable.Core.Json.Serialize(again));
    }

    [Fact]
    public void Problems_names_what_a_crafted_nationality_gets_wrong()
    {
        var bad = new Nationality
        {
            Id = "bad", Dress = [new() { Wear = [Garment.Of("cape", "long")], Colours = new() { ["glow"] = "teal" }, Item = "laser" }],
            Naming = [new() { Pattern = "{nobody}" }],
        };
        var problems = CrowdDress.Problems(bad);
        Assert.Contains(problems, p => p.Contains("unknown part cape"));
        Assert.Contains(problems, p => p.Contains("unknown slot glow"));
        Assert.Contains(problems, p => p.Contains("unknown colour teal"));
        Assert.Contains(problems, p => p.Contains("unknown hand item laser"));
        Assert.Contains(problems, p => p.Contains("no list nobody"));
    }

    [Fact]
    public void A_look_carries_its_whole_body_and_is_changed_part_by_part()
    {
        var person = CrowdDress.Person(CrowdDress.Flat(Nationalities.Named("japanese")!), 1, 77, Now, E.Woman, E.Elder, CrowdBaseline.Housewife);
        var look = CrowdDress.Look(person, CrowdDress.Flat(Nationalities.Named("japanese")!), "#000000");
        Assert.Equal((E.Woman, E.Elder, CrowdLook.Far), (look.Sex, look.Band, look.Face));
        var alone = KTable.Core.Json.Deserialize<CrowdLook>(KTable.Core.Json.Serialize(look));
        Assert.Equal(KTable.Core.Json.Serialize(look), KTable.Core.Json.Serialize(alone));
        var dressed = look.Clone();
        dressed.Wear(Garment.Of(Wardrobe.Hat, "kasa", CrowdSlots.Hat));
        dressed.Wear(Garment.Of(Wardrobe.Robe, "kimono", CrowdSlots.Outer, 0.1, 1.1, "shoulder"));
        dressed.TakeOff(Wardrobe.Belt);
        dressed.Colours[CrowdSlots.Outer] = "#336699";
        Assert.Equal("kasa", dressed.Part(Wardrobe.Hat)!.Kind);
        Assert.Equal(0.1, dressed.Part(Wardrobe.Robe)!.Hem);
        Assert.Single(dressed.Worn, g => g.Part == Wardrobe.Robe);
        Assert.Null(dressed.Part(Wardrobe.Belt));
        Assert.Equal(dressed.Worn.Select(g => g.Part), Wardrobe.Parts.Where(p => dressed.Part(p) != null));
        Assert.Equal(look.Part(Wardrobe.Feet)!.Kind, dressed.Part(Wardrobe.Feet)!.Kind);
        Assert.NotNull(look.Part(Wardrobe.Belt));
        Assert.NotEqual("#336699", look.Colours[CrowdSlots.Outer]);
    }

    [Fact]
    public void A_table_s_packs_carry_their_own_nationalities_over_the_shipped_ones()
    {
        var copy = new Nationality { Id = "continental", Base = "gothic", Name = "My Continentals" };
        var fresh = new Nationality { Id = "my-saxons", Base = "continental", ArmyCoats = ["#ffffff"] };
        var pack = new PackManifest { Id = "user.table", Name = "Table", Nationalities = [copy, fresh] };
        var back = KTable.Core.Json.Deserialize<PackManifest>(KTable.Core.Json.Serialize(pack));
        var own = Nationalities.Of([new PackManifest { Id = "std.x", Name = "X" }, back]);
        Assert.Equal(["continental", "my-saxons"], own.Select(n => n.Id));
        Assert.Equal("My Continentals", CrowdDress.Find("continental", own)!.Name);
        Assert.Equal("Gothic", CrowdDress.Find("gothic", own)!.Name);
        Assert.Equal("#ffffff", CrowdDress.ArmyCoat(CrowdDress.Flat(CrowdDress.Find("my-saxons", own)!, own), Town));
        Assert.Equal(1.70, CrowdDress.Flat(CrowdDress.Find("my-saxons", own)!, own).Heights![E.Man]);
        Assert.Equal(7, Nationalities.Offered([back]).Count());
        Assert.Equal("My Continentals", Nationalities.Offered([back]).First(n => n.Id == "continental").Name);
        Assert.Null(CrowdDress.Find("nobody", own));
    }
}
