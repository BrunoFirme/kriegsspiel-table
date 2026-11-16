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

    static bool Within(IReadOnlyList<Spot> ring, Spot p)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            if (ring[i].Y > p.Y != ring[j].Y > p.Y && p.X < (ring[j].X - ring[i].X) * (p.Y - ring[i].Y) / (ring[j].Y - ring[i].Y) + ring[i].X) inside = !inside;
        return inside;
    }

    static bool Crosses(Spot a, Spot b, Spot c, Spot d)
    {
        static double Side(Spot p, Spot q, Spot r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        return Side(a, b, c) * Side(a, b, d) < 0 && Side(c, d, a) * Side(c, d, b) < 0;
    }

    [Fact]
    public void A_castle_gate_is_open_through_its_gatehouse_and_no_curtain_crosses_it()
    {
        int gates = 0;
        foreach (int manner in new[] { Manners.Gothic, Manners.Rus, Manners.Byzantine, Manners.Saracen, Manners.Continental })
            for (int seed = 1; seed <= 6; seed++)
            {
                var castle = Castle(manner, seed);
                var walls = castle.Parts.Where(p => p.Kind == PartKinds.Line && p.Class is "wall" or "palisade" or "wardwall").Select(p => BuiltGeometry.Points(p.At).Select(Spot.Of).ToList()).ToList();
                foreach (var house in castle.Parts.Where(p => p.Kind == PartKinds.Block && p.Class == "gatehouse"))
                {
                    var ring = BuiltGeometry.Points(house.At).Select(Spot.Of).ToList();
                    var mid = new Spot(ring.Average(q => q.X), ring.Average(q => q.Y));
                    var inner = ring.Select(q => mid + (q - mid) * 0.9).ToList();
                    var through = ((ring[1] - ring[0]).Length < (ring[2] - ring[1]).Length ? ring[1] - ring[0] : ring[2] - ring[1]).Unit;
                    var reach = through * (12 / MetresToTheUnit + ((ring[1] - ring[0]).Length < (ring[2] - ring[1]).Length ? (ring[1] - ring[0]).Length : (ring[2] - ring[1]).Length) / 2);
                    foreach (var line in walls)
                        for (int k = 0; k + 1 < line.Count; k++)
                            for (int t = 0; t <= 20; t++)
                                Assert.False(Within(inner, line[k] + (line[k + 1] - line[k]) * (t / 20.0)), $"{Manners.Id(manner)} castle {seed}: a wall runs through its gatehouse");
                    bool Open(Spot way) => !walls.Any(line => Enumerable.Range(0, line.Count - 1).Any(k => Crosses(mid, mid + way, line[k], line[k + 1])));
                    Assert.True(Open(reach) || Open(reach * -1), $"{Manners.Id(manner)} castle {seed}: walls close both ways out of its gate");
                    gates++;
                }
            }
        Assert.True(gates >= 20, $"only {gates} gatehouses laid");
    }

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
