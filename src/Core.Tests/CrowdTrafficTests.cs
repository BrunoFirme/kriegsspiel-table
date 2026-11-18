using KTable.Core.Model;

namespace KTable.Core.Tests;

public class CrowdTrafficTests
{
    const double Side = 80, MetresPerUnit = 10, Radius = 16;
    const int Hour = CalendarDef.SecondsInHour;

    static CrowdTown Town(string? people = null)
    {
        var g = new GameState { ScenarioId = "t" };
        g.Sheets.Add(new SheetDef { Id = "s", Size = new Size2(Side, Side) });
        g.Calibration.UnitsPerMeter = 1 / MetresPerUnit;
        var site = new BuiltSite { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresPerUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = [], Manner = Manners.Continental };
        var built = Settlements.Lay(site, "s", new Spot(Side / 2, Side / 2), Radius, 5, 0.8, 0, Plans.Value(Plans.Offered(Manners.Continental)[0]));
        built.Id = "w-1";
        g.Built.Add(built);
        var town = CrowdRoster.Towns(g, id => CrowdDress.Find(id) is { } n ? CrowdDress.Flat(n) : null, 0).Single();
        if (people == null) return town;
        var flat = CrowdDress.Flat(CrowdDress.Find(people)!);
        return new CrowdTown { Thing = town.Thing, People = flat, Coat = town.Coat, Places = town.Places, Residents = town.Residents };
    }

    [Fact]
    public void Every_nationality_ships_rigs_that_hold_together_and_are_driven_by_its_own_callings()
    {
        foreach (var n in Nationalities.All)
        {
            var flat = CrowdDress.Flat(n);
            Assert.True(flat.Traffic is { Count: >= 5 }, $"{n.Id} has its period's traffic");
            Assert.Empty(CrowdDress.Problems(flat));
            foreach (var rig in flat.Traffic!)
                foreach (var p in rig.Parts.Where(p => p.Is is RigParts.Walk or RigParts.Sit or RigParts.Ride or RigParts.Bear))
                    Assert.True(flat.Callings?.ContainsKey("m:" + p.What) == true, $"{n.Id} {rig.Id}: {p.What} is a calling of its people");
        }
    }

    [Fact]
    public void Edo_has_no_animal_drawn_wheels_Rome_moves_heavy_carts_by_night_and_trams_belong_to_the_modern_town()
    {
        static bool Drawn(TrafficRig r) => r.Parts.Any(p => p.Is == RigParts.Vehicle && p.What != "chair") && r.Parts.Any(p => p.Is == RigParts.Animal);
        Assert.DoesNotContain(CrowdDress.Flat(Nationalities.Named("japanese")!).Traffic!, Drawn);
        Assert.Contains(CrowdDress.Flat(Nationalities.Named("roman")!).Traffic!, r => r.Night && r.Parts.Any(p => p.What == "ox"));
        foreach (var n in Nationalities.All)
            Assert.Equal(n.Id == "modern", CrowdDress.Flat(n).Traffic!.Any(r => r.Role == TrafficRoles.Tram));
    }

    [Fact]
    public void A_town_day_sends_traffic_in_from_its_edges_and_each_stay_ends_in_a_trip_back()
    {
        var town = Town();
        var ends = CrowdTraffic.Ends(town);
        Assert.True(ends.Length >= 2, "a town with streets has ways in and out");
        var trips = CrowdTraffic.Plan(town, DayKind.Working, 3, town.Residents.Count);
        Assert.NotEmpty(trips);
        Assert.All(trips, t => Assert.InRange(t.Depart, 0, CrowdDay.Day - 1));
        Assert.All(trips, t => Assert.NotEqual(t.From, t.To));
        Assert.Contains(trips, t => ends.Contains(t.From));
        foreach (var stay in trips.Where(t => t.Until > 0))
            Assert.Contains(trips, back => back.Seed == stay.Seed && back.From == stay.To && back.To == stay.From && back.Depart == stay.Until);
        Assert.Equal(trips, CrowdTraffic.Plan(town, DayKind.Working, 3, town.Residents.Count));
    }

    [Fact]
    public void Market_days_bring_more_farm_carts_and_night_rigs_leave_only_in_the_dark()
    {
        var town = Town();
        var rigs = town.People.Traffic!;
        int Farm(DayKind kind) => Enumerable.Range(1, 6).Sum(d => CrowdTraffic.Plan(town, kind, d, town.Residents.Count).Count(t => rigs[t.Rig].Role == TrafficRoles.Farm));
        Assert.True(Farm(DayKind.Market) > Farm(DayKind.Working), $"market {Farm(DayKind.Market)} against {Farm(DayKind.Working)}");
        var rome = Town("roman");
        double dark = rome.People.Day?.DarkBy ?? 21;
        var night = Enumerable.Range(1, 6).SelectMany(d => CrowdTraffic.Plan(rome, DayKind.Working, d, rome.Residents.Count))
            .Where(t => rome.People.Traffic![t.Rig].Night).ToList();
        Assert.NotEmpty(night);
        Assert.All(night.Where(t => t.Until > 0), t => Assert.True(t.Depart / (double)Hour >= dark || t.Depart / (double)Hour <= 5, $"a plaustrum leaves at {t.Depart / (double)Hour:0.0} h"));
    }

    [Fact]
    public void Every_vehicle_and_animal_is_a_closed_shape_with_turning_wheels_or_striding_legs()
    {
        foreach (string v in RigParts.Vehicles)
        {
            var shape = RigShapes.Vehicle(v, v == "chair" ? "sedan" : null, "sacks", false);
            Assert.True(shape.Tris > 10, v);
            Assert.All(shape.Triangles, i => Assert.InRange(i, 0, shape.Points.Count - 1));
            Assert.Equal(v != "chair", shape.Limb.Contains(RigShapes.Wheel));
        }
        foreach (string a in RigParts.Animals)
            foreach (string tack in RigParts.Tacks)
            {
                var shape = RigShapes.Animal(a, tack);
                Assert.Equal(4, Enumerable.Range(RigShapes.UpperLeg, 4).Count(l => shape.Limb.Contains((byte)l)));
                Assert.True(shape.Points.Max(p => p.Y) > RigShapes.BackOf(a) * 0.8, $"{a} stands as tall as its back");
            }
    }
}
