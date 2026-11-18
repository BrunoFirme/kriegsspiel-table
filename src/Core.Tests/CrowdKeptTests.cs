using KTable.Core.Model;

namespace KTable.Core.Tests;

public class CrowdKeptTests
{
    const double Side = 60, MetresPerUnit = 10, Radius = 12;

    static GameState Table()
    {
        var g = new GameState { ScenarioId = "t" };
        g.Sheets.Add(new SheetDef { Id = "s", Size = new Size2(Side, Side) });
        g.Calibration.UnitsPerMeter = 1 / MetresPerUnit;
        var site = new BuiltSite { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresPerUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = [], Manner = Manners.Continental };
        var built = Settlements.Lay(site, "s", new Spot(Side / 2, Side / 2), Radius, 7, 0.8, 0, Plans.Value(Plans.Offered(Manners.Continental)[0]));
        built.Id = "w-1";
        g.Built.Add(built);
        return g;
    }

    static List<string> Dressed(Nationality people)
    {
        var town = CrowdRoster.Towns(Table(), _ => people, 0).Single();
        return town.Residents.Where(r => !r.Outsider).Select(r => Json.Serialize(r.Entry) + Json.Serialize(CrowdDress.Look(r.Entry, town.People, town.Coat))).ToList();
    }

    [Fact]
    public void An_old_table_opened_on_new_nationality_data_shows_the_same_people_in_the_same_dress_and_gains_only_what_it_lacked()
    {
        var old = CrowdDress.Flat(Nationalities.Named("continental")!);
        (old.Traffic, old.Strangers, old.Day!.Timetables, old.Day.NightLights, old.Day.NightShare) = (null, null, null, null, null);
        var fresh = CrowdDress.Flat(Nationalities.Named("continental")!);
        fresh.Dyes = fresh.Dyes!.ToDictionary(d => d.Key, _ => new List<string> { "#ff00ff" });
        fresh.Skins = ["#00ff00"];
        fresh.Day!.DarkBy = 3;
        Assert.NotEqual(Dressed(old), Dressed(fresh));
        var kept = CrowdDress.Kept(old, fresh);
        Assert.Equal(Dressed(old), Dressed(kept));
        Assert.Equal(fresh.Traffic!.Count, kept.Traffic!.Count);
        Assert.Equal(fresh.Strangers, kept.Strangers);
        Assert.Equal(fresh.Day.Timetables!.Keys.Order(), kept.Day!.Timetables!.Keys.Order());
        Assert.Equal(fresh.Day.NightLights, kept.Day.NightLights);
        Assert.Equal(old.Day.DarkBy, kept.Day.DarkBy);
        Assert.Empty(CrowdDress.Problems(kept));
        Assert.Equal(Json.Serialize(kept), Json.Serialize(CrowdDress.Kept(kept, fresh)));
    }
}
