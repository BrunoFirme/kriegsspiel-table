using KTable.Core.Model;
using KTable.Core.Protocol;
using E = KTable.Core.Model.CrowdEntry;

namespace KTable.Core.Tests;

public class CrowdDayTests
{
    const double Side = 60, MetresPerUnit = 10, Radius = 12;
    const int Hour = CalendarDef.SecondsInHour;
    const long Now = 1_000_000_000;
    static readonly Traveller Foot = Travellers.Default(Manners.Later)[Travellers.Foot];

    static GameState Table(int seed = 3)
    {
        var g = new GameState { ScenarioId = "t" };
        g.Sheets.Add(new SheetDef { Id = "s", Size = new Size2(Side, Side) });
        g.Calibration.UnitsPerMeter = 1 / MetresPerUnit;
        var site = new BuiltSite { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresPerUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = [], Manner = Manners.Continental };
        var town = Settlements.Lay(site, "s", new Spot(Side / 2, Side / 2), Radius, seed, 0.8, 0, Plans.Value(Plans.Offered(Manners.Continental)[0]));
        town.Id = "w-1";
        g.Built.Add(town);
        return g;
    }

    static CrowdTown Town(GameState g) =>
        CrowdRoster.Towns(g, id => CrowdDress.Find(id) is { } n ? CrowdDress.Flat(n) : null, Now).Single();

    static CrowdPath Straight(CrowdTown t, int from, int to) => new([t.Places[from].At, t.Places[to].At]);

    static int Outside(CrowdTown t, int second, DayKind kind = DayKind.Working) =>
        t.Residents.Count(r => r.Rank < 0.5 && CrowdDay.Where(t, r, CrowdDay.Plan(t, r, kind, 1), (a, b) => Straight(t, a, b), second, 1 / MetresPerUnit, out _));

    [Fact]
    public void Most_of_a_middling_town_wears_undyed_cloth_and_strong_dye_is_for_the_few()
    {
        var town = Town(Table());
        var flat = town.People;
        var outer = town.Residents.Where(r => r.Entry.Calling is not (CrowdBaseline.Soldier or CrowdBaseline.Cleric))
            .Select(r => CrowdDress.Look(r.Entry, flat, town.Coat).Colours[CrowdSlots.Outer]).ToList();
        Assert.True(outer.Count > 50);
        double strong = outer.Count(c => flat.Dyes![E.Rich].Contains(c)) / (double)outer.Count;
        double dyed = outer.Count(c => flat.Dyes![E.Rich].Contains(c) || flat.Dyes[E.Middle].Contains(c)) / (double)outer.Count;
        Assert.InRange(strong, 0, 0.15);
        Assert.InRange(dyed, 0.1, 0.5);
        Assert.Contains(town.Residents, r => r.Entry.Wealth == E.Rich);
        Assert.True(town.Residents.Count(r => r.Entry.Wealth == E.Poor) > town.Residents.Count / 3);
    }

    [Fact]
    public void A_nationality_s_timetable_from_its_pack_sends_a_calling_where_it_says()
    {
        var g = Table();
        var people = Json.Clone(Nationalities.Named("continental")!);
        people.Day = new DayRules { Timetables = new() { [CrowdBaseline.Labourer] = [new TimetableStop { At = 6, To = "street" }, new TimetableStop { At = 20, To = "home" }] } };
        Assert.Empty(CrowdDress.Problems(people));
        var town = CrowdRoster.Towns(g, _ => CrowdDress.Flat(people), Now).Single();
        var labourer = town.Residents.First(r => r.Entry.Calling == CrowdBaseline.Labourer && r.Leader < 0);
        var legs = CrowdDay.Plan(town, labourer, DayKind.Working, 1);
        Assert.Equal(CrowdUse.Street, town.Places[legs[0].To].Use);
        Assert.Equal(6 * Hour, legs[0].Depart);
        Assert.Equal(labourer.Home, legs[^1].To);
        people.Day.Timetables["anyone"] = [new TimetableStop { At = 30, To = "moon" }];
        Assert.NotEmpty(CrowdDress.Problems(people));
    }

    [Fact]
    public void A_made_crowd_settled_in_a_town_lives_in_its_houses_and_goes_about_its_day()
    {
        var g = Table();
        var town = Town(g);
        var made = new MadeCrowd { Id = "m-1", Count = 30, Mix = MadeMixes.Pilgrims, At = new Vec2(0, 0), Seed = 5, Settled = "w-1" };
        var settled = CrowdMade.Settle(made, CrowdDress.Flat(Nationalities.Baseline), town, 0);
        Assert.Equal(30, settled.Residents.Count);
        Assert.Same(town.Places, settled.Places);
        Assert.All(settled.Residents, r => Assert.Equal(CrowdUse.Home, town.Places[r.Home].Use));
        Assert.Contains(settled.Residents, r => CrowdDay.Plan(settled, r, DayKind.Working, 1).Count > 1);
        Assert.Null(StateMachine.Apply(g, new SetMadeCrowd { Crowd = made }, out _));
        made.Settled = "w-9";
        Assert.NotNull(StateMachine.Apply(g, new SetMadeCrowd { Crowd = made }, out _));
    }

    [Fact]
    public void A_town_s_roster_is_the_same_on_every_machine_and_fits_its_culture()
    {
        var g = Table();
        var a = Town(g);
        var b = Town(Table());
        Assert.Equal("continental", a.People.Id);
        Assert.NotEmpty(a.Residents);
        Assert.Equal(a.Residents.Select(r => KTable.Core.Json.Serialize(r.Entry)), b.Residents.Select(r => KTable.Core.Json.Serialize(r.Entry)));
        Assert.All(a.Residents, r => Assert.True(a.Places[r.Home].Use is CrowdUse.Home or CrowdUse.Barracks));
        Assert.Contains(a.Residents, r => r.Work >= 0 && r.Entry.Calling != CrowdBaseline.Labourer);
        Assert.Contains(a.Residents, r => r.Entry.Band == E.Child);
        Assert.Equal("modern", CrowdRoster.NationalityOf(g.Built[0], g.Sheets[0], new GameState { Lighting = { MapStyle = "modern" } }));
    }

    [Fact]
    public void A_level_shows_its_share_of_a_fixed_ranking_the_same_faces_each_time()
    {
        var t = Town(Table());
        int Shown(int level) => t.Residents.Count(r => r.Rank < Population.Share(level));
        Assert.Equal(0, Shown(0));
        Assert.Equal(t.Residents.Count, Shown(Population.Most));
        Assert.InRange(Shown(Population.Usual), t.Residents.Count * 0.4, t.Residents.Count * 0.6);
        var usual = t.Residents.Where(r => r.Rank < Population.Share(50)).Select(r => r.Entry.Id).ToHashSet();
        Assert.Subset(t.Residents.Where(r => r.Rank < Population.Share(100)).Select(r => r.Entry.Id).ToHashSet(), usual);
    }

    [Fact]
    public void Only_the_watch_is_out_at_night_and_a_quarter_of_the_town_by_mid_morning_spread_over_its_places()
    {
        var t = Town(Table());
        var shown = t.Residents.Where(r => r.Rank < 0.5).ToList();
        List<CrowdWhere> Out(int second, DayKind kind) => shown
            .Select(r => CrowdDay.Where(t, r, CrowdDay.Plan(t, r, kind, 1), (a, b) => Straight(t, a, b), second, 1 / MetresPerUnit, out var w) ? (CrowdWhere?)w : null)
            .OfType<CrowdWhere>().ToList();
        var night = shown.Where(r => CrowdDay.Where(t, r, CrowdDay.Plan(t, r, DayKind.Working, 1), (a, b) => Straight(t, a, b), 3 * Hour, 1 / MetresPerUnit, out _)).ToList();
        Assert.All(night, r => Assert.Equal(CrowdBaseline.Soldier, r.Entry.Calling));
        Assert.InRange(night.Count, 0, shown.Count / 20);
        var morning = Out(10 * Hour + 24 * 60, DayKind.Working);
        Assert.InRange(morning.Count, shown.Count / 6, shown.Count * 2 / 5);
        var busiest = morning.Where(w => w.Place >= 0).GroupBy(w => w.Place).Max(g => g.Count());
        Assert.True(busiest < morning.Count / 3, $"no one place holds the town on a working morning ({busiest} of {morning.Count})");
        int Market(DayKind kind) => Out(10 * Hour + 24 * 60, kind).Count(w => w.Place >= 0 && t.Places[w.Place].Use == CrowdUse.Market);
        Assert.True(Market(DayKind.Market) > Market(DayKind.Working) * 3, "the market fills on a market day");
        var grown = t.Residents.Where(r => r.Entry.Band == E.Adult && t.Places[r.Home].Use == CrowdUse.Home).ToList();
        int Worship(DayKind kind) => grown.Count(r => CrowdDay.Plan(t, r, kind, 1).Any(l => t.Places[l.To].Use == CrowdUse.Worship));
        Assert.True(Worship(DayKind.Holy) > grown.Count / 2 && Worship(DayKind.Holy) > Worship(DayKind.Working) * 3, "on the holy day the town goes to church");
    }

    [Fact]
    public void Soldiers_hold_a_post_and_walk_their_rounds_in_file_behind_their_leader()
    {
        var t = Town(Table());
        var soldiers = t.Residents.Where(r => r.Entry.Calling == CrowdBaseline.Soldier).ToList();
        Assert.True(soldiers.Count >= 4);
        Assert.All(soldiers, s => Assert.True(s.Work >= 0 && t.Places[s.Work].Use is CrowdUse.Gate or CrowdUse.Square or CrowdUse.Market or CrowdUse.Barracks or CrowdUse.Work));
        var file = soldiers.First(s => s.Leader >= 0);
        var leader = t.Residents[file.Leader];
        Assert.Equal(CrowdDay.Plan(t, leader, DayKind.Working, 1), CrowdDay.Plan(t, file, DayKind.Working, 1));
        Assert.Equal(leader.SpeedM, file.SpeedM);
        var plan = CrowdDay.Plan(t, leader, DayKind.Working, 1);
        var spans = CrowdDay.Spans(t, leader, plan, (a, b) => Straight(t, a, b), 1 / MetresPerUnit);
        var walk = spans.First(s => s.Path is { Length: > 1 });
        int mid = (walk.Start + walk.End) / 2;
        Assert.True(CrowdDay.At(t, leader, walk, mid, 1 / MetresPerUnit, out var head));
        Assert.True(CrowdDay.At(t, file, walk, mid, 1 / MetresPerUnit, out var behind));
        Assert.InRange((head.At - behind.At).Length * MetresPerUnit, file.File * 1.2, file.File * 1.6);
    }

    [Fact]
    public void A_walker_is_on_the_way_and_a_trip_with_no_way_does_not_run()
    {
        var t = Town(Table());
        var r = t.Residents.First(x => x.Work >= 0 && x.Entry.Calling != CrowdBaseline.Soldier && (t.Places[x.Work].At - t.Places[x.Home].At).Length > 1);
        var legs = CrowdDay.Plan(t, r, DayKind.Working, 1);
        var first = legs[0];
        double length = (t.Places[first.To].At - t.Places[r.Home].At).Length;
        int halfway = first.Depart + (int)(length / 2 / (r.SpeedM / MetresPerUnit));
        Assert.True(CrowdDay.Where(t, r, legs, (a, b) => Straight(t, a, b), halfway, 1 / MetresPerUnit, out var w));
        Assert.True(w.Moving && w.Place < 0);
        Assert.InRange((w.At - t.Places[r.Home].At).Length / length, 0.4, 0.6);
        Assert.False(CrowdDay.Where(t, r, legs, (_, _) => null, halfway, 1 / MetresPerUnit, out _), "a trip with no way stays indoors at home");
    }

    [Fact]
    public void Routes_follow_the_streets_and_go_round_a_cleared_area()
    {
        var g = Table();
        var t = Town(g);
        var map = TravelMap.For(g, "s", null) ?? throw new Xunit.Sdk.XunitException("the town has ways");
        int market = t.Of(CrowdUse.Market).Concat(t.Of(CrowdUse.Worship)).First();
        var tree = map.TreeTo(t.Places[market].At, Foot) ?? throw new Xunit.Sdk.XunitException("the market is on a street");
        var far = t.Residents.Select(r => r.Home).Distinct().MaxBy(h => (t.Places[h].At - t.Places[market].At).Length);
        var path = tree.From(map.Sheeted(t.Places[far].At)) ?? throw new Xunit.Sdk.XunitException("a far house reaches the market");
        Assert.Equal(t.Places[market].At, path[^1]);
        Assert.True(path.Count > 3);
        var walked = new CrowdPath(path).Length;
        Assert.True(walked >= (t.Places[far].At - t.Places[market].At).Length * 0.99, "a street is never shorter than the crow's way");
        var middle = path[path.Count / 2];
        var cleared = map.TreeTo(t.Places[market].At, Foot, p => (p - middle).Length < 2 / MetresPerUnit * 10);
        var round = cleared?.From(map.Sheeted(t.Places[far].At));
        Assert.True(round == null || round.All(p => (p - middle).Length >= 2 / MetresPerUnit * 10 - 1e-6), "the way round keeps out of the cleared area");
    }

    [Fact]
    public void Population_levels_keep_clear_strokes_and_peoples_are_set_and_undone()
    {
        var g = Table();
        Assert.Null(StateMachine.Apply(g, new SetPopulation { Thing = "w-1", Parts = [0, 1], Levels = [0, 200] }, out var back));
        Assert.Equal((0, 200), (Population.Of(g.Built[0].Parts[0]), Population.Of(g.Built[0].Parts[1])));
        Assert.Null(StateMachine.Apply(g, back!, out _));
        Assert.True(g.Built[0].Parts.Take(2).All(p => p.Population == null));
        Assert.NotNull(StateMachine.Apply(g, new SetPopulation { Thing = "w-1", Parts = [0], Levels = [500] }, out _));
        Assert.NotNull(StateMachine.Apply(g, new SetPopulation { Thing = "nowhere", Parts = [0], Levels = [50] }, out _));
        var stroke = new ClearStroke { Points = [new Vec2(1, 1), new Vec2(3, 1)], Radius = 0.5 };
        Assert.Null(StateMachine.Apply(g, new SetKeepClear { Strokes = [stroke] }, out var unclear));
        Assert.True(g.Crowds!.Clears(new Vec2(2, 1.2)) && !g.Crowds.Clears(new Vec2(2, 3)));
        Assert.Null(StateMachine.Apply(g, unclear!, out _));
        Assert.Null(g.Crowds);
        Assert.NotNull(StateMachine.Apply(g, new SetKeepClear { Strokes = [new ClearStroke { Points = [], Radius = 1 }] }, out _));
        var flat = CrowdDress.Flat(Nationalities.Named("gothic")!);
        Assert.Null(StateMachine.Apply(g, new SetPeoples { Peoples = new() { ["gothic"] = flat } }, out var forget));
        Assert.Equal("Gothic", g.Crowds!.Peoples["gothic"].Name);
        Assert.Null(StateMachine.Apply(g, forget!, out _));
        Assert.Null(g.Crowds);
        Assert.NotNull(StateMachine.Apply(g, new SetPeoples { Peoples = new() { ["other"] = flat } }, out _));
        var again = KTable.Core.Json.Deserialize<GameState>(KTable.Core.Json.Serialize(Table()));
        Assert.Equal(KTable.Core.Json.Serialize(Table()), KTable.Core.Json.Serialize(again));
    }
}
