using KTable.Core.Model;

namespace KTable.Core.Tests;

public class TownHallmarkTests
{
    const double Side = 60, MetresToTheUnit = 50, Radius = 7;
    static readonly Spot Middle = new(30, 30);

    static BuiltSite Site(int manner) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = [], Manner = manner };

    static BuiltThing Town(int manner, double radius = Radius, double houses = 0.8, double walls = 0, int seed = 4) =>
        Settlements.Lay(Site(manner), "s", Middle, radius, seed, houses, walls, Plans.Value(Plans.Offered(manner)[0]));

    static int Count(BuiltThing t, string kind, string cls) => t.Parts.Count(p => p.Kind == kind && p.Class == cls);

    static int Signs(BuiltThing t, params string[] classes) => t.Parts.Count(p => p.Kind == PartKinds.Sign && classes.Contains(p.Class));

    static bool Any(int manner, Func<BuiltThing, bool> has, double walls = 0) => Enumerable.Range(1, 5).Any(seed => has(Town(manner, walls: walls, seed: seed)));

    [Fact]
    public void Each_culture_fills_its_market_from_its_own_kit()
    {
        Assert.True(Any(Manners.Gothic, t => Signs(t, Markets.Booth, Markets.Trestle) >= 3), "Gothic booths and trestles");
        Assert.True(Any(Manners.Saracen, t => Signs(t, Markets.Rugs, Markets.Baskets) >= 3), "Saracen rugs and baskets");
        Assert.True(Any(Manners.Chinese, t => Signs(t, Markets.Parasol) >= 1), "Chinese parasols");
        Assert.All(new[] { Manners.Gothic, Manners.Saracen, Manners.Chinese, Manners.Rus, Manners.Egyptian }, m => Assert.Equal(0, Signs(Town(m), Markets.Stall) > 6 ? 1 : 0));
        Assert.Equal("sweep", Markets.Centre(Manners.Northern));
        Assert.All(Enumerable.Range(1, 4), seed => Assert.True(Signs(Town(Manners.Rus, seed: seed), "sweep") <= 1 && Signs(Town(Manners.American, seed: seed), "pump") <= 1, "one centrepiece, not two"));
        Assert.True(Any(Manners.Gothic, t => Signs(t, "fountain") >= 1), "a Gothic town keeps its fountain");
        Assert.All(new[] { Manners.Rus, Manners.Northern, Manners.American }, m => Assert.Equal(0, Signs(Town(m), "fountain")));
        Assert.Equal("pump", Markets.Centre(Manners.American));
    }

    [Fact]
    public void Medieval_streets_are_dirty_and_a_Roman_street_has_its_stepping_stones()
    {
        var gothic = Town(Manners.Gothic);
        Assert.True(Signs(gothic, Streetlife.Puddle) >= 5, "puddles in the Gothic streets");
        Assert.True(Signs(gothic, "dungheap", "pigs") >= 3, "dung and pigs in the street");
        Assert.True(Count(gothic, PartKinds.Line, Streetlife.Gutter) >= 1, "a gutter down the street");
        Assert.True(Any(Manners.Saracen, t => Signs(t, Streetlife.Donkeys, Streetlife.Camels) >= 2), "pack beasts in the Saracen streets");
        Assert.Equal(0, Signs(Town(Manners.Saracen), Streetlife.Puddle, "pigs"));
        Assert.True(Any(Manners.Roman, t => Signs(t, Streetlife.SteppingStones) >= 2), "stepping stones across the Roman streets");
    }

    [Fact]
    public void Every_walled_age_grows_suburbs_at_its_gates_and_the_faithful_bury_outside_them()
    {
        foreach (int manner in new[] { Manners.Roman, Manners.Ottoman, Manners.Gothic })
        {
            var town = Town(manner, walls: 0.5);
            Assert.True(Count(town, PartKinds.Block, "gatehouse") >= 1, Manners.Id(manner) + " has gates");
            Assert.True(town.Parts.Count(p => p.Kind == PartKinds.Block && p.Class is "house" or "villa" or "domus" && (BuiltSite.Round(p).At - Middle).Length > Radius) >= 1,
                Manners.Id(manner) + " grows outside its gates");
        }
        Assert.True(Any(Manners.Saracen, t => Count(t, PartKinds.Block, "tomb") >= 1, 0.5), "domed tombs");
    }

    [Fact]
    public void Each_ancient_culture_raises_its_own_hallmarks()
    {
        Assert.True(Any(Manners.Roman, t => Count(t, PartKinds.Area, Hallmarks.Theatre) == 1), "a Roman theatre");
        Assert.True(Any(Manners.Roman, t => Count(t, PartKinds.Line, Hallmarks.Aqueduct) == 1), "a Roman aqueduct");
        Assert.True(Any(Manners.Persian, t => Signs(t, Hallmarks.Qanat) >= 5), "Persian qanat shafts");
        Assert.All(Enumerable.Range(1, 5), seed =>
        {
            var egyptian = Town(Manners.Egyptian, seed: seed);
            Assert.True(Count(egyptian, PartKinds.Block, "church") >= 1 && Signs(egyptian, Hallmarks.Sphinx) >= 4, $"seed {seed}: every Egyptian town has its temple and its avenue of sphinxes");
        });
        Assert.True(Any(Manners.Chinese, t => Count(t, PartKinds.Block, Hallmarks.DrumTower) == 1 && Count(t, PartKinds.Block, Hallmarks.BellTower) == 1), "drum and bell towers");
        Assert.True(Any(Manners.Tribal, t => Count(t, PartKinds.Area, "pen") >= 1), "a cattle kraal");
    }

    static List<double> Arms(BuiltThing town)
    {
        var gates = town.Parts.Where(p => p.Kind == PartKinds.Block && p.Class == "gatehouse").Select(p => BuiltSite.Round(p).At).ToList();
        return town.Parts.Where(p => p.Kind == PartKinds.Line && p.Class == "street" && p.At.Count == 4).Select(p => BuiltGeometry.Points(p.At).Select(Spot.Of).ToList())
            .Where(l => gates.Any(g => (l[0] - g).Length < 10 / MetresToTheUnit) && (l[1] - Middle).Length > (l[0] - Middle).Length).Select(l => (l[1] - l[0]).Length * MetresToTheUnit).ToList();
    }

    [Fact]
    public void Suburbs_grow_at_every_gate_but_never_as_a_starfish()
    {
        foreach (int manner in new[] { Manners.Chinese, Manners.Gothic, Manners.Roman })
            foreach (int seed in new[] { 1, 2, 3 })
            {
                var town = Town(manner, radius: 9, walls: 0.5, seed: seed);
                var arms = Arms(town).Where(m => m > 70).ToList();
                Assert.True(arms.Count <= 3, $"{Manners.Id(manner)} {seed}: {arms.Count} long arms");
                Assert.Equal(arms.Count, arms.Select(m => Math.Round(m / 12)).Distinct().Count());
            }
        Assert.True(Any(Manners.Chinese, t => Arms(t).Count > 3, 0.5), "the other gates keep small suburbs of their own");
    }

    [Fact]
    public void Later_and_modern_towns_raise_their_own_hallmarks()
    {
        Assert.True(Any(Manners.Continental, t => Count(t, PartKinds.Area, Hallmarks.Parterre) == 1), "a baroque parterre");
        Assert.True(Any(Manners.Ottoman, t => Count(t, PartKinds.Block, Hallmarks.Bedesten) == 1), "a covered bazaar");
        Assert.True(Any(Manners.Japanese, t => Signs(t, Hallmarks.FireTower) >= 1), "a fire watchtower");
        Assert.True(Any(Manners.American, t => Signs(t, Hallmarks.Hitch) >= 2), "hitching rails");
        Assert.True(Any(Manners.NorthAfrican, t => Count(t, PartKinds.Block, "tomb") >= 1 && Signs(t, "column") >= 4), "marabouts and an arcaded street");
        Assert.True(Any(Manners.Pacific, t => Count(t, PartKinds.Block, "greathouse") == 1 && Signs(t, "racks") >= 1), "a meeting house and copra racks");
        Assert.True(Any(Manners.Nippon, t => Count(t, PartKinds.Area, Hallmarks.SchoolYard) == 1 && Count(t, PartKinds.Block, "baths") == 1), "a school yard and a bathhouse");
        Assert.True(Any(Manners.Western, t => Count(Settlements.Lay(Site(Manners.Western), "s", Middle, 10, 4, 0.8, 0, Plans.Value(Plans.Offered(Manners.Western)[0])), PartKinds.Line,
            Hallmarks.Tramway) >= 1), "a tramway in a city");
    }
}
