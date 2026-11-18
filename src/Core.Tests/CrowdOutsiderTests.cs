using KTable.Core.Model;

namespace KTable.Core.Tests;

public class CrowdOutsiderTests
{
    const double Side = 80, MetresPerUnit = 10, Radius = 16, Upm = 1 / MetresPerUnit;

    static CrowdTown Town(int seed = 5)
    {
        var g = new GameState { ScenarioId = "t" };
        g.Sheets.Add(new SheetDef { Id = "s", Size = new Size2(Side, Side) });
        g.Calibration.UnitsPerMeter = Upm;
        var site = new BuiltSite { Width = Side, Height = Side, UnitsPerMetre = Upm, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = [], Manner = Manners.Continental };
        var built = Settlements.Lay(site, "s", new Spot(Side / 2, Side / 2), Radius, seed, 0.8, 0, Plans.Value(Plans.Offered(Manners.Continental)[0]));
        built.Id = "w-1";
        g.Built.Add(built);
        return CrowdRoster.Towns(g, id => CrowdDress.Find(id) is { } n ? CrowdDress.Flat(n) : null, 0).Single();
    }

    static List<CrowdSpan> Spans(CrowdTown t, CrowdResident r, DayKind kind) =>
        CrowdDay.Spans(t, r, CrowdDay.Plan(t, r, kind, 4), (a, b) => new CrowdPath([t.Places[a].At, t.Places[b].At]), Upm);

    [Fact]
    public void Villagers_and_travellers_live_beyond_the_town_edge_and_are_never_seen_standing_there()
    {
        var town = Town();
        var ends = CrowdTraffic.Ends(town);
        var outsiders = town.Residents.Where(r => r.Outsider).ToList();
        Assert.Contains(outsiders, r => r.Entry.Calling == CrowdBaseline.Housewife);
        Assert.Contains(outsiders, r => r.Entry.Calling is CrowdBaseline.Merchant or CrowdBaseline.Labourer);
        Assert.All(outsiders, r => Assert.Contains(r.Home, ends));
        foreach (var kind in new[] { DayKind.Working, DayKind.Market })
            foreach (var r in outsiders)
                Assert.DoesNotContain(Spans(town, r, kind), s => s.Path == null && (s.Place == r.Home || s.Place == r.Away));
    }

    [Fact]
    public void Villagers_come_in_on_market_days_and_travellers_cross_the_town_from_edge_to_edge()
    {
        var town = Town();
        var villagers = town.Residents.Where(r => r.Outsider && r.Entry.Calling == CrowdBaseline.Housewife).ToList();
        Assert.All(villagers, r => Assert.Empty(Spans(town, r, DayKind.Working)));
        Assert.Contains(villagers, r => Spans(town, r, DayKind.Market).Any(s => s.Place >= 0 && town.Places[s.Place].Use == CrowdUse.Market));
        var travellers = town.Residents.Where(r => r.Outsider && r.Entry.Calling == CrowdBaseline.Labourer).ToList();
        Assert.NotEmpty(travellers);
        Assert.All(travellers, r => Assert.Contains(CrowdDay.Plan(town, r, DayKind.Working, 4), leg => leg.To == r.Away));
    }

    [Fact]
    public void A_traveller_may_be_a_stranger_dressed_by_their_own_nationality_while_the_town_keeps_its_own()
    {
        var strangers = Enumerable.Range(1, 6).SelectMany(seed => Town(seed).Residents).Where(r => r.Entry.Nationality != "continental").ToList();
        Assert.NotEmpty(strangers);
        Assert.All(strangers, r => Assert.True(r.Outsider && r.Entry.Nationality == "ottoman", $"{r.Entry.Calling} of {r.Entry.Nationality}"));
        Assert.All(Town().Residents.Where(r => !r.Outsider), r => Assert.Equal("continental", r.Entry.Nationality));
    }
}
