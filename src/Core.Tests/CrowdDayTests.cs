using KTable.Core.Model;

namespace KTable.Core.Tests;

public class CrowdDayTests
{
    const double Side = 60, MetresToTheUnit = 10, Radius = 12;
    const int Hour = CalendarDef.SecondsInHour;
    static readonly double UnitsPerMetre = 1 / MetresToTheUnit;
    static readonly SheetDef Sheet = new() { Id = "s", Size = new(Side, Side) };

    static BuiltThing Town(int seed)
    {
        var site = new BuiltSite { Width = Side, Height = Side, UnitsPerMetre = UnitsPerMetre, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = [], Manner = Manners.Continental };
        return Settlements.Lay(site, "s", new Spot(Side / 2, Side / 2), Radius, seed, 0.8, 0, Plans.Value(Plans.Offered(Manners.Continental)[0]));
    }

    static List<CrowdPerson> People(int seed = 3) => CrowdRoster.Of(Town(seed), Sheet, UnitsPerMetre);

    static int Outside(List<CrowdPerson> people, long clock) => people.Count(p => CrowdDay.Where(p, clock, UnitsPerMetre, out _));

    [Fact]
    public void The_same_town_gives_the_same_people_on_every_machine()
    {
        var a = People();
        var b = People();
        Assert.NotEmpty(a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Nobody_is_out_of_doors_at_three_in_the_morning()
    {
        Assert.Equal(0, Outside(People(), 3 * Hour));
    }

    [Fact]
    public void A_modest_share_is_out_of_doors_in_the_morning()
    {
        var people = People();
        int outside = Outside(people, 10 * Hour + 30 * 60);
        Assert.InRange(outside, people.Count / 20, people.Count * 2 / 5);
    }

    [Fact]
    public void A_walker_is_on_the_way_between_home_and_work()
    {
        var walker = People().First(p => (p.Work.At - p.Home.At).Length > 1);
        var times = CrowdDay.Times(walker, UnitsPerMetre);
        long halfway = walker.Leave + times.LegOne / 2;
        Assert.True(CrowdDay.Where(walker, times, halfway, UnitsPerMetre, out var w));
        Assert.True(w.Moving);
        Assert.Equal(CrowdLeg.ToWork, w.Leg);
        double apart = (walker.Work.At - walker.Home.At).Length;
        Assert.Equal(apart, (w.At - walker.Home.At).Length + (walker.Work.At - w.At).Length, 6);
        Assert.InRange((w.At - walker.Home.At).Length / apart, 0.4, 0.6);
    }

    [Fact]
    public void The_day_repeats_the_next_day()
    {
        var p = People().First(x => CrowdDay.Times(x, UnitsPerMetre).LegOne > 120);
        long morning = p.Leave + 60;
        Assert.True(CrowdDay.Where(p, morning, UnitsPerMetre, out var today));
        Assert.True(CrowdDay.Where(p, morning + CrowdDay.Day, UnitsPerMetre, out var tomorrow));
        Assert.Equal(today, tomorrow);
    }
}
