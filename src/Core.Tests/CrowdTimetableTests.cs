using KTable.Core.Model;

namespace KTable.Core.Tests;

public class CrowdTimetableTests
{
    const double Side = 60, MetresPerUnit = 10, Radius = 12;

    static CrowdTown Town(Func<Nationality, Nationality> shape)
    {
        var g = new GameState { ScenarioId = "t" };
        g.Sheets.Add(new SheetDef { Id = "s", Size = new Size2(Side, Side) });
        g.Calibration.UnitsPerMeter = 1 / MetresPerUnit;
        var site = new BuiltSite { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresPerUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = [], Manner = Manners.Continental };
        var built = Settlements.Lay(site, "s", new Spot(Side / 2, Side / 2), Radius, 3, 0.8, 0, Plans.Value(Plans.Offered(Manners.Continental)[0]));
        built.Id = "w-1";
        g.Built.Add(built);
        return CrowdRoster.Towns(g, id => CrowdDress.Find(id) is { } n ? shape(CrowdDress.Flat(n)) : null, 0).Single();
    }

    static int Going(CrowdTown t, DayKind kind = DayKind.Working) => t.Residents.Count(r => CrowdDay.Plan(t, r, kind, 2).Count > 0);

    [Fact]
    public void Every_day_plan_is_nationality_data_and_a_people_without_timetables_stays_in()
    {
        var shipped = Town(n => n);
        Assert.True(Going(shipped) > shipped.Residents.Count / 3, "the shipped timetables send most people out");
        Assert.NotNull(Nationalities.Baseline.Day?.Timetables);
        var none = Town(n =>
        {
            n.Day!.Timetables = new();
            return n;
        });
        Assert.Equal(0, Going(none));
    }

    [Fact]
    public void A_pack_timetable_for_one_calling_replaces_only_that_calling()
    {
        var own = new Nationality
        {
            Id = "mine", Base = "continental",
            Day = new DayRules { Timetables = new() { ["farmer"] = [new TimetableStop { At = 3, To = TimetableStop.Out, Hours = 1 }] } },
        };
        var flat = CrowdDress.Flat(own, [own]);
        Assert.Empty(CrowdDress.Problems(flat));
        Assert.Single(flat.Day!.Timetables!["farmer"]);
        Assert.Equal(Nationalities.Baseline.Day!.Timetables!["housewife"].Count, flat.Day.Timetables["housewife"].Count);
    }

    [Fact]
    public void A_stop_that_names_no_place_or_condition_the_planner_knows_is_refused()
    {
        var bad = new Nationality
        {
            Id = "bad", Base = "continental",
            Day = new DayRules { Timetables = new() { ["farmer"] = [new TimetableStop { To = "moon" }], ["smith"] = [new TimetableStop { To = "work", If = "tuesday" }] } },
        };
        Assert.Equal(2, CrowdDress.Problems(CrowdDress.Flat(bad, [bad])).Count(p => p.Contains("timetable", StringComparison.OrdinalIgnoreCase)));
    }
}
