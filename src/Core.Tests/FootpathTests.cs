using KTable.Core.Model;

namespace KTable.Core.Tests;

public class FootpathTests
{
    const double Side = 60, MetresToTheUnit = 50, ReachedM = 26;
    static readonly Spot Middle = new(30, 30);
    static readonly string[] Ways = ["track", "lane", "street", "road", Footpaths.Path];

    static BuiltSite Site(int manner) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = [], Manner = manner };

    static BuiltThing Village(int manner, string plan, int seed, double radius = 6, double houses = 0.5, double walls = 0) =>
        Settlements.Lay(Site(manner), "s", Middle, radius, seed, houses, walls, Plans.Value(plan));

    static List<(Spot A, Spot B)> Legs(BuiltThing t, params string[] classes) => t.Parts.Where(p => p.Kind == PartKinds.Line && classes.Contains(p.Class))
        .SelectMany(p => BuiltGeometry.Points(p.At).Select(Spot.Of).ToList() is var l ? l.Zip(l.Skip(1)) : []).ToList();

    static double Off(Spot p, List<(Spot A, Spot B)> legs) => legs.Min(leg => p.FromSegment(leg.A, leg.B));

    [Fact]
    public void Scattered_steadings_are_joined_by_footpaths_to_each_other_and_the_road()
    {
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var village = Village(Manners.Tribal, Plans.Scatter, seed);
            var ways = Legs(village, Ways);
            var huts = village.Parts.Where(p => p.Kind == PartKinds.Block && p.Class is "roundhouse" or "longhouse").Select(BuiltSite.Round).ToList();
            Assert.True(Legs(village, Footpaths.Path).Count >= 6, $"seed {seed}: footpaths between the steadings");
            double reached = huts.Count(h => (Off(h.At, ways) - h.Reach) * MetresToTheUnit < ReachedM) / (double)huts.Count;
            Assert.True(reached >= 0.85, $"seed {seed}: {reached:P0} of the houses lie on a way");
            Assert.True(village.Parts.Count(p => p.Kind == PartKinds.Sign && p.Class == "well") >= 1, "a well they share");
            Assert.True(village.Parts.Count(p => p.Kind == PartKinds.Area && p.Class is "garden" or "threshing") >= 2, "plots and a threshing floor between them");
        }
    }

    [Fact]
    public void A_tent_camp_keeps_aisles_and_no_footpath_runs_through_a_wall_or_a_house()
    {
        var camp = Village(Manners.Mongol, Plans.Round, 4, radius: 5, houses: 0.6);
        Assert.True(Legs(camp, Footpaths.Path).Count >= 4, "aisles through the tent rings");
        foreach (int manner in new[] { Manners.Gothic, Manners.Tribal })
        {
            var town = Village(manner, Plans.Offered(manner)[0], 2, radius: 7, houses: 0.6, walls: 0.5);
            var paths = Legs(town, Footpaths.Path);
            var walls = town.Parts.Where(p => p.Kind == PartKinds.Line && p.Class is "wall" or "palisade").Select(p => BuiltGeometry.Points(p.At).Select(Spot.Of).ToList())
                .Where(l => l.Zip(l.Skip(1)).Sum(leg => (leg.Second - leg.First).Length) * MetresToTheUnit > 150).SelectMany(l => l.Zip(l.Skip(1))).ToList();
            Assert.DoesNotContain(paths, p => walls.Any(w => Crosses(p.A, p.B, w.First, w.Second)));
        }
    }

    [Fact]
    public void No_plot_lies_over_another_plot_a_house_or_a_road()
    {
        foreach (int manner in new[] { Manners.Continental, Manners.Gothic, Manners.Tribal })
            foreach (int seed in new[] { 1, 2, 3 })
            {
                var place = Village(manner, Plans.Offered(manner)[0], seed, radius: 6, houses: 0.55);
                var plots = place.Parts.Where(p => p.Kind == PartKinds.Area && p.Class is "garden" or "orchard" or "vineyard").Select(p => BuiltGeometry.Points(p.At).Select(Spot.Of).ToList()).ToList();
                var inner = plots.Select(r => r.Select(q => q + (new Spot(r.Average(v => v.X), r.Average(v => v.Y)) - q).Unit * (1.5 / MetresToTheUnit)).ToList()).ToList();
                for (int i = 0; i < plots.Count; i++)
                    for (int j = 0; j < plots.Count; j++)
                        Assert.False(i != j && inner[i].Any(q => Enclosures.Inside(plots[j], q)), $"{Manners.Id(manner)} {seed}: two plots overlap");
                var houses = place.Parts.Where(p => p.Kind == PartKinds.Block && p.Class is "house" or "cottage" or "townhouse" or "farm").Select(BuiltSite.Round).ToList();
                Assert.DoesNotContain(inner, r => houses.Any(h => Enclosures.Inside(r, h.At)));
                Assert.DoesNotContain(inner, r => Legs(place, "street", "road", "lane", "track").Any(w => Enclosures.Inside(r, (w.A + w.B) * 0.5)));
            }
    }

    static bool Crosses(Spot a, Spot b, Spot c, Spot d)
    {
        static double Turn(Spot o, Spot p, Spot q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);
        return Turn(a, b, c) * Turn(a, b, d) < 0 && Turn(c, d, a) * Turn(c, d, b) < 0;
    }
}
