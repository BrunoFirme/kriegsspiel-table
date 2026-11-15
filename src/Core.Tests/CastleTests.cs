using KTable.Core.Model;

namespace KTable.Core.Tests;

public class CastleTests
{
    const double Side = 60, MetresToTheUnit = 50;
    static readonly Spot Middle = new(30, 30);
    static readonly string[] Plans = [Castles.Motte, Castles.Enclosure, Castles.Quadrangle, Castles.Concentric, Castles.Spur, Castles.Kremlin, Castles.Kastron, Castles.Citadel, Castles.Nested];

    static BuiltSite Site(int manner) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = [], Manner = manner };

    static BuiltThing Castle(int manner, int seed) => Defences.Lay(Site(manner), "s", Defences.Castle, Middle, 3, seed, 0, 0, seed * 0.7);

    static string Plan(BuiltThing castle) => Plans[(int)castle.Setting("plan", 0)];

    static string Print(BuiltThing castle) => string.Join(",", castle.Parts.GroupBy(p => p.Kind + p.Class).OrderBy(g => g.Key).Select(g => g.Key + g.Count()));

    [Fact]
    public void Castles_differ_in_plan_towers_and_works_by_culture_and_seed()
    {
        var gothic = Enumerable.Range(1, 14).Select(seed => Castle(Manners.Gothic, seed)).ToList();
        Assert.True(gothic.Select(Plan).Distinct().Count() >= 4, "a Gothic castle comes in several plans: " + string.Join(" ", gothic.Select(Plan)));
        Assert.True(gothic.Select(Print).Distinct().Count() >= 12, "and hardly two come out alike");
        Assert.All(gothic, c => Assert.True(c.Parts.Count(p => p.Kind == PartKinds.Area && p.Class == Forts.Platform) >= 1 && c.Parts.Any(p => p.Class == "gatehouse")));
        Assert.All(Enumerable.Range(1, 4), seed => Assert.Equal(Castles.Kremlin, Plan(Castle(Manners.Rus, seed))));
        Assert.All(Enumerable.Range(1, 4), seed => Assert.Equal(Castles.Nested, Plan(Castle(Manners.Japanese, seed))));
        Assert.Contains(Enumerable.Range(1, 6), seed => Plan(Castle(Manners.Saracen, seed)) == Castles.Citadel && Castle(Manners.Saracen, seed).Parts.Any(p => p.Class == Castles.RoundTower));
        Assert.Contains(Enumerable.Range(1, 6), seed => Plan(Castle(Manners.Byzantine, seed)) == Castles.Kastron);
    }

    [Fact]
    public void Fortresses_vary_and_the_Roman_camp_keeps_its_standard_plan()
    {
        var forts = Enumerable.Range(1, 8).Select(seed => Defences.Lay(Site(Manners.Saracen), "s", Defences.Fortress, Middle, 3, seed, 0, 0, 0)).ToList();
        Assert.True(forts.Select(Print).Distinct().Count() >= 6, "fortresses come out differently");
        var camps = Enumerable.Range(1, 3).Select(seed => Defences.Lay(Site(Manners.Roman), "s", Defences.Camp, Middle, 3, seed, 0, 0, 0)).ToList();
        Assert.Single(camps.Select(Print).Distinct());
    }
}
