using System.Diagnostics;
using KTable.Core.Model;

namespace KTable.Core.Tests;

public class EarthworkLineTests
{
    const double Side = 60, PerMetre = 0.02, Far = 1.5;
    const int Steps = 40;

    static BuiltThing With(params BuiltPart[] parts)
    {
        var thing = new BuiltThing { Kind = BuiltKinds.Settlement, Sheet = "s" };
        thing.Parts.AddRange(parts);
        return thing;
    }

    static List<Spot> Hexagon(double reach) => Enumerable.Range(0, 7).Select(i => new Spot(30 + reach * Math.Cos(i * Math.PI / 3 + 0.2), 30 + reach * Math.Sin(i * Math.PI / 3 + 0.2))).ToList();

    static Func<Spot, float> Reader(ReliefGrid grid, Earthworks.Laid laid, float[] of)
    {
        int cols = (grid.Cols - 1) * laid.Times + 1, rows = (grid.Rows - 1) * laid.Times + 1;
        return p => of[(int)Math.Round(p.Y / Side * (rows - 1)) * cols + (int)Math.Round(p.X / Side * (cols - 1))];
    }

    [Fact]
    public void A_ditch_cuts_the_ground_along_every_stretch_of_its_line_and_nowhere_away_from_it()
    {
        var grid = ReliefGrid.Flat(Side, Side);
        var ring = Hexagon(12);
        var laid = Earthworks.Lay(grid, [With(BuiltSite.Line("ditch", ring))], Side, Side, PerMetre, Side)!;
        var cut = Reader(grid, laid, laid.Cut);
        for (int edge = 0; edge + 1 < ring.Count; edge++)
            for (int step = 0; step <= Steps; step++)
            {
                var on = ring[edge] + (ring[edge + 1] - ring[edge]) * (step / (double)Steps);
                var across = (ring[edge + 1] - ring[edge]).Unit.Left;
                Assert.True(cut(on) < 0, $"no cut on the line at {on.X:0.00}, {on.Y:0.00}");
                Assert.Equal(0, cut(on + across * Far));
                Assert.Equal(0, cut(on - across * Far));
            }
        Assert.Equal(0, cut(new Spot(30, 30)));
        Assert.All(laid.Raised, v => Assert.Equal(0, v));
    }

    [Fact]
    public void A_rampart_raises_the_ground_along_a_slanting_line_as_much_at_its_ends_as_at_its_middle()
    {
        var grid = ReliefGrid.Flat(Side, Side);
        Spot from = new(12, 14), to = new(47, 41);
        var laid = Earthworks.Lay(grid, [With(BuiltSite.Line("rampart", [from, to]))], Side, Side, PerMetre, Side)!;
        var raised = Reader(grid, laid, laid.Raised);
        float middle = raised(from + (to - from) * 0.5);
        Assert.True(middle > 0);
        for (int step = 1; step < Steps; step++) Assert.Equal(middle, raised(from + (to - from) * (step / (double)Steps)), 3);
        Assert.Equal(0, raised(from + (to - from).Unit.Left * Far));
        Assert.Equal(0, raised(new Spot(47, 14)));
    }

    [Fact]
    public void Works_fitted_only_near_their_lines_shape_the_ground_exactly_as_when_every_cell_is_tried()
    {
        var grid = ReliefGrid.Flat(Side, Side);
        for (int i = 0; i < grid.H.Length; i++) grid.H[i] = (float)(0.003 * Math.Sin(i % grid.Cols * 0.05) * Math.Cos(i / grid.Cols * 0.04));
        static Spot Wobble(int i) => new(20 + (4 + 0.4 * Math.Sin(i * 1.7)) * Math.Cos(i * Math.PI / 20), 22 + (4 + 0.4 * Math.Cos(i * 2.3)) * Math.Sin(i * Math.PI / 20));
        var wobbly = Enumerable.Range(0, 41).Select(Wobble).ToList();
        var square = new List<Spot> { new(36, 10), new(44, 10), new(44, 16), new(36, 16), new(36, 10) };
        var works = With(BuiltSite.Line("ditch", wobbly), BuiltSite.Line("moat", square), BuiltSite.Line("rampart", [new(30, 34), new(47, 41), new(52, 38)]), BuiltSite.Line("dam", [new(12, 44), new(12.6, 44.1)]),
            BuiltSite.Line("trench", [new(0.2, 50), new(8, 59.9)]));
        var near = Earthworks.Lay(grid, [works], Side, Side, PerMetre, Side)!;
        var every = Earthworks.Lay(grid, [works], Side, Side, PerMetre, Side, everyCell: true)!;
        Assert.True(near.Cut.Count(v => v < 0) > 1000 && near.Raised.Count(v => v > 0) > 500);
        Assert.True(near.Raised.AsSpan().SequenceEqual(every.Raised));
        Assert.True(near.Cut.AsSpan().SequenceEqual(every.Cut));
    }

    [Fact]
    [Trait("Category", "Timing")]
    public void The_works_of_a_walled_town_are_fitted_to_the_ground_in_a_moment()
    {
        var grid = ReliefGrid.Flat(80, 60);
        var site = new BuiltSite { Width = 80, Height = 60, UnitsPerMetre = PerMetre, Grid = grid, HeightUnit = 80, Manner = Manners.Continental };
        var town = Settlements.Lay(site, "s", new Spot(40, 26), 7, 11, 0.6, 0.5, Plans.Value(Plans.Offered(Manners.Continental)[0]));
        Assert.Contains(town.Parts, p => p.Kind == PartKinds.Line && p.Class == "ditch");
        var watch = Stopwatch.StartNew();
        Assert.NotNull(Earthworks.Lay(grid, [town], 80, 60, PerMetre, 80));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"fitting a walled town's works took {watch.Elapsed.TotalSeconds:0.0} s");
    }
}
