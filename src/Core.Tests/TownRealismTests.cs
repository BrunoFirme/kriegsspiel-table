using KTable.Core.Model;

namespace KTable.Core.Tests;

public class TownRealismTests
{
    const double Side = 60, MetresToTheUnit = 50;
    static readonly Spot Middle = new(30, 30);

    static BuiltSite Site() => new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = Side };

    static List<BuiltPart> Houses(BuiltThing t) => t.Parts.Where(p => p.Kind == PartKinds.Block && p.Class == "house").ToList();

    static List<Spot> Points(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    static double Frontage(BuiltPart house) => (Points(house)[1] - Points(house)[0]).Length * MetresToTheUnit;

    static double Mean(Func<int, double> of) => Enumerable.Range(1, 5).Average(of);

    [Fact]
    public void A_walled_town_is_built_up_as_towns_were()
    {
        double packed = Mean(seed => Houses(Settlements.Lay(Site(), "s", Middle, 3, seed, 1, 0.5, 0)).Count);
        double usual = Mean(seed => Houses(Settlements.Lay(Site(), "s", Middle, 3, seed, 0.7, 0.5, 0)).Count);
        Assert.True(packed >= 110, $"a packed town 300 m across holds {packed:0} houses, at least 110 looked for");
        Assert.True(usual >= 55 && usual < packed, $"at Houses 70% it holds {usual:0}");
    }

    [Fact]
    public void A_village_and_a_hamlet_keep_their_few_scattered_farmsteads()
    {
        double village = Mean(seed => Houses(Settlements.Lay(Site(), "s", Middle, 3, seed, 0.35, 0, 0)).Count);
        double hamlet = Mean(seed => Houses(Settlements.Lay(Site(), "s", Middle, 1.4, seed, 0.3, 0, 0)).Count);
        Assert.True(village is >= 12 and <= 60, $"a village 300 m across holds {village:0} houses");
        Assert.True(hamlet is >= 2 and <= 14, $"a hamlet 140 m across holds {hamlet:0} houses");
    }

    [Fact]
    public void Town_houses_are_narrow_to_the_street_and_farmhouses_broad()
    {
        var town = Houses(Settlements.Lay(Site(), "s", Middle, 3, 2, 1, 0.5, 0)).Where(h => h.At.Count == 8).ToList();
        var village = Houses(Settlements.Lay(Site(), "s", Middle, 3, 2, 0.3, 0, 0)).Where(h => h.At.Count == 8).ToList();
        Assert.True(town.Average(Frontage) < 8.8, $"town frontage {town.Average(Frontage):0.0} m");
        Assert.True(village.Average(h => Points(h).Max(p => (p - Points(h)[0]).Length) * MetresToTheUnit) > 12, "a farmhouse is a long house");
        Assert.DoesNotContain(Houses(Settlements.Lay(Site(), "s", Middle, 3, 2, 1, 0.5, 0)), h => h.At.Count == 12);
    }

    [Fact]
    public void A_town_church_is_larger_than_a_village_one()
    {
        double Long(BuiltThing t) => t.Parts.Where(p => p.Class == "church").Select(p => (Points(p)[1] - Points(p)[0]).Length * MetresToTheUnit).DefaultIfEmpty(0).Max();
        Assert.Equal(24, Long(Settlements.Lay(Site(), "s", Middle, 3, 2, 0.5, 0, 0)), 0);
        Assert.True(Long(Settlements.Lay(Site(), "s", Middle, 7, 2, 0.7, 0, 0)) > 32);
    }
}
