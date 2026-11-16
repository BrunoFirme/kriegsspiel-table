using KTable.Core.Model;

namespace KTable.Core.Tests;

public class DrawnStreetsTests
{
    const double Side = 60, MetresToTheUnit = 25, Reach = 9, OnTheLine = 0.05, Joined = 0.3;
    static readonly Spot Middle = new(30, 30);

    static readonly (Spot A, Spot B)[] Plan =
    [
        (new Spot(19, 33), new Spot(41, 33)),
        (new Spot(36, 22), new Spot(36, 38)),
    ];

    static BuiltThing[] Laid(string cls) =>
        Plan.Select(line => new BuiltThing { Kind = BuiltKinds.Road, Parts = { BuiltSite.Line(cls, [line.A, line.B]) } }).ToArray();

    static BuiltSite Site(int manner, params BuiltThing[] others) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = 1, Others = others, Manner = manner };

    static BuiltThing Town(BuiltSite site, int seed, string plan = Plans.Grown, Spot? at = null) =>
        Settlements.Lay(site, "s", at ?? Middle, Reach, seed, 0.8, 0, Plans.Value(plan), false, null, new Settlements.Shape());

    static List<List<Spot>> Streets(BuiltThing town) =>
        town.Parts.Where(p => p.Kind == PartKinds.Line && p.Class is "street" or "lane" or "road" or "track").Select(p => BuiltGeometry.Points(p.At).Select(Spot.Of).ToList()).ToList();

    static double FromPlan(Spot p) => Plan.Min(line => p.FromSegment(line.A, line.B));

    static double Long(List<Spot> line) => line.Zip(line.Skip(1)).Sum(leg => (leg.Second - leg.First).Length);

    static bool EndsInTheMiddle(BuiltThing town, Spot middle) =>
        Streets(town).Where(line => line.Any(p => FromPlan(p) > OnTheLine)).Any(line => (line[0] - middle).Length < Joined || (line[^1] - middle).Length < Joined);

    [Theory]
    [InlineData(Manners.Continental, Plans.Grown)]
    [InlineData(Manners.Continental, Plans.Grid)]
    [InlineData(Manners.Roman, Plans.Grid)]
    [InlineData(Manners.Ottoman, Plans.Maze)]
    public void Drawn_streets_are_the_town_s_main_streets_and_it_grows_its_side_streets_off_them(int manner, string plan)
    {
        var drawn = Site(manner, Laid("street"));
        var roads = Site(manner, Laid("road"));
        int ordinary = 0;
        foreach (int seed in Enumerable.Range(1, 4))
        {
            var town = Town(drawn, seed, plan);
            var own = Streets(town).Where(line => line.Any(p => FromPlan(p) > OnTheLine)).ToList();
            Assert.True(own.Count > 0, $"seed {seed}: it grows streets of its own off the drawn ones");
            Assert.False(EndsInTheMiddle(town, Middle), $"seed {seed}: it runs no main street of its own into its middle");
            if (EndsInTheMiddle(Town(roads, seed, plan), Middle)) ordinary++;
            var all = Streets(town);
            bool Meets(Spot p, List<Spot> line) => FromPlan(p) < Joined || all.Any(other => other != line && other.Zip(other.Skip(1)).Any(leg => p.FromSegment(leg.First, leg.Second) < Joined));
            Assert.All(own, line => Assert.True(line.Any(p => Meets(p, line)),
                $"seed {seed}: a street at {line[0]} joins the others"));
        }
        Assert.True(ordinary > 0, "with roads in place of the drawn streets, it runs main streets of its own into its middle");
    }

    [Fact]
    public void Another_town_s_streets_are_not_drawn_streets()
    {
        var site = Site(Manners.Continental);
        var first = Town(site, 3, at: new Spot(24, 30));
        var beside = Town(Site(Manners.Continental, first), 5, at: new Spot(38, 30));
        Assert.True(EndsInTheMiddle(beside, new Spot(38, 30)), "a town beside another still runs main streets of its own into its middle");
    }
}
