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
    public void Nobody_is_out_at_night_and_a_modest_share_is_out_in_the_morning()
    {
        var t = Town(Table());
        int shown = t.Residents.Count(r => r.Rank < 0.5);
        Assert.Equal(0, Outside(t, 3 * Hour));
        Assert.InRange(Outside(t, 10 * Hour + 30 * 60), 1, shown * 2 / 5);
        var grown = t.Residents.Where(r => r.Entry.Band == E.Adult && t.Places[r.Home].Use == CrowdUse.Home).ToList();
        int Worship(DayKind kind) => grown.Count(r => CrowdDay.Plan(t, r, kind, 1).Any(l => t.Places[l.To].Use == CrowdUse.Worship));
        Assert.True(Worship(DayKind.Holy) > grown.Count / 2 && Worship(DayKind.Holy) > Worship(DayKind.Working) * 3, "on the holy day the town goes to church");
        Assert.All(t.Residents.Where(r => r.Entry.Calling == CrowdBaseline.Soldier), r => Assert.Equal(CrowdUse.Barracks, t.Places[r.Home].Use));
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
