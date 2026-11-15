using KTable.Core.Model;

namespace KTable.Core.Tests;

public class TownHistoryTests
{
    const double Side = 60, MetresToTheUnit = 50;
    static readonly Spot Middle = new(30, 30);

    static BuiltSite Site(int manner, ReliefGrid? grid = null) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = grid ?? ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = [], Manner = manner };

    static BuiltThing Town(int manner, double radius = 7, double houses = 0.8, double walls = 0, int seed = 4, double ravaged = 0, string? plan = null) =>
        Settlements.Lay(Site(manner), "s", Middle, radius, seed, houses, walls, Plans.Value(plan ?? Plans.Offered(manner)[0]), shape: new Settlements.Shape(Ravaged: ravaged));

    static int Count(BuiltThing t, string kind, string cls) => t.Parts.Count(p => p.Kind == kind && p.Class == cls);

    static bool Any(int manner, Func<BuiltThing, bool> has, double radius = 7, double walls = 0) => Enumerable.Range(1, 6).Any(seed => has(Town(manner, radius, walls: walls, seed: seed)));

    [Fact]
    public void Village_streets_are_earth_and_town_streets_paved_where_rich_or_main()
    {
        Assert.Equal(0, Count(Town(Manners.Continental, radius: 3, houses: 0.4), PartKinds.Line, Settlements.Paved));
        Assert.True(Count(Town(Manners.Continental, radius: 8), PartKinds.Line, Settlements.Paved) >= 1, "a town paves some of its streets");
        Assert.Equal(0, Count(Town(Manners.Saracen, radius: 8), PartKinds.Line, Settlements.Paved));
    }

    [Fact]
    public void War_damage_is_of_its_age()
    {
        var sacked = Town(Manners.Gothic, ravaged: 0.8);
        Assert.True(Count(sacked, PartKinds.Block, Settlements.Ruin) >= 10 && Count(sacked, PartKinds.Sign, Settlements.Crater) + Count(sacked, PartKinds.Sign, Settlements.BombCrater) == 0,
            "a medieval sack burns, it does not shell");
        var bombed = Town(Manners.Western, ravaged: 0.8);
        Assert.True(Count(bombed, PartKinds.Block, Settlements.Rubble) >= 10, "a bombed town lies in rubble");
        Assert.True(Count(bombed, PartKinds.Sign, Settlements.BombCrater) >= 2 && Count(bombed, PartKinds.Sign, Settlements.Crater) >= 2, "with bomb and shell craters");
        var shelled = Town(Manners.Continental, ravaged: 0.8);
        Assert.True(Count(shelled, PartKinds.Sign, Settlements.Crater) >= 1 && Count(shelled, PartKinds.Sign, Settlements.BombCrater) == 0, "a bombardment of the age of guns");
    }

    [Fact]
    public void A_castle_town_turns_its_castle_gate_to_the_town_and_a_street_leads_up_to_it()
    {
        Assert.True(Any(Manners.Gothic, t =>
        {
            if (t.Parts.FirstOrDefault(p => p.Kind == PartKinds.Area && p.Class == Forts.Platform) is not { } ward) return false;
            var keep = BuiltSite.Round(ward).At;
            var gate = t.Parts.Where(p => p.Kind == PartKinds.Block && p.Class == "gatehouse").Select(p => BuiltSite.Round(p).At).MinBy(g => (g - keep).Length);
            var square = t.Parts.Where(p => p.Kind == PartKinds.Area && p.Class == "square").Select(p => BuiltSite.Round(p).At).FirstOrDefault();
            return square != default && (gate - square).Length < (keep - square).Length;
        }, radius: 8), "the castle gate faces the town");
    }

    [Fact]
    public void Noxious_trades_sit_downstream_and_rich_gardens_are_walled()
    {
        Assert.True(Any(Manners.Gothic, t => Count(t, PartKinds.Line, Settlements.GardenWall) >= 1, radius: 7), "rich plots behind walls");
        var town = Settlements.Lay(Site(Manners.Continental), "s", Middle, 7, 3, 0.8, 0, Plans.Value(Plans.Grown));
        Assert.True(Count(town, PartKinds.Block, "tanner") >= 1 && Count(town, PartKinds.Sign, "pits") >= 1, "a tannery by its pits");
    }

    [Fact]
    public void Towns_grow_in_layers_of_their_history()
    {
        Assert.True(Count(Town(Manners.Western, radius: 10), PartKinds.Line, Settlements.Boulevard) >= 1, "a ring boulevard where the walls stood");
        Assert.Equal(0, Count(Town(Manners.Western, radius: 5), PartKinds.Line, Settlements.Boulevard));
        Assert.Equal(0, Count(Town(Manners.Continental, radius: 10), PartKinds.Line, Settlements.Boulevard));
    }

    [Fact]
    public void A_1940s_town_has_its_school_memorial_shelters_and_filling_station_and_a_Soviet_one_its_collective_farm()
    {
        Assert.True(Any(Manners.Western, t => Count(t, PartKinds.Area, Hallmarks.SchoolYard) == 1 && Count(t, PartKinds.Sign, Hallmarks.Memorial) >= 1), "school and war memorial");
        Assert.True(Any(Manners.Western, t => Count(t, PartKinds.Block, Hallmarks.Garage) >= 1 && Count(t, PartKinds.Sign, Hallmarks.FuelPump) >= 2), "a filling station");
        Assert.True(Any(Manners.Western, t => Count(t, PartKinds.Sign, Hallmarks.Shelter) >= 2), "air-raid shelters");
        Assert.True(Any(Manners.Soviet, t => Count(t, PartKinds.Sign, Hallmarks.Tractor) >= 3 && Count(t, PartKinds.Area, "yard") >= 1), "a collective farm");
        Assert.True(Any(Manners.Soviet, t => Count(t, PartKinds.Line, Hallmarks.Boardwalk) >= 2), "boardwalks along the streets");
        Assert.True(Any(Manners.Western, t => Count(t, PartKinds.Area, "churchyard") >= 2), "a cemetery besides the churchyard");
    }
}
